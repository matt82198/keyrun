using System.Collections.Generic;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Xunit;
using static Keyrun.Sim.Tests.CombatCastTestKit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// CAST-RANGE-RECHECK: a target-shaped cast rechecks its range when it completes, not only when it starts, so a
/// target that walks out of a telegraphed cast's range before castTicks run out is not hit (design/BIBLE.md
/// section 3, cast bars are the enemy's language). The ability mirrors content/data/abilities/enemies.json
/// seed.lash verbatim: target shape, range 1500, castTicks 20, not interruptible, damage 70 physical.
/// Driven through the real Rules step (cast start, MoveCommand, completion at castEndsTick).
/// </summary>
public sealed class CastRangeRecheckTests
{
    private const int Seed = 1;
    private const int Player = 2;

    private static readonly AbilityDef SeedLash = Def("seed.lash", new AbilityShape(ShapeKind.Target), new EffectOp[] { new DamageOp(70, "physical") },
        castTicks: 20, gcdTicks: 0, cooldownTicks: 200, charges: 1, range: 1500, interruptible: false);

    private static (SimState State, StepRules Rules, List<SimEvent> Events) Scenario()
    {
        var seed = Make(Seed, ActorKind.Enemy, "seed.elite", ActorRole.None, x: 0, maxHealth: 3000);
        var player = Make(Player, ActorKind.Player, "warrior", ActorRole.Tank, x: 1200, maxHealth: 500);
        var state = At(0, seed, player);
        var rules = Rules.Make(Config(AbilityMap(SeedLash), boundsHalf: 100000));
        return (state, rules, new List<SimEvent>());
    }

    private static void RunUntil(SimState state, StepRules rules, List<SimEvent> events, Dictionary<int, Command[]> byTick, int lastTick)
    {
        while (state.Tick <= lastTick)
        {
            Tick(state, rules, byTick.TryGetValue(state.Tick, out var c) ? c : new Command[0], events.Add);
        }
    }

    [Fact]
    public void A_lash_begun_in_range_does_not_hit_a_target_that_moved_beyond_range_before_completion()
    {
        var (state, rules, events) = Scenario();
        var byTick = new Dictionary<int, Command[]> { [0] = new Command[] { new CastCommand(0, Seed, "seed.lash", Player) } };
        for (var t = 1; t <= 4; t++)
        {
            byTick[t] = new Command[] { new MoveCommand(t, Player, new Dir2(1000, 0), 0) };
        }
        RunUntil(state, rules, events, byTick, 21);

        Assert.True(Casting.RangeMm(state.Actors[Seed], state.Actors[Player]) > 1500, "the player walked out of lash range before tick 20");
        Assert.Contains(events, e => e.Type == "cast_complete" && e.Tick == 20 && Str(e, "abilityId") == "seed.lash");
        Assert.Equal(500, state.Actors[Player].Health);
        Assert.Null(state.Actors[Seed].CastId);
    }

    [Fact]
    public void A_lash_whose_target_stays_in_range_lands_its_70_damage_at_completion()
    {
        var (state, rules, events) = Scenario();
        RunUntil(state, rules, events, new Dictionary<int, Command[]> { [0] = new Command[] { new CastCommand(0, Seed, "seed.lash", Player) } }, 21);

        Assert.True(Casting.RangeMm(state.Actors[Seed], state.Actors[Player]) <= 1500);
        Assert.Contains(events, e => e.Type == "cast_complete" && e.Tick == 20 && Str(e, "abilityId") == "seed.lash");
        Assert.Equal(430, state.Actors[Player].Health);
    }

    [Fact]
    public void A_target_exactly_at_range_at_completion_is_still_hit()
    {
        var (state, rules, events) = Scenario();
        RunUntil(state, rules, events, new Dictionary<int, Command[]> { [0] = new Command[] { new CastCommand(0, Seed, "seed.lash", Player) } }, 10);
        state.Actors[Player].Pos = new Vec3(1500, 0, 0);
        RunUntil(state, rules, events, new Dictionary<int, Command[]>(), 21);

        Assert.Equal(1500, Casting.RangeMm(state.Actors[Seed], state.Actors[Player]));
        Assert.Equal(430, state.Actors[Player].Health);
        Assert.Single(events, e => e.Type == "cast_complete");
    }
}
