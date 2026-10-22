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
/// b. every fill is the sim's value, each fill cycle's filling lines rise, and some cycle fills above 0 before its break;
/// c. a stagger on that bar, and a fill=1000 broken line no earlier than it;
/// d. a fill=0 filling line after the broken one (the bar reset).
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

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test with xUnit's output sink (the proof lines are printed for the PR).</summary>
    /// <param name="output">The sink.</param>
    public BreakBarSceneReplicaTests(ITestOutputHelper output) => _output = output;

    private sealed record Line(int Step, string Bar, int Boss, int Tick, long Current, long Max, int Fill, bool Broken)
    {
        public override string ToString() => FormattableString.Invariant(
            $"HUD breakbar boss={Boss} bar={Bar} tick={Tick} current={Current} max={Max} fill={Fill} phase={(Broken ? "broken" : "filling")}");
    }

    private static string? Str(SimEvent e, string key) => e.Get(key) is JsString s ? s.Value : null;

    [Fact(DisplayName = "BreakBarSceneTest replica: in the default 7000-tick autorun the Twins' shared Break bar fills, breaks on a stagger and resets")]
    public void Default_autorun_break_bar_fills_breaks_and_resets()
    {
        var registry = ContentLoader.Load();
        var setup = HostRunSetup.Build(registry, "hollow-vault", Key, Lobby.DefaultClassId, withBots: true);
        var start = setup.StartFor(null, null, registry, Lobby.DefaultClassId);
        var options = (start.Options ?? new RunOptions()) with { RotationIndex = setup.RotationIndex };
        var party = setup.PartyFor(setup.Dungeon);
        options = setup.WithBotLoadouts(options, party) ?? options;
        var created = RunRoot.CreateRun(Seed, party, setup.Dungeon, setup.Content, options);
        var run = created with { Encounter = BossRules.Arm(created.Encounter, setup.Boss) };
        var warden = new ScriptedWarden();
        var bots = new BotCommandSource(setup.BotActorIds, KitRegistry.From(registry), Seed, BotCommandSource.DefaultSkillDial, null);
        bots.Reset();
        var backfill = BackfillState.Empty;

        var lines = new List<Line>();
        int spawnStep = -1;
        var staggers = new List<(int Step, string Bar)>();
        string? lastKey = null;
        for (var step = 0; run.State.Tick < Ticks && run.RunEnd.ResultTick is null; step++)
        {
            var tick = run.State.Tick;
            var commands = new List<Command>();
            warden.Sample(tick, run, commands, backfill.Open.Select(s => s.ActorId).ToList());
            bots.Sample(tick, run, commands);
            bots.TakeReactions();
            var stepped = Backfill.Step(backfill, run, commands, Array.Empty<QueueRequest>());
            backfill = stepped.Backfill;
            run = stepped.Step.Run;
            // The step's EVENT lines print before its HUD proof lines (SimDriver.StepOnce, then Stepped).
            foreach (var e in stepped.Step.Events)
            {
                if (spawnStep < 0 && e.Type == "boss_add_spawn") { spawnStep = step; }
                if (e.Type == "stagger") { staggers.Add((step, Str(e, "barId") ?? string.Empty)); }
            }
            // Hud/BreakBar.cs Current() and OnStepped, without Godot.
            foreach (var frame in UnitFrameModel.Build(run.State, setup.HumanActorId))
            {
                if (frame.Kind != "boss" || BossBreakBarReadout.For(run.Encounter, frame.ActorId) is not { } bar)
                {
                    continue;
                }
                var look = BreakBarRule.For(bar.Bar.Current, bar.Bar.Max, bar.Bar.CooldownReadyTick, run.State.Tick, Keyrun.Sim.Encounter.BreakBar.ResetCooldownTicks);
                var broken = look.Phase == BreakBarPhase.Broken;
                var key = FormattableString.Invariant($"{bar.BarId}:{bar.Bar.Current}:{(broken ? "broken" : "filling")}");
                if (key != lastKey)
                {
                    lastKey = key;
                    lines.Add(new Line(step, bar.BarId, frame.ActorId, run.State.Tick, bar.Bar.Current, bar.Bar.Max, look.FillPermille, broken));
                }
                break;
            }
        }
        foreach (var l in lines) { _output.WriteLine(l.ToString()); }
        _output.WriteLine(FormattableString.Invariant($"end tick={run.State.Tick} runEnd={run.RunEnd.ResultTick} spawnStep={spawnStep} staggers={staggers.Count}"));

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
        // b. R-BOSS-1 sets 50 per interrupt on a 100 bar: one partial fill (500) per cycle, never the old two.
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
        Assert.True(cycles.Any(c => c.Count >= 1), $"no filling line above 0 before any of {cycles.Count} break(s): the bar only flips to broken");
        foreach (var rising in cycles)
        {
            for (var k = 1; k < rising.Count; k++)
            {
                Assert.True(rising[k].Fill > rising[k - 1].Fill && rising[k].Tick > rising[k - 1].Tick, $"fill did not rise: {rising[k - 1]} then {rising[k]}");
            }
        }
        // c.
        var stagger = staggers.Where(s => s.Bar == barId).Select(s => (int?)s.Step).FirstOrDefault();
        Assert.True(stagger is not null, $"no stagger for {barId} (the Twins' bar never broke in this run)");
        var brokenLine = lines.FirstOrDefault(l => l.Broken && l.Fill == 1000);
        Assert.True(brokenLine is not null, "no fill=1000 broken line (the bar never showed the break)");
        Assert.True(brokenLine!.Step >= stagger!.Value, $"broken line before the stagger: {brokenLine}");
        // d.
        Assert.Contains(lines, l => l.Step > brokenLine.Step && !l.Broken && l.Fill == 0);
    }
}
