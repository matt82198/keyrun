using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Xunit;
using static Keyrun.Sim.Tests.CombatCastTestKit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// CAST-KEEPS-START-TARGET (finding f-bd8fbbb1, the WoW rule for the core loop): a cast resolves on the target it
/// started on. A TargetCommand that arrives mid-cast changes the caster's target for the NEXT press only; the cast in
/// flight still lands its effects and its Window on the target it began on. Evidence: a PolicyBot healer pressed
/// Cadence on the tank, a TargetCommand to enemy 100 arrived mid-cast, and the cast opened its Window on enemy 100.
/// Driven through the real Rules step (cast start, TargetCommand, completion at castEndsTick, then Windows).
/// Twin: tests/sim/combat/cast-keeps-target.test.ts.
/// </summary>
public sealed class CastKeepsStartTargetTests
{
    private const int Caster = 1;
    private const int Ally = 2;
    private const int EnemyA = 10;
    private const int EnemyB = 20;

    private static readonly AbilityDef Bolt = Def(
        "bolt",
        new AbilityShape(ShapeKind.Target),
        new EffectOp[] { new DamageOp(30, "arcane"), new WindowOp("attuned", 60, true, "chanter") },
        castTicks: 20,
        range: 5000);

    private static readonly AbilityDef Mend = Def(
        "mend",
        new AbilityShape(ShapeKind.Target),
        new EffectOp[] { new HealOp(40) },
        castTicks: 20,
        range: 5000);

    private static (SimState State, StepRules Rules, List<SimEvent> Events) Scenario()
    {
        var caster = Make(Caster, ActorKind.Player, "chanter", ActorRole.Healer, x: 0, maxHealth: 200);
        var ally = Make(Ally, ActorKind.Player, "warden", ActorRole.Tank, x: 1000, maxHealth: 200);
        var a = Make(EnemyA, ActorKind.Enemy, "x", ActorRole.None, x: 2000, maxHealth: 100);
        var b = Make(EnemyB, ActorKind.Enemy, "x", ActorRole.None, x: -2000, maxHealth: 100);
        ally.Health = 100;
        var state = At(0, caster, ally, a, b);
        var rules = Rules.Make(Config(AbilityMap(Bolt, Mend), boundsHalf: 100000));
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
    public void A_bolt_begun_on_A_lands_on_A_when_the_caster_retargets_B_mid_cast()
    {
        var (state, rules, events) = Scenario();
        var byTick = new Dictionary<int, Command[]>
        {
            [0] = new Command[] { new CastCommand(0, Caster, "bolt", EnemyA) },
            [7] = new Command[] { new TargetCommand(7, Caster, EnemyB) },
        };
        RunUntil(state, rules, events, byTick, 21);

        Assert.Contains(events, e => e.Type == "cast_complete" && e.Tick == 20 && Str(e, "abilityId") == "bolt");
        Assert.Equal(70, state.Actors[EnemyA].Health);
        Assert.Equal(100, state.Actors[EnemyB].Health);
        var opened = Assert.Single(events, e => e.Type == "window_opened");
        Assert.Equal(EnemyA, Int(opened, "targetId"));
        Assert.Equal(EnemyB, state.Actors[Caster].TargetId);
    }

    [Fact]
    public void A_heal_begun_on_the_tank_heals_the_tank_when_the_healer_retargets_an_enemy_mid_cast()
    {
        var (state, rules, events) = Scenario();
        var byTick = new Dictionary<int, Command[]>
        {
            [0] = new Command[] { new CastCommand(0, Caster, "mend", Ally) },
            [7] = new Command[] { new TargetCommand(7, Caster, EnemyA) },
        };
        RunUntil(state, rules, events, byTick, 21);

        Assert.Equal(140, state.Actors[Ally].Health);
        Assert.Contains(events, e => e.Type == "heal" && Int(e, "targetId") == Ally && Int(e, "amount") == 40);
    }

    [Fact]
    public void The_next_cast_after_a_mid_cast_retarget_goes_to_the_new_target()
    {
        var (state, rules, events) = Scenario();
        var byTick = new Dictionary<int, Command[]>
        {
            [0] = new Command[] { new CastCommand(0, Caster, "bolt", EnemyA) },
            [7] = new Command[] { new TargetCommand(7, Caster, EnemyB) },
            [25] = new Command[] { new CastCommand(25, Caster, "bolt", EnemyB) },
        };
        RunUntil(state, rules, events, byTick, 46);

        Assert.Equal(70, state.Actors[EnemyA].Health);
        Assert.Equal(70, state.Actors[EnemyB].Health);
        Assert.Equal(new long[] { EnemyA, EnemyB }, events.Where(e => e.Type == "window_opened").Select(e => Int(e, "targetId")).ToArray());
    }
}
