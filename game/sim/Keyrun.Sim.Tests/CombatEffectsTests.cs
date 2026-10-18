using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Xunit;
using static Keyrun.Sim.Tests.CombatTestKit;

namespace Keyrun.Sim.Tests;

/// <summary>Port of tests/sim/combat/effects.test.ts (names kept) plus polarity and absorb edge pins.</summary>
public sealed class CombatEffectsTests
{
    private static ActorState A(int id = 1, ActorKind kind = ActorKind.Player) =>
        Actor(id, kind, maxHealth: 1000, maxResource: 100, role: ActorRole.Damage);

    private static EffectDef Plain(string id, long maxStacks, string? tag = null, EffectModifiers? m = null, EffectPeriodic? p = null) =>
        new(id, maxStacks, tag, m ?? new EffectModifiers(), p);

    [Fact]
    public void ApplyEffectInstance_adds_a_new_instance_with_the_requested_stacks_and_duration()
    {
        var a = A();
        Effects.ApplyEffectInstance(a, Plain("burn", 3, "magic"), 2, 40, 1, 10);
        var e = Assert.Single(a.Effects);
        Assert.Equal(("burn", 2, 1L, 50, (int?)null), (e.EffectId, e.SourceId, e.Stacks, e.EndsTick, e.NextPeriodicTick));
    }

    [Fact]
    public void ApplyEffectInstance_stacks_and_refreshes_an_existing_instance_from_the_same_source_capped_at_maxStacks()
    {
        var a = A();
        var def = Plain("burn", 2);
        Effects.ApplyEffectInstance(a, def, 2, 40, 1, 0);
        Effects.ApplyEffectInstance(a, def, 2, 40, 5, 10);
        var e = Assert.Single(a.Effects);
        Assert.Equal(2, e.Stacks);
        Assert.Equal(50, e.EndsTick);
    }

    [Fact]
    public void ApplyEffectInstance_keeps_separate_instances_per_source()
    {
        var a = A();
        var def = Plain("burn", 5);
        Effects.ApplyEffectInstance(a, def, 2, 40, 1, 0);
        Effects.ApplyEffectInstance(a, def, 3, 40, 1, 0);
        Assert.Equal(2, a.Effects.Count);
    }

    /// <summary>effects.ts:51: a new instance gets at least 1 stack; :45 a refresh arms a missing periodic timer.</summary>
    [Fact]
    public void ApplyEffectInstance_floors_new_stacks_at_1_and_a_periodic_def_arms_its_timer()
    {
        var a = A();
        var dot = Plain("dot", 3, p: new EffectPeriodic(20, new DamageOp(10, "nature")));
        Effects.ApplyEffectInstance(a, dot, 2, 100, 0, 5);
        Assert.Equal(1, a.Effects[0].Stacks);
        Assert.Equal(25, a.Effects[0].NextPeriodicTick);
        a.Effects[0].NextPeriodicTick = null;
        Effects.ApplyEffectInstance(a, dot, 2, 100, 1, 30);
        Assert.Equal(50, a.Effects[0].NextPeriodicTick);
        Assert.Equal(130, a.Effects[0].EndsTick);
    }

    [Fact]
    public void RemoveEffectInstances_removes_all_instances_of_an_effect_id()
    {
        var a = A();
        var def = Plain("burn", 5);
        Effects.ApplyEffectInstance(a, def, 2, 40, 1, 0);
        Effects.ApplyEffectInstance(a, def, 3, 40, 1, 0);
        Effects.RemoveEffectInstances(a, "burn");
        Assert.Empty(a.Effects);
    }

    [Fact]
    public void ExpireEffects_expires_instances_whose_endsTick_has_passed_keeps_ones_still_active()
    {
        var a = A();
        Effects.ApplyEffectInstance(a, Plain("burn", 5), 2, 10, 1, 0);
        Effects.ExpireEffects(a, 9);
        Assert.Single(a.Effects);
        Effects.ExpireEffects(a, 10);
        Assert.Empty(a.Effects);
    }

    [Fact]
    public void DuePeriodicInstances_returns_instances_whose_nextPeriodicTick_has_arrived()
    {
        var a = A();
        var def = Plain("dot", 1, p: new EffectPeriodic(20, new DamageOp(10, "nature")));
        Effects.ApplyEffectInstance(a, def, 2, 100, 1, 0);
        Assert.Empty(Effects.DuePeriodicInstances(a, 19));
        var due = Assert.Single(Effects.DuePeriodicInstances(a, 20));
        Effects.AdvancePeriodic(due, def, 20);
        Assert.Equal(40, due.NextPeriodicTick);
    }

    [Fact]
    public void RawInstances_push_verbatim_and_remove_by_predicate()
    {
        var a = A();
        Effects.PushRawEffectInstance(a, new EffectInstance { EffectId = "window:w:cinder", SourceId = 3, Stacks = 1, EndsTick = 99 });
        Effects.PushRawEffectInstance(a, new EffectInstance { EffectId = "lockfail", SourceId = 3, Stacks = 2, EndsTick = 99 });
        Assert.Equal(0, Effects.AggregateModifiers(a, NoDefs).DamageTakenBp);
        Effects.RemoveEffectInstanceWhere(a, e => e.EffectId.StartsWith("window:", System.StringComparison.Ordinal));
        Assert.Equal("lockfail", Assert.Single(a.Effects).EffectId);
    }

    [Fact]
    public void AggregateModifiers_sums_bp_modifiers_across_stacks_and_multiple_effects()
    {
        var a = A();
        var haste = Plain("haste", 3, m: new EffectModifiers(HasteBp: 500));
        var slow = Plain("slow", 1, m: new EffectModifiers(MovementBp: -3000));
        Effects.ApplyEffectInstance(a, haste, 2, 100, 2, 0);
        Effects.ApplyEffectInstance(a, slow, 2, 100, 1, 0);
        var totals = Effects.AggregateModifiers(a, Defs(haste, slow));
        Assert.Equal(1000, totals.HasteBp);
        Assert.Equal(-3000, totals.MovementBp);
        Assert.Equal(0, totals.DamageDoneBp);
    }

    // ---- removeDispellableEffects (wildcard removeEffect, effectId: "*") ----

    private static readonly EffectDef Slow = Plain("slow", 1, "magic", new EffectModifiers(MovementBp: -4000));
    private static readonly EffectDef ChillDehaste = Plain("chillx", 1, "physical", new EffectModifiers(HasteBp: -3000));
    private static readonly EffectDef Haste = Plain("haste", 1, "magic", new EffectModifiers(HasteBp: 1500));
    private static readonly EffectDef StunNotDispellable = Plain("stun", 1);
    private static readonly System.Collections.Generic.IReadOnlyDictionary<string, EffectDef> DispelDefs = Defs(Slow, ChillDehaste, Haste, StunNotDispellable);

    [Fact]
    public void RemoveDispellable_cleanse_style_an_ally_caster_strips_hostile_dispellable_effects_leaves_buffs_and_non_dispellable_effects()
    {
        var caster = A(2);
        var target = A(1);
        foreach (var d in new[] { Slow, ChillDehaste, Haste, StunNotDispellable })
        {
            Effects.ApplyEffectInstance(target, d, caster.Id, 100, 1, 0);
        }
        EffectsRemove.RemoveDispellableEffects(caster.Kind, target, DispelDefs);
        Assert.Equal(new[] { "haste", "stun" }, target.Effects.Select(e => e.EffectId));
    }

    [Fact]
    public void RemoveDispellable_purge_style_an_enemy_caster_strips_beneficial_dispellable_effects_leaves_hostile_effects()
    {
        var caster = A(2);
        var target = A(3, ActorKind.Enemy);
        Effects.ApplyEffectInstance(target, Haste, caster.Id, 100, 1, 0);
        Effects.ApplyEffectInstance(target, Slow, caster.Id, 100, 1, 0);
        EffectsRemove.RemoveDispellableEffects(caster.Kind, target, DispelDefs);
        Assert.Equal(new[] { "slow" }, target.Effects.Select(e => e.EffectId));
    }

    [Fact]
    public void RemoveDispellable_removes_in_insertion_order_deterministically_preserving_the_order_of_survivors()
    {
        var caster = A(2);
        var target = A(1);
        foreach (var d in new[] { Haste, Slow, StunNotDispellable, ChillDehaste })
        {
            Effects.ApplyEffectInstance(target, d, caster.Id, 100, 1, 0);
        }
        EffectsRemove.RemoveDispellableEffects(caster.Kind, target, DispelDefs);
        EffectsRemove.RemoveDispellableEffects(caster.Kind, target, DispelDefs);
        Assert.Equal(new[] { "haste", "stun" }, target.Effects.Select(e => e.EffectId));
    }

    [Fact]
    public void RemoveDispellable_a_literal_effectId_still_removes_only_that_one_effect()
    {
        var target = A(1);
        Effects.ApplyEffectInstance(target, Slow, 2, 100, 1, 0);
        Effects.ApplyEffectInstance(target, ChillDehaste, 2, 100, 1, 0);
        Effects.RemoveEffectInstances(target, "slow");
        Assert.Equal(new[] { "chillx" }, target.Effects.Select(e => e.EffectId));
    }

    /// <summary>effects-remove.ts:32-48: mixed or empty signals are hostile; a heal periodic is beneficial, a damage periodic hostile.</summary>
    [Fact]
    public void EffectPolarity_classifies_from_modifiers_and_periodic_ops_failing_closed_to_hostile()
    {
        Assert.Equal(EffectPolarity.Hostile, EffectsRemove.PolarityOf(Plain("stunned", 1, "magic")));
        Assert.Equal(EffectPolarity.Beneficial, EffectsRemove.PolarityOf(Plain("aegis", 1, null, new EffectModifiers(Absorb: 50))));
        Assert.Equal(EffectPolarity.Beneficial, EffectsRemove.PolarityOf(Plain("brace", 1, null, new EffectModifiers(DamageTakenBp: -4000))));
        Assert.Equal(EffectPolarity.Hostile, EffectsRemove.PolarityOf(Plain("mixed", 1, null, new EffectModifiers(HasteBp: 500, MovementBp: -100))));
        Assert.Equal(EffectPolarity.Beneficial, EffectsRemove.PolarityOf(Plain("hot", 1, p: new EffectPeriodic(20, new HealOp(5)))));
        Assert.Equal(EffectPolarity.Hostile, EffectsRemove.PolarityOf(Plain("dot", 1, p: new EffectPeriodic(20, new DamageOp(5, "fire")))));
    }

    [Fact]
    public void ConsumeAbsorb_consumes_whole_stacks_to_cover_the_request()
    {
        var a = A();
        var shield = Plain("shield", 3, m: new EffectModifiers(Absorb: 50));
        Effects.ApplyEffectInstance(a, shield, 2, 100, 3, 0);
        Assert.Equal(70, Effects.ConsumeAbsorb(a, Defs(shield), 70));
        Assert.Equal(1, a.Effects[0].Stacks);
    }

    [Fact]
    public void ConsumeAbsorb_removes_the_instance_entirely_once_fully_consumed()
    {
        var a = A();
        var shield = Plain("shield", 1, m: new EffectModifiers(Absorb: 50));
        Effects.ApplyEffectInstance(a, shield, 2, 100, 1, 0);
        Assert.Equal(50, Effects.ConsumeAbsorb(a, Defs(shield), 200));
        Assert.Empty(a.Effects);
    }

    /// <summary>effects.ts:122-135: absorb drains instances in insertion order and a non-absorb effect is untouched.</summary>
    [Fact]
    public void ConsumeAbsorb_drains_absorb_instances_in_insertion_order()
    {
        var a = A();
        var small = Plain("small", 1, m: new EffectModifiers(Absorb: 20));
        var big = Plain("big", 1, m: new EffectModifiers(Absorb: 100));
        var haste = Plain("haste", 1, m: new EffectModifiers(HasteBp: 100));
        Effects.ApplyEffectInstance(a, small, 2, 100, 1, 0);
        Effects.ApplyEffectInstance(a, haste, 2, 100, 1, 0);
        Effects.ApplyEffectInstance(a, big, 2, 100, 1, 0);
        var defs = Defs(small, big, haste);
        Assert.Equal(120, Effects.AbsorbPool(a, defs));
        Assert.Equal(30, Effects.ConsumeAbsorb(a, defs, 30));
        Assert.Equal(new[] { "haste" }, a.Effects.Select(e => e.EffectId));
    }
}
