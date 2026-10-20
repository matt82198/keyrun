using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Xunit;
using static Keyrun.Sim.Tests.CombatCastTestKit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// design/BIBLE.md section 3 ("Cast bars are the enemy's language": every dangerous cast shows a bar drawn ahead of
/// resolution; "A kick counts when its intended tick falls inside the cast") and section 9 (the Choir teaches the
/// kick on the Cantor's 4-second heal). A cast in flight is the contract the party reads and kicks against, so a
/// second <see cref="Casting.StartCast"/> on a busy caster is rejected with "casting" and never replaces the first:
/// the in-flight cast id, start and end ticks are untouched, and nothing is spent. Cancelling is the explicit
/// stopcast command's job, never a silent side effect of a new cast.
/// </summary>
public class StartCastBusyGuardTests
{
    private const int CasterId = 1;
    private const int TargetId = 2;

    private static readonly AbilityDef Heal = Def("choir.heal", new AbilityShape(ShapeKind.Self), new EffectOp[] { new HealOp(50) },
        castTicks: 80, costResource: "none", interruptible: true);

    private static readonly AbilityDef Lock = Def("boss.lock", new AbilityShape(ShapeKind.Self), new EffectOp[] { new HealOp(1) },
        castTicks: 40, costResource: "none");

    private static readonly AbilityDef Instant = Def("enemy.instant", new AbilityShape(ShapeKind.Target), new EffectOp[] { new DamageOp(10, "physical") },
        castTicks: 0, costResource: "none", range: 40000);

    [Fact]
    public void Second_cast_while_casting_is_rejected_and_keeps_the_first_cast()
    {
        var caster = Make(CasterId, ActorKind.Enemy);
        var target = Make(TargetId, ActorKind.Player, x: 1000);
        var state = At(10, caster, target);
        Assert.Null(Casting.StartCast(state, caster, Heal, null, null, 10));

        var err = Casting.StartCast(state, caster, Lock, null, null, 20);

        Assert.Equal("casting", err);
        Assert.Equal("choir.heal", caster.CastId);
        Assert.Equal(10, caster.CastStartTick);
        Assert.Equal(90, caster.CastEndsTick);
    }

    [Fact]
    public void Instant_while_casting_is_rejected_and_spends_no_charge()
    {
        var caster = Make(CasterId, ActorKind.Enemy);
        var target = Make(TargetId, ActorKind.Player, x: 1000);
        var state = At(10, caster, target);
        Assert.Null(Casting.StartCast(state, caster, Heal, null, null, 10));

        Assert.Equal("casting", Casting.StartCast(state, caster, Instant, TargetId, null, 20));

        Assert.Equal("choir.heal", caster.CastId);
        Assert.True(Cooldowns.IsAbilityReady(caster, Instant, 20));
    }

    [Fact]
    public void A_dead_caster_still_starts_its_on_death_cast()
    {
        // BIBLE section 9 (The Seed drops its Window on death): the onDeath step is the only cast a dead actor makes,
        // and it must fire even when the Seed died mid-cast (oracle bots-hv-k10-s7, seed.deathBloom at t1776).
        var caster = Make(CasterId, ActorKind.Enemy);
        var target = Make(TargetId, ActorKind.Player, x: 1000);
        var state = At(10, caster, target);
        Assert.Null(Casting.StartCast(state, caster, Heal, null, null, 10));
        caster.Alive = false;

        Assert.Null(Casting.StartCast(state, caster, Instant, TargetId, null, 20));
        Assert.Equal("enemy.instant", caster.CastId);
    }

    [Fact]
    public void A_new_cast_starts_once_the_first_is_stopped()
    {
        var caster = Make(CasterId, ActorKind.Enemy);
        var state = At(10, caster);
        Assert.Null(Casting.StartCast(state, caster, Heal, null, null, 10));
        Casting.Stopcast(caster, _ => { }, 15);

        Assert.Null(Casting.StartCast(state, caster, Lock, null, null, 20));
        Assert.Equal("boss.lock", caster.CastId);
    }
}
