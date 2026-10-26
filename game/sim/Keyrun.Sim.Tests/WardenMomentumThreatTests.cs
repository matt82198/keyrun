using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Xunit;
using static Keyrun.Sim.Tests.CombatCastTestKit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// Warden threat after R-THREAT-2 (design/THREAT-MODEL.md section 7). Momentum spent no longer writes threat.
/// THREAT-MODEL 7.1, Momentum spent row: "**Retired.** Reckoning's 20000 coefficient carries the spender's threat".
/// THREAT-MODEL 7.6: "game/sim/Keyrun.Sim.Tests/WardenMomentumThreatTests.cs: rewritten as damage-coefficient tests
/// (Momentum threat retired)."
/// Threat comes only from damage (and healing) through trunc(amount x abilityBp x classBp / 10^8), section 7.1. The
/// defs mirror content/data/abilities/warden.json (Reckoning, Vigil) and cinder.json's Heat cost, driven through the
/// real <see cref="Rules.Make"/> step.
/// </summary>
public sealed class WardenMomentumThreatTests
{
    // Reckoning: 400 damage, threat coefficient 20000 (the amount-less threat op), Warden classBp 20000.
    private static readonly AbilityDef Reckoning = Def(
        "warden.reckoning",
        new AbilityShape(ShapeKind.Target),
        new EffectOp[] { new DamageOp(400, "physical"), new ThreatOp(null, 20000) },
        gcdTicks: 30,
        costResource: "momentum",
        costAmount: 100,
        range: 5000);

    private static readonly AbilityDef Vigil = Def(
        "warden.vigil",
        new AbilityShape(ShapeKind.Self),
        new EffectOp[] { new HealOp(10) },
        costResource: "momentum",
        costAmount: 50);

    // Cinder Detonate: 10 fire damage, no threat op, classBp 10000 (Cinder).
    private static readonly AbilityDef Detonate = Def(
        "cinder.detonate",
        new AbilityShape(ShapeKind.Target),
        new EffectOp[] { new DamageOp(10, "fire") },
        costResource: "heat",
        costAmount: 30,
        range: 5000);

    private static (ActorState Warden, ActorState Enemy, SimState State) Pull(long momentum)
    {
        var warden = Make(1, ActorKind.Player, classId: "warden", role: ActorRole.Tank, maxHealth: 1000, maxResource: 100);
        warden.ThreatBp = 20000; // THREAT-MODEL 7.2: Warden classBp 20000 (Make does not carry it; RunTestKit maps it).
        warden.Resource = momentum;
        var enemy = Make(2, ActorKind.Enemy, maxHealth: 10000);
        return (warden, enemy, At(0, warden, enemy));
    }

    private static long ThreatOf(ActorState actor, int sourceId) => actor.Threat.TryGetValue(sourceId, out var v) ? v : 0;

    private static void Step(SimState state, params Command[] cmds) =>
        Rules.Make(Config(AbilityMap(Reckoning, Vigil, Detonate)))(state, cmds, _ => { });

    [Fact]
    public void Reckoning_threat_is_its_400_damage_times_the_20000_coefficient_times_the_Warden_20000_and_the_Momentum_spent_adds_nothing()
    {
        // THREAT-MODEL 7.1: trunc(400 x 20000 x 20000 / 10^8) = 1600. Spending 100 Momentum adds 0 on top.
        var (warden, enemy, state) = Pull(100);
        Step(state, Cast(1, "warden.reckoning", 2));
        Assert.Equal(0, warden.Resource);
        Assert.Equal(1600, ThreatOf(enemy, 1));
    }

    [Fact]
    public void Vigil_spends_50_Momentum_and_threatens_nothing_even_while_the_Warden_targets_an_enemy()
    {
        // THREAT-MODEL 7.1: Momentum spent makes no threat. Vigil has no damage and its self-heal lands on no enemy's
        // table, so the enemy the Warden targets gains nothing.
        var (warden, enemy, state) = Pull(50);
        warden.TargetId = 2;
        Step(state, Cast(1, "warden.vigil"));
        Assert.Equal(0, warden.Resource);
        Assert.Empty(enemy.Threat);
        Assert.Empty(warden.Threat);
    }

    [Fact]
    public void Two_Reckonings_accumulate_their_damage_threat_only()
    {
        // THREAT-MODEL 7.1: 1600 + 1600 = 3200, with no Momentum term.
        var (warden, enemy, state) = Pull(100);
        Step(state, Cast(1, "warden.reckoning", 2));
        state.Tick = 100;
        warden.Resource = 100;
        Step(state, Cast(1, "warden.reckoning", 2));
        Assert.Equal(3200, ThreatOf(enemy, 1));
    }

    [Fact]
    public void Spending_a_resource_that_is_not_Momentum_adds_no_threat_beyond_the_damage_itself()
    {
        // THREAT-MODEL 7.1: Cinder Detonate, 10 fire damage x classBp 10000: trunc(10 x 10000 x 10000 / 10^8) = 10.
        // The 30 Heat spent adds nothing on top.
        var cinder = Make(1, ActorKind.Player, classId: "cinder", maxHealth: 1000, maxResource: 100);
        cinder.Resource = 100;
        var enemy = Make(2, ActorKind.Enemy, maxHealth: 10000);
        Step(At(0, cinder, enemy), Cast(1, "cinder.detonate", 2));
        Assert.Equal(70, cinder.Resource);
        Assert.Equal(10, ThreatOf(enemy, 1));
    }

    [Fact]
    public void Momentum_spent_with_no_hostile_target_threatens_no_one()
    {
        var (warden, enemy, state) = Pull(50);
        Step(state, Cast(1, "warden.vigil"));
        Assert.Equal(0, warden.Resource);
        Assert.Empty(enemy.Threat);
        Assert.Empty(warden.Threat);
    }

    [Fact]
    public void Momentum_spent_while_targeting_an_ally_puts_no_threat_on_the_ally()
    {
        var (warden, enemy, state) = Pull(50);
        var ally = Make(3, ActorKind.Player, classId: "chanter", maxHealth: 1000);
        state.Actors[3] = ally;
        warden.TargetId = 3;
        Step(state, Cast(1, "warden.vigil"));
        Assert.Equal(0, warden.Resource);
        Assert.Empty(ally.Threat);
        Assert.Empty(enemy.Threat);
    }

    [Fact]
    public void A_refused_cast_spends_no_Momentum_and_generates_no_threat()
    {
        var (warden, enemy, state) = Pull(99);
        Step(state, Cast(1, "warden.reckoning", 2));
        Assert.Equal(99, warden.Resource);
        Assert.Empty(enemy.Threat);
    }
}
