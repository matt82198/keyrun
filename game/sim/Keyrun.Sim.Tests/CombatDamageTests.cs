using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Xunit;
using static Keyrun.Sim.Tests.CombatTestKit;

namespace Keyrun.Sim.Tests;

/// <summary>Port of tests/sim/combat/damage.test.ts (names kept) plus the trunc and Pact pins.</summary>
public sealed class CombatDamageTests
{
    private static ActorState Target(long health = 100)
    {
        var a = Actor(2, ActorKind.Enemy, maxHealth: health);
        a.Health = health;
        return a;
    }

    [Fact]
    public void ApplyModifiers_passes_base_amount_through_with_zero_bp_modifiers() =>
        Assert.Equal(100, Damage.ApplyModifiers(100, 0, 0));

    [Fact]
    public void ApplyModifiers_applies_plus20pct_source_and_minus10pct_target_multiplicatively_truncating_toward_zero() =>
        Assert.Equal(108, Damage.ApplyModifiers(100, 2000, -1000));

    [Fact]
    public void ApplyModifiers_never_goes_negative_even_with_large_negative_modifiers() =>
        Assert.Equal(0, Damage.ApplyModifiers(100, 0, -20000));

    /// <summary>
    /// damage.ts:30-31 use Math.trunc, not Math.floor (ORACLE.md section 8: no trace pins this). afterSource =
    /// trunc(100 * -5001 / 10000) = trunc(-50.01) = -50 (floor would be -51); afterTarget = trunc(-50 * -10000 /
    /// 10000) = 50 (floor path: 51).
    /// </summary>
    [Fact]
    public void ApplyModifiers_truncates_a_negative_intermediate_toward_zero_not_floor() =>
        Assert.Equal(50, Damage.ApplyModifiers(100, -15001, -20000));

    [Fact]
    public void ApplyDamage_reduces_health_by_the_modified_amount_and_floors_at_0()
    {
        var t = Target(50);
        var (_, emit) = Collector();
        var result = Damage.ApplyDamage(5, t, 80, 0, 0, NoDefs, emit, 1);
        Assert.Equal(50, result.HealthDamage);
        Assert.Equal(0, t.Health);
        Assert.False(t.Alive);
    }

    [Fact]
    public void ApplyDamage_emits_a_damage_event_and_a_death_event_exactly_once_on_lethal_damage()
    {
        var t = Target(10);
        var (events, emit) = Collector();
        Damage.ApplyDamage(5, t, 10, 0, 0, NoDefs, emit, 1);
        Assert.Equal(2, events.Count);
        Assert.Equal(
            "{\"absorbed\":0,\"healthDamage\":10,\"modifiedAmount\":10,\"rawAmount\":10,\"sourceId\":1,\"targetId\":2,\"tick\":5,\"type\":\"damage\"}",
            events[0].ToCanonicalJson());
        Assert.Equal("{\"actorId\":2,\"sourceId\":1,\"tick\":5,\"type\":\"death\"}", events[1].ToCanonicalJson());
    }

    [Fact]
    public void ApplyDamage_does_not_re_emit_death_or_any_event_on_further_damage_to_an_already_dead_actor()
    {
        var t = Target(10);
        var (events, emit) = Collector();
        Damage.ApplyDamage(5, t, 10, 0, 0, NoDefs, emit, 1);
        events.Clear();
        Damage.ApplyDamage(6, t, 10, 0, 0, NoDefs, emit, 1);
        Assert.Empty(events);
    }

    [Fact]
    public void ApplyDamage_consumes_absorb_before_health_in_pipeline_order_base_mods_absorb_health()
    {
        var t = Target(100);
        var shield = new EffectDef("shield", 1, null, new EffectModifiers(Absorb: 30));
        Effects.ApplyEffectInstance(t, shield, 1, 100, 1, 0);
        var (_, emit) = Collector();
        var result = Damage.ApplyDamage(0, t, 50, 0, 0, Defs(shield), emit, 1);
        Assert.Equal(30, result.Absorbed);
        Assert.Equal(20, result.HealthDamage);
        Assert.Equal(80, t.Health);
    }

    [Fact]
    public void ApplyHeal_caps_healing_at_maxHealth()
    {
        var t = Target(100);
        t.Health = 90;
        var (events, emit) = Collector();
        var applied = Damage.ApplyHeal(0, t, 50, 1, emit);
        Assert.Equal(10, applied);
        Assert.Equal(100, t.Health);
        Assert.Equal("{\"amount\":10,\"sourceId\":1,\"targetId\":2,\"tick\":0,\"type\":\"heal\"}", Assert.Single(events).ToCanonicalJson());
    }

    /// <summary>damage.ts:55: the Pact weighting runs on the raw amount BEFORE the bp modifiers; the event keeps the raw.</summary>
    [Fact]
    public void ApplyDamage_applies_the_pact_weighting_before_modifiers_and_reports_the_raw_amount()
    {
        var t = Target(1000);
        var (events, emit) = Collector();
        var r = Damage.ApplyDamage(3, t, 100, 0, -5000, NoDefs, emit, 1, raw => raw * 2);
        Assert.Equal(100, r.RawAmount);
        Assert.Equal(100, r.ModifiedAmount); // trunc(200 * 5000 / 10000)
        Assert.Equal(900, t.Health);
        Assert.Equal(100, Assert.Single(events).Get("rawAmount") is JsInt ri ? ri.Value : -1);
    }

    /// <summary>damage.ts:53: a dead target is inert before the Pact hook runs (the hook is never consulted).</summary>
    [Fact]
    public void ApplyDamage_on_a_dead_target_never_calls_the_pact_weighting()
    {
        var t = Target(10);
        Damage.ApplyDamage(1, t, 10, 0, 0, NoDefs, _ => { }, 1);
        var called = false;
        var r = Damage.ApplyDamage(2, t, 10, 0, 0, NoDefs, _ => { }, 1, raw => { called = true; return raw; });
        Assert.False(called);
        Assert.Equal(new DamageResult(10, 0, 0, 0, false), r);
    }
}
