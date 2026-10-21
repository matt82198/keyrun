using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;
using Xunit.Abstractions;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// KF2 regression guard (finding f-210c3939; Matt's demo verdict 2026-10-19: "bots idle in the first room (the Choir is
/// meant to teach the kick)"; design/BIBLE.md section 9: "The Choir - two melee and a caster lieutenant whose 4-second
/// heal cast must be kicked"). Run C's default setup, headless and with no game launch: the run built as SimDriver
/// builds it (<see cref="HostRunSetup.Build"/> at key 2, seed 1, bots on, then <see cref="BossRules.Arm"/>), seat 1 the
/// autorun Warden (<see cref="ScriptedWarden"/>, the host's own file linked in unedited) and seats 2 and 3 the host's
/// own <see cref="BotCommandSource"/>, stepped in SimDriver.StepOnce's order (seat 1 first, then the bots). From the
/// Entrance the party must reach the Choir, combat with it must start within <see cref="CombatWithinTicks"/>, and a kick
/// must be attempted on a Choral Mending.
/// </summary>
public sealed class RunCEngagesChoirTests
{
    /// <summary>
    /// The pull-to-first-pack walk: from the layout start (x 4000) to the Choir trigger's near edge (x 12000) plus the
    /// formation's 2500 mm trail, at 180 mm per tick (run.json movement) = ceil(10500 / 180) = 59 ticks.
    /// </summary>
    public const int WalkTicks = 59;

    /// <summary>
    /// Combat (the first damage between the party and a Choir enemy, either way) must start within this many ticks of
    /// the run start: twice the walk (the leader holds at the standoff point for the party, LEADER-WAITS) plus 100 ticks
    /// (5 s) for the Choir to close from its origin and the first swing or cast to land. 2 * 59 + 100 = 218.
    /// </summary>
    public const int CombatWithinTicks = (2 * WalkTicks) + 100;

    private const int Key = 2;
    private const long Seed = 1;
    private const int MaxTicks = 7000; // run C's --ticks
    private const string HealCast = "choir.healCast";

    private sealed record Tally(
        int PullStartTick,
        int FirstCombatTick,
        int HealsStarted,
        int KickAttempts,
        int KicksLanded,
        List<string> Log);

    private static readonly Lazy<Tally> Driven = new(DriveRunC);

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test with xUnit's output sink (the trace is printed for the PR).</summary>
    /// <param name="output">The sink.</param>
    public RunCEngagesChoirTests(ITestOutputHelper output) => _output = output;

    private static bool Kicks(Keyrun.Sim.Combat.AbilityDef def) => def.Effects.Any(op => op is Keyrun.Sim.Combat.InterruptOp);

    private static Tally DriveRunC()
    {
        var registry = ContentLoader.Load();
        var setup = HostRunSetup.Build(registry, "hollow-vault", Key, "warden", withBots: true);
        var start = setup.StartFor(null, null, registry, "warden");
        var created = RunRoot.CreateRun(Seed, setup.PartyFor(setup.Dungeon), setup.Dungeon, setup.Content, start.Options);
        var run = created with { Encounter = BossRules.Arm(created.Encounter, setup.Boss) };
        var warden = new ScriptedWarden();
        var bots = new BotCommandSource(setup.BotActorIds, KitRegistry.From(registry), Seed);
        var party = new HashSet<long>(setup.Party.Select(p => (long)p.Id));
        var choir = new HashSet<long>(setup.Dungeon.Pulls[0].Spawns.Select(s => (long)s.ActorId));
        Assert.Equal("choir", setup.Dungeon.Pulls[0].Id);

        var log = new List<string>();
        var pullStart = -1;
        var firstCombat = -1;
        var started = 0;
        var attempts = 0;
        var landed = 0;
        var casting = new HashSet<int>();
        var nextSample = 0;
        var firstHitBy = new HashSet<long>();
        var cleared = false;

        // Until the Choir is cleared (or run C's 7000 ticks run out).
        while (run.State.Tick < MaxTicks && run.RunEnd.ResultTick is null && !cleared)
        {
            var tick = run.State.Tick;
            if (tick >= nextSample)
            {
                log.Add($"{tick} pos " + string.Join(" ", setup.Party.Select(p => run.State.Actors[p.Id]).Select(a => $"{a.Id}=({a.Pos.X},{a.Pos.Z})")));
                nextSample = tick + 20;
            }
            var commands = new List<Command>();
            warden.Sample(tick, run, commands);
            bots.Sample(tick, run, commands);
            foreach (var c in commands.OfType<CastCommand>())
            {
                if (c.TargetId is { } t && run.State.Actors.TryGetValue(t, out var target) && target.Alive && target.CastId == HealCast &&
                    run.Content.Abilities.TryGetValue(c.AbilityId, out var def) && Kicks(def))
                {
                    attempts++;
                    log.Add($"{tick} kick attempt {c.AbilityId} by {c.ActorId} at {t}");
                }
            }

            var stepped = RunRoot.StepRun(run, commands);
            run = stepped.Run;
            foreach (var e in stepped.Events)
            {
                switch (e.Type)
                {
                    case "room_enter":
                        log.Add($"{e.Tick} room_enter {((JsInt)e.Get("actorId")!).Value} {((JsString)e.Get("room")!).Value}");
                        break;
                    case "pull_start" when ((JsString)e.Get("pullId")!).Value == "choir":
                        pullStart = e.Tick;
                        log.Add($"{e.Tick} pull_start choir");
                        break;
                    case "pull_clear" when ((JsString)e.Get("pullId")!).Value == "choir":
                        cleared = true;
                        log.Add($"{e.Tick} pull_clear choir");
                        break;
                    case "damage" when e.Get("sourceId") is JsInt hitter && party.Contains(hitter.Value) &&
                        e.Get("targetId") is JsInt hit && choir.Contains(hit.Value) && firstHitBy.Add(hitter.Value):
                        log.Add($"{e.Tick} first damage by seat {hitter.Value} on {((JsInt)e.Get("targetId")!).Value}");
                        if (firstCombat < 0)
                        {
                            firstCombat = e.Tick;
                        }
                        break;
                    case "damage" when firstCombat < 0 && e.Get("sourceId") is JsInt src && e.Get("targetId") is JsInt dst &&
                        ((party.Contains(src.Value) && choir.Contains(dst.Value)) || (choir.Contains(src.Value) && party.Contains(dst.Value))):
                        firstCombat = e.Tick;
                        log.Add($"{e.Tick} first combat damage {src.Value} -> {dst.Value}");
                        break;
                    case "interrupt" when ((JsString)e.Get("abilityId")!).Value == HealCast:
                        var by = ((JsInt)e.Get("sourceId")!).Value;
                        if (party.Contains(by))
                        {
                            landed++;
                        }
                        log.Add($"{e.Tick} interrupt {HealCast} by {by}");
                        break;
                }
            }
            foreach (var a in run.State.Actors.Values.Where(a => a.Kind == ActorKind.Enemy))
            {
                if (a.Alive && a.CastId == HealCast)
                {
                    if (casting.Add(a.Id))
                    {
                        started++;
                        log.Add($"{run.State.Tick} heal started by {a.Id}");
                    }
                }
                else
                {
                    casting.Remove(a.Id);
                }
            }
        }
        return new Tally(pullStart, firstCombat, started, attempts, landed, log);
    }

    [Fact(DisplayName = "Run C (KF2): from the Entrance the party starts combat with the Choir within 218 ticks and kicks a Choral Mending")]
    public void Run_c_party_engages_the_choir_and_kicks_its_heal()
    {
        var t = Driven.Value;
        foreach (var line in t.Log)
        {
            _output.WriteLine(line);
        }
        _output.WriteLine($"RUNC pullStart={t.PullStartTick} firstCombat={t.FirstCombatTick} healsStarted={t.HealsStarted} kickAttempts={t.KickAttempts} kicksLanded={t.KicksLanded} withinTicks={CombatWithinTicks}");
        Assert.True(t.PullStartTick >= 0, "the Choir pull never started: the party idled outside the first room");
        Assert.True(t.FirstCombatTick >= 0 && t.FirstCombatTick <= CombatWithinTicks,
            $"combat with the Choir started at tick {t.FirstCombatTick} (want 0..{CombatWithinTicks})");
        // Non-vacuity: the Cantor must have started a heal, or a kick count would mean nothing.
        Assert.True(t.HealsStarted >= 1, $"the Cantor started {t.HealsStarted} Choral Mendings; the kick check would be vacuous");
        Assert.True(t.KickAttempts >= 1, $"the party attempted {t.KickAttempts} kicks on {t.HealsStarted} Choral Mendings");
    }
}
