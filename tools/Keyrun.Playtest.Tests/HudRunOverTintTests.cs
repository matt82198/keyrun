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

namespace Keyrun.Playtest.Tests;

/// <summary>
/// The headless half of HudSceneTest.ps1 check 41 (lane TINT-RUNOVER, finding f-c9a92309). The scene's run N cannot show
/// the run-over switch working: after its timer-cap end the Warden's target is frozen at 102 alive 2289 mm off (inside
/// the 3000 mm reach of his Target-shaped hostile slots), so the scene never has a living target beyond reach once the run
/// is over. This test drives the real run state to the first tick where the Warden's living hostile target stands beyond
/// the reach while the run is live, then asks <see cref="AbilitySlotLook.ShowsOutOfRange"/> with the bar's own arguments:
/// the run-over flag off reads tinted, and the flag on (the run has ended for any result, as <c>AbilityBar.OutOfRange</c>
/// passes it) reads untinted. Both halves are needed: with only the second, a switch that always returned false would pass.
/// </summary>
public sealed class HudRunOverTintTests
{
    private const int Horizon = 40000;        // hang guard

    // R-K4-13 (c) and lane S1 (measured 2026-10-23, this tree, Playtest harness, seed 1, bots on): the seat abandon "2@300"
    // ends depleted at run_end 6900 with no living hostile beyond 3000 mm while live, so it fails. Searched anchors at key 2:
    // "2@900" depleted 7696, no far hostile; "2@1500" far hostile at 1468 but depleted only at 22300; "2@2400" far hostile at
    // 1468 (3651 mm, before the abandon fires) and depleted at ResultTick 11279 (chosen: the smallest change that does both; the
    // 11279 is measured headlessly by Run_N_ends_depleted_and_uncleared_before_the_horizon, log
    // scratchpad/lanes/s1-hudrunN-probe.log: Result=Depleted, PenaltyTicks=6600, Deaths=17, clock 17879 at tick 11279. It was 9401
    // (PenaltyTicks 17000, Deaths 35) before the shared SceneBots/SceneWarden helper passed the scene's Forces plan); "2@pull1.hp10+50"
    // far hostile at 1468, depleted 21500. Key 5 (2@300, 2@900, 2@1500) never shows a far hostile; key 10 "2@900" does (3168,
    // 3054 mm) but changes the key. Scene N pins "2@2400" as $TintAbandon in HudRunN-SceneTest.ps1. The first candidate that
    // shows a far living hostile is used.
    private static readonly (long Seed, int Key, string? Abandon, string Name)[] Candidates =
    {
        (1, 2, "2@2400", "run N (abandon 2@2400, scene N $TintAbandon)"),
        (1, 2, null, "run N without the seat abandon"),
    };

    [Fact(DisplayName = "HudSceneTest run N tint (no seat abandon, or the abandon run if it shows one): a living hostile target beyond reach tints the Target slots only while the run is live")]
    public void Run_over_switch_clears_the_tint_on_a_living_target_beyond_reach()
    {
        (Run Run, HostRunSetup Setup, long Distance)? found = null;
        var name = "none";
        foreach (var c in Candidates)
        {
            found = FindFarTargetTick(c.Seed, c.Key, c.Abandon);
            if (found is not null)
            {
                name = c.Name;
                break;
            }
        }
        Assert.True(found is not null, "no scripted run showed a living hostile target beyond 3000 mm while the run was live");
        var (run, setup, distance) = found!.Value;
        var me = run.State.Actors[setup.HumanActorId];
        var target = run.State.Actors[me.TargetId!.Value];
        var hostileTargetSlots = setup.Slots
            .Select(s => run.Content.Abilities[s.AbilityId])
            .Where(def => def.Shape.Kind == Keyrun.Sim.Combat.ShapeKind.Target && Affinities.Of(def, run.Content.Effects) == Affinity.Hostile)
            .ToList();
        Assert.NotEmpty(hostileTargetSlots);
        var range = new RangeTarget(Alive: target.Alive, Hostile: target.Kind != me.Kind, RangeMm: distance);
        foreach (var def in hostileTargetSlots)
        {
            Assert.True(distance > def.Range, $"{name}, {def.Id}: the target ({distance} mm) must stand beyond its {def.Range} mm reach");
            Assert.True(AbilitySlotLook.ShowsOutOfRange(range, hostileAbility: true, def.Range, runOver: false),
                $"{name}, {def.Id}: a live run with a living hostile target beyond {def.Range} mm must tint");
            Assert.False(AbilitySlotLook.ShowsOutOfRange(range, hostileAbility: true, def.Range, runOver: true),
                $"{name}, {def.Id}: the run has ended, so the range tint must be off");
        }
    }

    /// <summary>
    /// Lane S1 (TINT-RUNOVER): the scene's run N must reach a non-clear end before its --ticks cap, or the run-over half of the
    /// tint can never show. Runs the same setup and loop as <see cref="FindFarTargetTick"/> to the run's own run_end, or to
    /// <see cref="Horizon"/>, and asserts it ends depleted (the timer cap, uncleared). The message carries the measured
    /// ResultTick, clock, penalty and deaths either way, so a failure hands back numbers.
    /// </summary>
    /// <summary>
    /// Scene N's parameters, re-derived in lane S1 premise (log scratchpad/lanes/s1-hudrunN-premise.log). The old key 2 run
    /// clears hollow-vault on this tree, so no timer-cap end happens; key 3 ends depleted at the cap. Key: the scene passes
    /// <c>--key 3</c>, which ArenaOptions.cs:194-196 reads into Options.KeyLevel (default 2, ArenaOptions.cs:137), and
    /// SimDriver.cs:245 passes that to DungeonPick.BuildSetup. Seed: <c>--seed</c> (ArenaOptions.cs:203-205), default 1.
    /// Abandon: <c>--abandon-seat</c> (ArenaOptions.cs:206-207) through SimDriver.Queue.cs:130. Bots and Warden: SimDriver.cs:254-258,
    /// PlayerInput.cs:211-215. Measured end: ResultTick 12900 = CapTicks 26200 - PenaltyTicks 13300 (26 deaths), Depleted.
    /// </summary>
    private const long NSeed = 1;
    private const int NKey = 3;
    private const string NAbandon = "2@2400";

    [Fact(DisplayName = "HudSceneTest run N (scene parameters, bots on): the run ends depleted and uncleared exactly at the timer cap before the horizon")]
    public void Run_N_ends_depleted_and_uncleared_before_the_horizon()
    {
        var run = RunToEnd(NSeed, NKey, NAbandon);
        var encounter = run.Encounter;
        var end = run.RunEnd.ResultTick;
        var clockAtEnd = end is null ? -1 : Timer.ClockTicks(encounter, end.Value);
        var message = $"run N (seed {NSeed}, key {NKey}, abandon {NAbandon}) must end by tick {Horizon}: ResultTick={end?.ToString() ?? "none"}, "
            + $"Result={run.RunEnd.Result?.ToString() ?? "none"}, clock at end={clockAtEnd}, PenaltyTicks={encounter.PenaltyTicks}, "
            + $"Deaths={encounter.Deaths}, CapTicks={Timer.CapTicks(encounter)}, ParTicks={encounter.ParTicks}, Depleted={encounter.Depleted}";
        Assert.True(end is not null, message);
        Assert.True(run.RunEnd.Result == RunResult.Depleted, message + " (ended, but not depleted)");
        Assert.False(run.RunEnd.Result == RunResult.Timed, message + " (cleared, which the case must not be)");
        Assert.True(clockAtEnd == Timer.CapTicks(encounter), message + " (ended, but not at the timer cap)");
    }

    /// <summary>Mirrors <see cref="FindFarTargetTick"/>'s setup and step loop, without the far-target stop, to the run's own run_end or the horizon.</summary>
    private static Run RunToEnd(long seed, int key, string? abandon)
    {
        var registry = ContentLoader.Load();
        var setup = HostRunSetup.Build(registry, "hollow-vault", key, "warden", withBots: true);
        var start = setup.StartFor(null, null, registry, "warden");
        var options = (start.Options ?? new RunOptions()) with { RotationIndex = setup.RotationIndex };
        var party = setup.PartyFor(setup.Dungeon);
        options = setup.WithBotLoadouts(options, party) ?? options;
        var created = RunRoot.CreateRun(seed, party, setup.Dungeon, setup.Content, options);
        var run = created with { Encounter = BossRules.Arm(created.Encounter, setup.Boss) };
        var warden = SceneWarden(registry);
        var bots = SceneBots(setup, registry, seed);
        SeatAbandonPlan? plan = abandon is null ? null : SeatAbandonPlan.Parse(abandon);
        int? anchor = null;
        var book = BackfillState.Empty;
        var queue = new List<QueueRequest>();
        while (run.State.Tick < Horizon && run.RunEnd.ResultTick is null)
        {
            var tick = run.State.Tick;
            if (plan is not null && tick == plan.ResolveTick(anchor))
            {
                book = Backfill.Abandon(book, run, plan.ActorId);
            }
            var commands = new List<Command>();
            warden.Sample(tick, run, commands, book.Open.Select(s => s.ActorId).ToList());
            bots.Sample(tick, run, commands);
            var stepped = Backfill.Step(book, run, commands, queue);
            book = stepped.Backfill;
            if (plan is not null)
            {
                anchor ??= plan.AnchorAt(tick, stepped.Step.Events, stepped.Step.Run.Dungeon, stepped.Step.Run.State.Actors);
            }
            run = stepped.Step.Run;
        }
        return run;
    }

    /// <summary>
    /// Steps the run as SimDriver does until the Warden's current target is alive, hostile and beyond 3000 mm, and returns that
    /// state. Returns null if no such tick exists while the run is live (before its run_end) within the horizon.
    /// </summary>
    private static (Run Run, HostRunSetup Setup, long Distance)? FindFarTargetTick(long seed, int key, string? abandon)
    {
        var registry = ContentLoader.Load();
        var setup = HostRunSetup.Build(registry, "hollow-vault", key, "warden", withBots: true);
        var start = setup.StartFor(null, null, registry, "warden");
        var options = (start.Options ?? new RunOptions()) with { RotationIndex = setup.RotationIndex };
        var party = setup.PartyFor(setup.Dungeon);
        options = setup.WithBotLoadouts(options, party) ?? options;
        var created = RunRoot.CreateRun(seed, party, setup.Dungeon, setup.Content, options);
        var run = created with { Encounter = BossRules.Arm(created.Encounter, setup.Boss) };
        var warden = SceneWarden(registry);
        var bots = SceneBots(setup, registry, seed);
        SeatAbandonPlan? plan = abandon is null ? null : SeatAbandonPlan.Parse(abandon);
        int? anchor = null;
        var book = BackfillState.Empty;
        var queue = new List<QueueRequest>();
        // R-K4-13 (b) PR #665 (AoE kit): the window is the run's LIVE span, read from the run's own run_end. The fact must hold
        // before the run ends.
        while (run.State.Tick < Horizon && run.RunEnd.ResultTick is null)
        {
            var tick = run.State.Tick;
            if (plan is not null && tick == plan.ResolveTick(anchor))
            {
                book = Backfill.Abandon(book, run, plan.ActorId);
            }
            if (FarLivingTarget(run, setup.HumanActorId, out var distance))
            {
                return (run, setup, distance);
            }
            var commands = new List<Command>();
            warden.Sample(tick, run, commands, book.Open.Select(s => s.ActorId).ToList());
            bots.Sample(tick, run, commands);
            var stepped = Backfill.Step(book, run, commands, queue);
            book = stepped.Backfill;
            if (plan is not null)
            {
                anchor ??= plan.AnchorAt(tick, stepped.Step.Events, stepped.Step.Run.Dungeon, stepped.Step.Run.State.Actors);
            }
            run = stepped.Step.Run;
        }
        return null;
    }

    /// <summary>
    /// Lane S1 derivation: the seat and Warden construction the scene uses, shared by every HUD derivation fact in this file.
    /// Source of truth: game/src/Host/SimDriver.cs:254-258, which builds <c>new BotCommandSource(Setup.BotActorIds,
    /// KitRegistry.From(Registry), Options.Seed, BotCommandSource.DefaultSkillDial, careless) { Forces = ForcesPlan.Of(Registry) }</c>.
    /// careless is null here as it is in the scene (no --careless-seat in HudRunN-SceneTest.ps1), and DefaultSkillDial is the
    /// constructor default (game/src/HostCore/BotCommandSource.cs:34, :60). The autorun Warden is PlayerInput's ScriptedWarden
    /// (game/src/Host/PlayerInput.cs:211-215), which takes <c>Forces = ForcesPlan.Of(Driver.Registry)</c> and the open seats
    /// (SimDriver.Queue.cs:133). The abandon goes through SimDriver.AbandonSeat (SimDriver.Queue.cs:130), which is the same
    /// <c>Backfill.Abandon</c> call the fact makes on its own book.
    /// </summary>
    private static BotCommandSource SceneBots(HostRunSetup setup, ContentRegistry registry, long seed) =>
        new BotCommandSource(setup.BotActorIds, KitRegistry.From(registry), seed, BotCommandSource.DefaultSkillDial, careless: null)
        {
            Forces = ForcesPlan.Of(registry),
        };

    /// <summary>The autorun Warden as PlayerInput builds it (game/src/Host/PlayerInput.cs:214): ScriptedWarden with the registry's forces plan.</summary>
    private static ScriptedWarden SceneWarden(ContentRegistry registry) => new ScriptedWarden { Forces = ForcesPlan.Of(registry) };

    /// <summary>True when the human's selected target is alive, hostile and farther than 3000 mm (the reach of the slots).</summary>
    private static bool FarLivingTarget(Run run, int humanId, out long distance)
    {
        distance = 0;
        if (!run.State.Actors.TryGetValue(humanId, out var me) || me.TargetId is not { } id || id == me.Id)
        {
            return false;
        }
        if (!run.State.Actors.TryGetValue(id, out var target) || !target.Alive || target.Kind == me.Kind)
        {
            return false;
        }
        distance = Casting.RangeMm(me, target);
        return distance > 3000;
    }
}
