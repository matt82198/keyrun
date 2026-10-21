using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Runs;
using Xunit;
using static Keyrun.Sim.Tests.CombatCastTestKit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// RETARGET-FIZZLE (finding f-f1ab879b, RULING R-RETARGET-1, the WoW rule): a cast whose start target dies mid-cast
/// fizzles; it does not hop to another target. The sim's dead-target auto-retarget (RunWiring.FixupTargeting) goes
/// through Casting.SetTarget, so it pins the cast's start target like any other mid-cast retarget: the dead start target
/// resolves to nothing, cast_complete still fires, and no one takes the bolt. The auto-retarget still moves the caster's
/// current target to the next living enemy for the next press. Driven through the real Rules step with FixupTargeting
/// after every step, as Run.Step runs it. Twin: tests/sim/combat/retarget-fizzle.test.ts.
/// </summary>
public sealed class RetargetFizzleTests
{
    private const int Caster = 1;
    private const int TankId = 2;
    private const int EnemyA = 10;
    private const int EnemyB = 20;

    private static readonly AbilityDef Bolt = Def(
        "bolt",
        new AbilityShape(ShapeKind.Target),
        new EffectOp[] { new DamageOp(30, "arcane") },
        castTicks: 20,
        range: 5000);

    private static readonly AbilityDef Smite = Def(
        "smite",
        new AbilityShape(ShapeKind.Target),
        new EffectOp[] { new DamageOp(100, "physical") },
        castTicks: 0,
        range: 5000);

    [Fact]
    public void A_bolt_begun_on_A_deals_no_damage_to_anyone_when_A_dies_mid_cast()
    {
        var caster = Make(Caster, ActorKind.Player, "cinder", ActorRole.Damage, x: 0, maxHealth: 200);
        var tank = Make(TankId, ActorKind.Player, "warden", ActorRole.Tank, x: 1000, maxHealth: 200);
        var a = Make(EnemyA, ActorKind.Enemy, "x", ActorRole.None, x: 2000, maxHealth: 100);
        var b = Make(EnemyB, ActorKind.Enemy, "x", ActorRole.None, x: -2000, maxHealth: 100);
        var state = At(0, caster, tank, a, b);
        var rules = Rules.Make(Config(AbilityMap(Bolt, Smite), boundsHalf: 100000));
        var events = new List<SimEvent>();
        var byTick = new Dictionary<int, Command[]>
        {
            [0] = new Command[] { new CastCommand(0, Caster, "bolt", EnemyA) },
            [7] = new Command[] { new CastCommand(7, TankId, "smite", EnemyA) },
        };

        RunWiring.FixupTargeting(state);
        while (state.Tick <= 21)
        {
            rules(state, byTick.TryGetValue(state.Tick, out var c) ? c : new Command[0], events.Add);
            RunWiring.FixupTargeting(state);
            state.Tick += 1;
        }

        Assert.False(state.Actors[EnemyA].Alive);
        Assert.Contains(events, e => e.Type == "cast_complete" && Int(e, "actorId") == Caster && Str(e, "abilityId") == "bolt");
        Assert.DoesNotContain(events, e => e.Type == "damage" && Int(e, "sourceId") == Caster);
        Assert.Equal(100, state.Actors[EnemyB].Health);
        Assert.Equal(EnemyB, state.Actors[Caster].TargetId);
        Assert.False(state.Actors[Caster].CastTargetPinned);
    }
}
