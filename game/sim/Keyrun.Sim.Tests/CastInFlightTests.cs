using System.Collections.Generic;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Xunit;
using static Keyrun.Sim.Tests.CombatCastTestKit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// R2 (C# architecture review): the cast in flight is one <see cref="CastInFlight"/> value on
/// <see cref="ActorState.Cast"/>, and <see cref="Casting.End"/> is its only clearer. No half-cleared actor is
/// observable: once <see cref="Casting.CompleteCastOn"/> returns, the actor holds no cast and no pin, while the
/// returned value still carries the pinned start target that the cast's Windows resolve on. A second End is a no-op.
/// </summary>
public sealed class CastInFlightTests
{
    private const int Caster = 1;
    private const int EnemyA = 10;
    private const int EnemyB = 20;

    private static readonly AbilityDef Bolt = Def(
        "bolt",
        new AbilityShape(ShapeKind.Target),
        new EffectOp[] { new DamageOp(30, "arcane") },
        castTicks: 20,
        range: 5000);

    [Fact]
    public void After_CompleteCastOn_the_actor_holds_no_cast_and_the_returned_cast_carries_the_pin()
    {
        var caster = Make(Caster, ActorKind.Player, "cinder", ActorRole.Damage, x: 0, maxHealth: 200);
        var a = Make(EnemyA, ActorKind.Enemy, "x", ActorRole.None, x: 2000, maxHealth: 100);
        var b = Make(EnemyB, ActorKind.Enemy, "x", ActorRole.None, x: -2000, maxHealth: 100);
        var state = At(0, caster, a, b);
        var ctx = new CastContext(AbilityMap(Bolt), new Dictionary<string, EffectDef>(), 0);

        Assert.Null(Casting.StartCast(state, caster, Bolt, EnemyA, null, 0, null, ctx));
        Casting.SetTarget(caster, EnemyB); // a mid-cast retarget pins the start target
        Assert.Equal(new CastInFlight("bolt", 0, 20, false, EnemyA, true), caster.Cast);

        var events = new List<SimEvent>();
        CastInFlight? ended = Casting.CompleteCastOn(state, caster, Bolt, new List<ActorState> { a }, ctx, events.Add);

        Assert.Null(caster.Cast);
        Assert.Null(caster.CastId);
        Assert.Null(caster.CastStartTick);
        Assert.Null(caster.CastEndsTick);
        Assert.False(caster.CastIsOverheat);
        Assert.False(caster.CastTargetPinned);
        Assert.Null(caster.CastTargetId);
        Assert.Equal(EnemyB, caster.TargetId);
        Assert.Equal(new CastInFlight("bolt", 0, 20, false, EnemyA, true), ended);
        Assert.Equal(EnemyA, Casting.TargetOfEnded(caster, ended));
        Assert.Equal(EnemyB, Casting.CastTargetOf(caster));

        Assert.Null(Casting.End(caster, CastEnd.Completed));
        Assert.Null(caster.Cast);
        Assert.False(caster.CastTargetPinned);
        Assert.Contains(events, e => e.Type == "cast_complete" && Str(e, "abilityId") == "bolt");
    }

    [Fact]
    public void End_clears_every_cast_field_in_one_call_for_each_reason()
    {
        foreach (var why in new[] { CastEnd.Completed, CastEnd.Stopped, CastEnd.Interrupted, CastEnd.Released })
        {
            var caster = Make(Caster, ActorKind.Player, "cinder", ActorRole.Damage, x: 0, maxHealth: 200);
            caster.Cast = new CastInFlight("bolt", 3, 23, true, EnemyA, true);

            var ended = Casting.End(caster, why);

            Assert.Equal(new CastInFlight("bolt", 3, 23, true, EnemyA, true), ended);
            Assert.Null(caster.Cast);
            Assert.Null(caster.CastId);
            Assert.Null(caster.CastStartTick);
            Assert.Null(caster.CastEndsTick);
            Assert.False(caster.CastIsOverheat);
            Assert.False(caster.CastTargetPinned);
            Assert.Null(caster.CastTargetId);
            Assert.Null(Casting.End(caster, why));
        }
    }
}
