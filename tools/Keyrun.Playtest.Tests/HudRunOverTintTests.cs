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

    // R-K4-13 (c) (measured 2026-10-22, this tree): run N with the seat abandon (seed 1, key 2, HudSceneTest.ps1 $TintAbandon
    // "2@300") ends at run_end 6900 and no living hostile beyond 3000 mm exists while it is live, so it is tried first and
    // fails. The same run without the abandon (seed 1, key 2, bots on) has a living hostile target beyond 3000 mm before its
    // run_end, so the fact targets that run; the first candidate that shows one is used.
    private static readonly (long Seed, int Key, string? Abandon, string Name)[] Candidates =
    {
        (1, 2, "2@300", "run N (HudSceneTest.ps1 $TintAbandon)"),
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
        var warden = new ScriptedWarden();
        var bots = new BotCommandSource(setup.BotActorIds, KitRegistry.From(registry), seed);
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
