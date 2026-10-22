using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Hud;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;
using Xunit.Abstractions;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// Godot-free replica of game/src/Hud/BreakBarSceneTest.ps1 (lane BREAK-BAR, BIBLE s9 mechanic 2: the Twins "share one
/// Break bar"). The scene test reads the boss frame's "HUD breakbar" proof lines from one default autorun of
/// <see cref="Ticks"/> ticks; this test drives the same default run headlessly (as <see cref="BossTeachesTests"/> does:
/// HostRunSetup.Build at key 2 seed 1 with bots, BossRules.Arm, ScriptedWarden on seat 1, BotCommandSource on 2 and 3,
/// Backfill.Step every tick) and rebuilds each proof line from the same three calls Hud/BreakBar.cs makes after every step
/// (UnitFrameModel.Build, BossBreakBarReadout.For, BreakBarRule.For; a line whenever bar, current or phase changes).
/// It then asserts the scene test's checks a-d on those lines and the step's events:
/// a. the first line follows the Twins' boss_add_spawn and names boss 900, an empty filling line follows the spawn, and
///    every line names one bar;
/// b. every fill is the sim's value, each fill cycle's filling lines rise, and every step's bar drop is exactly the
///    drain of that step's tagged casts at a Twin (R-BREAKBAR-1: 50 per interrupt-tagged kick, so two kicks on one tick
///    drain 100, plus DrainPerControl per control-tagged cast);
/// c. a stagger on that bar, and a fill=1000 broken line no earlier than it;
/// d. a fill=0 filling line after the broken one (the bar reset).
/// The partial fill (a filling 500 line) is proven by its own deterministic one-kick case, not by the seed-1 autorun:
/// R-BREAKBAR-1 / R-ORACLE-1 (2026-10-22) retired "some autorun cycle shows a fill above 0", which held only while the
/// bots' two kicks happened to land on different ticks (#569's boss-refuses-blind moved them onto one tick).
/// Why it exists (S3 scene-test split, PR #558): the glob step first ran BreakBarSceneTest on 73c613f5, where b and c
/// failed (one rising line, no break in 7000 ticks). That tree predates R-BOSS-1 (#561, whose PR states the default
/// fight had 0 Twins staggers before it); this replica pins a-d headlessly on the current tree so game-test, not only
/// game-e2e, goes red if the bar stops breaking in the default run.
/// </summary>
public sealed class BreakBarSceneReplicaTests
{
    /// <summary>BreakBarSceneTest.ps1's -Ticks default.</summary>
    private const int Ticks = 7000;

    private const int Key = 2;
    private const long Seed = 1;
    private const int BossId = 900;

    /// <summary>R-BOSS-1 (#561) / R-BREAKBAR-1: one tagged kick at a Twin drains 50 of the 100 bar.</summary>
    private const long DrainPerKick = 50;

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test with xUnit's output sink (the proof lines are printed for the PR).</summary>
    /// <param name="output">The sink.</param>
    public BreakBarSceneReplicaTests(ITestOutputHelper output) => _output = output;

    private sealed record Line(int Step, string Bar, int Boss, int Tick, long Current, long Max, int Fill, bool Broken)
    {
        public override string ToString() => FormattableString.Invariant(
            $"HUD breakbar boss={Boss} bar={Bar} tick={Tick} current={Current} max={Max} fill={Fill} phase={(Broken ? "broken" : "filling")}");
    }

    /// <summary>One step's drain: the bar before (after its reset tick), its tagged kicks, and the bar after.</summary>
    private sealed record Drain(int Tick, long Before, int Kicks, int Controls, long After);

    private static string? Str(SimEvent e, string key) => e.Get(key) is JsString s ? s.Value : null;

    /// <summary>The default autorun, with the hooks the two facts need.</summary>
    private sealed class Autorun
    {
        public HostRunSetup Setup { get; }
        public Keyrun.Sim.Runs.Run Run { get; private set; }
        public IReadOnlyDictionary<string, IReadOnlyList<string>?> Tags { get; }
        private readonly ScriptedWarden _warden = new();
        private readonly BotCommandSource _bots;
        private BackfillState _backfill = BackfillState.Empty;

        public Autorun()
        {
            var registry = ContentLoader.Load();
            Setup = HostRunSetup.Build(registry, "hollow-vault", Key, Lobby.DefaultClassId, withBots: true);
            var start = Setup.StartFor(null, null, registry, Lobby.DefaultClassId);
            var options = (start.Options ?? new RunOptions()) with { RotationIndex = Setup.RotationIndex };
            var party = Setup.PartyFor(Setup.Dungeon);
            options = Setup.WithBotLoadouts(options, party) ?? options;
            var created = RunRoot.CreateRun(Seed, party, Setup.Dungeon, Setup.Content, options);
            Run = created with { Encounter = BossRules.Arm(created.Encounter, Setup.Boss) };
            _bots = new BotCommandSource(Setup.BotActorIds, KitRegistry.From(registry), Seed, BotCommandSource.DefaultSkillDial, null);
            _bots.Reset();
            Tags = RunWiring.AbilityTagsOf(Run.Content.Abilities);
        }

        public bool Done => Run.State.Tick >= Ticks || Run.RunEnd.ResultTick is not null;

        public List<Command> Sample()
        {
            var tick = Run.State.Tick;
            var commands = new List<Command>();
            _warden.Sample(tick, Run, commands, _backfill.Open.Select(s => s.ActorId).ToList());
            _bots.Sample(tick, Run, commands);
            _bots.TakeReactions();
            return commands;
        }

        /// <summary>The Twins' bar inputs these commands derive (BreakBar.DeriveInputs, as EncounterStep calls it).</summary>
        public IReadOnlyList<BreakBarInput> Kicks(IReadOnlyList<Command> commands) =>
            BreakBar.DeriveInputs(commands, Run.State.Actors, Tags, Setup.Boss.TwinsBar)
                .Where(i => i.Tick == Run.State.Tick).ToList();

        /// <summary>The bar before this step's drain: refilled on its reset tick, full on the step it is created.</summary>
        public long BarBeforeDrain()
        {
            var src = Setup.Boss.TwinsBar;
            return Run.Encounter.BreakBars.TryGetValue(src.Id, out var bar)
                ? BreakBar.TickReset(bar, Run.State.Tick).Current
                : src.Max;
        }

        public bool BarExists => Run.Encounter.BreakBars.TryGetValue(Setup.Boss.TwinsBar.Id, out _);

        public IReadOnlyList<SimEvent> Step(IReadOnlyList<Command> commands)
        {
            var stepped = Backfill.Step(_backfill, Run, commands.ToList(), Array.Empty<QueueRequest>());
            _backfill = stepped.Backfill;
            Run = stepped.Step.Run;
            return stepped.Step.Events;
        }

        /// <summary>Hud/BreakBar.cs Current(), without Godot: the boss frame's bar and its look, or null.</summary>
        public (BossBreakBar Bar, BreakBarLook Look)? Readout()
        {
            foreach (var frame in UnitFrameModel.Build(Run.State, Setup.HumanActorId))
            {
                if (frame.Kind != "boss" || BossBreakBarReadout.For(Run.Encounter, frame.ActorId) is not { } bar)
                {
                    continue;
                }
                var look = BreakBarRule.For(bar.Bar.Current, bar.Bar.Max, bar.Bar.CooldownReadyTick, Run.State.Tick, Keyrun.Sim.Encounter.BreakBar.ResetCooldownTicks);
                return (bar, look);
            }
            return null;
        }
    }

    [Fact(DisplayName = "BreakBarSceneTest replica: in the default 7000-tick autorun the Twins' shared Break bar fills, breaks on a stagger and resets")]
    public void Default_autorun_break_bar_fills_breaks_and_resets()
    {
        var auto = new Autorun();
        var lines = new List<Line>();
        var drains = new List<Drain>();
        int spawnStep = -1;
        var staggers = new List<(int Step, string Bar)>();
        string? lastKey = null;
        for (var step = 0; !auto.Done; step++)
        {
            var commands = auto.Sample();
            var tick = auto.Run.State.Tick;
            var kicks = auto.Kicks(commands);
            var before = auto.BarBeforeDrain();
            var events = auto.Step(commands);
            // The step's EVENT lines print before its HUD proof lines (SimDriver.StepOnce, then Stepped).
            foreach (var e in events)
            {
                if (spawnStep < 0 && e.Type == "boss_add_spawn") { spawnStep = step; }
                if (e.Type == "stagger") { staggers.Add((step, Str(e, "barId") ?? string.Empty)); }
            }
            if (auto.Readout() is { } r)
            {
                drains.Add(new Drain(tick, before, kicks.Count(k => k.Tag == BreakBarTag.Interrupt), kicks.Count(k => k.Tag == BreakBarTag.Control), r.Bar.Bar.Current));
                var broken = r.Look.Phase == BreakBarPhase.Broken;
                var key = FormattableString.Invariant($"{r.Bar.BarId}:{r.Bar.Bar.Current}:{(broken ? "broken" : "filling")}");
                if (key != lastKey)
                {
                    lastKey = key;
                    lines.Add(new Line(step, r.Bar.BarId, BossId, auto.Run.State.Tick, r.Bar.Bar.Current, r.Bar.Bar.Max, r.Look.FillPermille, broken));
                }
            }
        }
        foreach (var l in lines) { _output.WriteLine(l.ToString()); }
        foreach (var d in drains.Where(d => d.Kicks + d.Controls > 0)) { _output.WriteLine(FormattableString.Invariant($"drain tick={d.Tick} before={d.Before} kicks={d.Kicks} controls={d.Controls} after={d.After}")); }
        _output.WriteLine(FormattableString.Invariant($"end tick={auto.Run.State.Tick} runEnd={auto.Run.RunEnd.ResultTick} spawnStep={spawnStep} staggers={staggers.Count}"));

        // a.
        Assert.NotEmpty(lines);
        var first = lines[0];
        Assert.True(spawnStep >= 0, "no boss_add_spawn (the Twins never came up)");
        Assert.True(first.Step >= spawnStep, $"a Break bar line before the Twins spawned: {first}");
        // R-BOSS-1 (#561): the bots kick the Twins at once and two interrupts break the bar, so it can break in the step it
        // appears (seed 1: created and staggered at tick 3108). a asks for an empty filling boss frame line after the spawn.
        Assert.True(first.Boss == BossId, $"first line is not the boss=900 frame's bar: {first}");
        Assert.Contains(lines, l => l.Boss == BossId && l.Fill == 0 && !l.Broken);
        Assert.Single(lines.Select(l => l.Bar).Distinct());
        var barId = first.Bar;
        // b. every fill line is the sim's value.
        foreach (var l in lines)
        {
            var want = l.Broken ? 1000 : l.Max <= 0 ? 0 : (int)((l.Max - Math.Min(l.Current, l.Max)) * 1000 / l.Max);
            Assert.True(l.Fill == want, $"fill {l.Fill} is not the sim value (want {want}): {l}");
        }
        var cycles = new List<List<Line>>();
        var cycle = new List<Line>();
        foreach (var l in lines)
        {
            if (l.Broken) { cycles.Add(cycle); cycle = new List<Line>(); continue; }
            if (l.Fill == 0) { cycle = new List<Line>(); continue; }
            cycle.Add(l);
        }
        foreach (var rising in cycles)
        {
            for (var k = 1; k < rising.Count; k++)
            {
                Assert.True(rising[k].Fill > rising[k - 1].Fill && rising[k].Tick > rising[k - 1].Tick, $"fill did not rise: {rising[k - 1]} then {rising[k]}");
            }
        }
        // b. R-BREAKBAR-1: every step's bar drop is exactly 50 per interrupt-tagged kick at a Twin, plus the bar's
        // DrainPerControl per control-tagged cast at one (BreakBar.DeriveInputs; clamped at the break); a bar broken going
        // into the step does not move. Anti-vacuity: some step's kicks drained a standing bar.
        var src = auto.Setup.Boss.TwinsBar;
        Assert.Equal(DrainPerKick, src.DrainPerInterrupt);
        foreach (var d in drains)
        {
            var amount = DrainPerKick * d.Kicks + src.DrainPerControl * d.Controls;
            var want = d.Before <= 0 ? d.Before : Math.Max(0, d.Before - amount);
            Assert.True(d.After == want, $"bar drop is not {DrainPerKick} x kicks + {src.DrainPerControl} x controls (want after={want}): {d}");
        }
        Assert.Contains(drains, d => d.Kicks > 0 && d.Before > 0 && d.After < d.Before);
        // c.
        var stagger = staggers.Where(s => s.Bar == barId).Select(s => (int?)s.Step).FirstOrDefault();
        Assert.True(stagger is not null, $"no stagger for {barId} (the Twins' bar never broke in this run)");
        var brokenLine = lines.FirstOrDefault(l => l.Broken && l.Fill == 1000);
        Assert.True(brokenLine is not null, "no fill=1000 broken line (the bar never showed the break)");
        Assert.True(brokenLine!.Step >= stagger!.Value, $"broken line before the stagger: {brokenLine}");
        // d.
        Assert.Contains(lines, l => l.Step > brokenLine.Step && !l.Broken && l.Fill == 0);
    }

    [Fact(DisplayName = "BreakBarSceneTest replica: one tagged kick at a Twin shows a filling 500 line on the boss frame (R-BREAKBAR-1)")]
    public void One_kick_shows_a_filling_500_line()
    {
        var auto = new Autorun();
        while (!auto.Done)
        {
            var commands = auto.Sample();
            var kicks = auto.Kicks(commands);
            if (!kicks.Any(k => k.Tag == BreakBarTag.Interrupt) || auto.BarBeforeDrain() <= 0)
            {
                auto.Step(commands);
                continue;
            }
            // The default run's first kick step, with only the first tagged kick kept: every other kick command is dropped.
            var keep = kicks.First(k => k.Tag == BreakBarTag.Interrupt).ActorId;
            var oneKick = commands.Where(c => c.ActorId == keep || auto.Kicks(new[] { c }).Count == 0).ToList();
            Assert.Single(auto.Kicks(oneKick));
            var tick = auto.Run.State.Tick;
            auto.Step(oneKick);
            var r = auto.Readout();
            Assert.True(r is not null, $"no boss frame Break bar after the one-kick step at tick {tick}");
            var line = new Line(0, r!.Value.Bar.BarId, BossId, auto.Run.State.Tick, r.Value.Bar.Bar.Current, r.Value.Bar.Bar.Max, r.Value.Look.FillPermille, r.Value.Look.Phase == BreakBarPhase.Broken);
            _output.WriteLine(FormattableString.Invariant($"one kick by actor {keep} at tick {tick}: {line}"));
            Assert.Equal(100, line.Max);
            Assert.Equal(100 - DrainPerKick, line.Current);
            Assert.False(line.Broken, $"one kick broke the bar: {line}");
            Assert.Equal(500, line.Fill);
            return;
        }
        Assert.Fail("the default run never kicked a Twin with the bar up");
    }
}
