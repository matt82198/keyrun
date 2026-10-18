using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Xunit;
using static Keyrun.Sim.Tests.CombatTestKit;

namespace Keyrun.Sim.Tests;

/// <summary>Port of tests/sim/combat/threat.test.ts (names kept).</summary>
public sealed class CombatThreatTests
{
    private static ActorState A() => Actor(1, ActorKind.Enemy);

    [Fact]
    public void AddThreat_adds_plain_amounts_with_no_multiplier()
    {
        var a = A();
        Threat.AddThreat(a, 5, 100);
        Threat.AddThreat(a, 5, 50);
        Assert.Equal(150, a.Threat[5]);
    }

    [Fact]
    public void AddThreat_applies_a_basis_point_multiplier_truncating_toward_zero()
    {
        var a = A();
        Threat.AddThreat(a, 5, 100, 15000);
        Assert.Equal(150, a.Threat[5]);
        Threat.AddThreat(a, 6, 100, 3333);
        Assert.Equal(33, a.Threat[6]);
    }

    /// <summary>threat.ts:11 Math.trunc: -100 * 3333 / 10000 = -33.33 truncates to -33 (floor would be -34).</summary>
    [Fact]
    public void AddThreat_truncates_a_negative_delta_toward_zero()
    {
        var a = A();
        Threat.AddThreat(a, 5, -100, 3333);
        Assert.Equal(-33, a.Threat[5]);
    }

    [Fact]
    public void HighestThreatSource_returns_null_with_no_threat_entries() =>
        Assert.Null(Threat.HighestThreatSource(A()));

    [Fact]
    public void HighestThreatSource_returns_the_source_with_the_highest_value()
    {
        var a = A();
        Threat.AddThreat(a, 5, 100);
        Threat.AddThreat(a, 6, 300);
        Threat.AddThreat(a, 7, 200);
        Assert.Equal(6, Threat.HighestThreatSource(a));
    }

    /// <summary>threat.ts:21 strict &gt; over ascending integer keys: a tie keeps the lowest id.</summary>
    [Fact]
    public void HighestThreatSource_breaks_ties_toward_the_lowest_id()
    {
        var a = A();
        Threat.AddThreat(a, 9, 200);
        Threat.AddThreat(a, 4, 200);
        Assert.Equal(4, Threat.HighestThreatSource(a));
    }

    [Fact]
    public void ClearThreat_removes_only_the_given_sources_entry()
    {
        var a = A();
        Threat.AddThreat(a, 5, 100);
        Threat.AddThreat(a, 6, 200);
        Threat.ClearThreat(a, 5);
        Assert.False(a.Threat.ContainsKey(5));
        Assert.Equal(200, a.Threat[6]);
    }
}

/// <summary>Port of tests/sim/combat/cooldowns.test.ts (names kept).</summary>
public sealed class CombatCooldownTests
{
    private static AbilityDef Kick(int cooldownTicks = 40, long charges = 2) =>
        Ability(new AbilityShape(ShapeKind.Target), new EffectOp[] { new InterruptOp() }, id: "kick", gcdTicks: 15, cooldownTicks: cooldownTicks, charges: charges);

    private static ActorState P() => Actor(1, ActorKind.Player, maxResource: 100, role: ActorRole.Damage);

    [Fact]
    public void Gcd_is_ready_at_tick_0_for_a_fresh_actor_and_not_ready_right_after_starting()
    {
        var actor = P();
        Assert.True(Cooldowns.IsGcdReady(actor, 0));
        Cooldowns.StartGcd(actor, 0, 15);
        Assert.False(Cooldowns.IsGcdReady(actor, 14));
        Assert.True(Cooldowns.IsGcdReady(actor, 15));
    }

    /// <summary>cooldowns.ts:17: a non-positive gcd leaves gcdReadyTick untouched.</summary>
    [Fact]
    public void Gcd_of_zero_ticks_does_not_move_the_ready_tick()
    {
        var actor = P();
        actor.GcdReadyTick = 7;
        Cooldowns.StartGcd(actor, 3, 0);
        Assert.Equal(7, actor.GcdReadyTick);
    }

    [Fact]
    public void Charges_starts_with_full_charges_and_none_on_cooldown() =>
        Assert.Equal(2, Cooldowns.ChargesAvailable(P(), Kick(), 0));

    [Fact]
    public void Charges_consumes_one_charge_at_a_time_and_recharges_after_cooldownTicks()
    {
        var actor = P();
        var def = Kick();
        Cooldowns.ConsumeCharge(actor, def, 0);
        Assert.Equal(1, Cooldowns.ChargesAvailable(actor, def, 0));
        Assert.Equal(1, Cooldowns.ChargesAvailable(actor, def, 39));
        Assert.Equal(2, Cooldowns.ChargesAvailable(actor, def, 40));
    }

    [Fact]
    public void Charges_chains_recharge_timers_when_both_charges_are_consumed()
    {
        var actor = P();
        var def = Kick();
        Cooldowns.ConsumeCharge(actor, def, 0);
        Cooldowns.ConsumeCharge(actor, def, 5);
        Assert.Equal(0, Cooldowns.ChargesAvailable(actor, def, 5));
        Assert.Equal(1, Cooldowns.ChargesAvailable(actor, def, 40));
        Assert.Equal(1, Cooldowns.ChargesAvailable(actor, def, 79));
        Assert.Equal(2, Cooldowns.ChargesAvailable(actor, def, 80));
        Assert.Equal(80, actor.Cooldowns["kick"]);
    }

    [Fact]
    public void Charges_treats_cooldownTicks_0_as_always_fully_available()
    {
        var actor = P();
        var def = Kick(cooldownTicks: 0, charges: 3);
        Cooldowns.ConsumeCharge(actor, def, 0);
        Assert.Equal(3, Cooldowns.ChargesAvailable(actor, def, 0));
        Assert.False(actor.Cooldowns.ContainsKey("kick"));
    }

    [Fact]
    public void IsAbilityReady_requires_both_gcd_and_a_charge()
    {
        var actor = P();
        var def = Kick();
        Cooldowns.StartGcd(actor, 0, 15);
        Assert.False(Cooldowns.IsAbilityReady(actor, def, 10));
        Assert.True(Cooldowns.IsAbilityReady(actor, def, 15));
        Cooldowns.ConsumeCharge(actor, def, 15);
        Cooldowns.ConsumeCharge(actor, def, 15);
        Assert.False(Cooldowns.IsAbilityReady(actor, def, 20));
    }
}
