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
/// is over. This test drives the real run N state (HostRunSetup at key 2, seed 1, bots on, <c>--abandon-seat 2@300</c>) to
/// the first tick where the Warden's living hostile target stands beyond the reach, then asks
/// <see cref="AbilitySlotLook.ShowsOutOfRange"/> with the bar's own arguments: the run-over flag off reads tinted, and the
/// flag on (the run has ended for any result, as <c>AbilityBar.OutOfRange</c> passes it) reads untinted. Both halves are
/// needed: with only the second, a switch that always returned false would pass.
/// </summary>
public sealed class HudRunOverTintTests
{
    private const long Seed = 1;   // ArenaOptions.DefaultSeed
    private const int Key = 2;     // ArenaOptions.DefaultKeyLevel
    private const string Abandon = "2@300";   // HudSceneTest.ps1 $TintAbandon
    private const int MaxTick = 8500;         // HudSceneTest.ps1 $TintTicks
    private const int Horizon = 40000;        // hang guard

    [Fact(DisplayName = "HudSceneTest run N tint: a living hostile target beyond reach tints the Target slots only while the run is live")]
    public void Run_over_switch_clears_the_tint_on_a_living_target_beyond_reach()
    {
        var (run, setup, distance) = FindFarTargetTick();
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
            Assert.True(distance > def.Range, $"{def.Id}: the target ({distance} mm) must stand beyond its {def.Range} mm reach");
            Assert.True(AbilitySlotLook.ShowsOutOfRange(range, hostileAbility: true, def.Range, runOver: false),
                $"{def.Id}: a live run with a living hostile target beyond {def.Range} mm must tint");
            Assert.False(AbilitySlotLook.ShowsOutOfRange(range, hostileAbility: true, def.Range, runOver: true),
                $"{def.Id}: the run has ended, so the range tint must be off");
        }
    }

    /// <summary>
    /// Steps run N as SimDriver does until the Warden's current target is alive, hostile and beyond 3000 mm, and returns that
    /// state. Fails if no such tick exists before <see cref="MaxTick"/> (the scene's budget).
    /// </summary>
    private static (Run Run, HostRunSetup Setup, long Distance) FindFarTargetTick()
    {
        var registry = ContentLoader.Load();
        var setup = HostRunSetup.Build(registry, "hollow-vault", Key, "warden", withBots: true);
        var start = setup.StartFor(null, null, registry, "warden");
        var options = (start.Options ?? new RunOptions()) with { RotationIndex = setup.RotationIndex };
        var party = setup.PartyFor(setup.Dungeon);
        options = setup.WithBotLoadouts(options, party) ?? options;
        var created = RunRoot.CreateRun(Seed, party, setup.Dungeon, setup.Content, options);
        var run = created with { Encounter = BossRules.Arm(created.Encounter, setup.Boss) };
        var warden = new ScriptedWarden();
        var bots = new BotCommandSource(setup.BotActorIds, KitRegistry.From(registry), Seed);
        var plan = SeatAbandonPlan.Parse(Abandon);
        int? anchor = null;
        var book = BackfillState.Empty;
        var queue = new List<QueueRequest>();
        while (run.State.Tick < Horizon && run.State.Tick < MaxTick)
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
        throw new InvalidOperationException($"run N never showed a living hostile target beyond 3000 mm before tick {MaxTick}");
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
