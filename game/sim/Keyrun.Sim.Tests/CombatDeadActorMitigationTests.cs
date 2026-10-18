using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Xunit;
using static Keyrun.Sim.Tests.CombatTestKit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// Port of the damage.ts and targeting.ts cases of tests/sim/combat/dead-actors.test.ts (names kept). The
/// "single-target cast on a dead target" cases drive casting.ts and port with lane 2B.
/// </summary>
public sealed class CombatDeadActorTests
{
    private static ActorState A(int id, ActorKind kind, long x, long maxHealth = 100) =>
        Actor(id, kind, x, maxHealth: maxHealth, maxResource: 100);

    private static void Kill(ActorState a) => Damage.ApplyDamage(1, a, a.Health, 0, 0, NoDefs, _ => { }, 99);

    private static readonly EffectOp Dmg = new DamageOp(10, "physical");
    private static readonly EffectOp Heal = new HealOp(10);

    [Fact]
    public void ApplyHeal_on_a_dead_actor_leaves_hp_at_0_keeps_alive_false_returns_0_and_emits_no_heal_event()
    {
        var a = A(2, ActorKind.Player, 0);
        Kill(a);
        var (events, emit) = Collector();
        Assert.Equal(0, Damage.ApplyHeal(2, a, 40, 1, emit));
        Assert.Equal(0, a.Health);
        Assert.False(a.Alive);
        Assert.Empty(events);
    }

    [Fact]
    public void ApplyDamage_on_a_dead_actor_is_a_no_op_hp_stays_0_no_damage_or_death_event_absorb_untouched_died_false()
    {
        var shield = new EffectDef("shield", 1, null, new EffectModifiers(Absorb: 50));
        var a = A(2, ActorKind.Enemy, 0, 10);
        Kill(a);
        Effects.ApplyEffectInstance(a, shield, 1, 100, 1, 1);
        var (events, emit) = Collector();
        var r = Damage.ApplyDamage(2, a, 30, 0, 0, Defs(shield), emit, 1);
        Assert.Equal(0, a.Health);
        Assert.False(a.Alive);
        Assert.False(r.Died);
        Assert.Equal(0, r.HealthDamage);
        Assert.Equal(0, r.Absorbed);
        Assert.Empty(events);
        Assert.Equal(50, Effects.AbsorbPool(a, Defs(shield)));
    }

    [Fact]
    public void ResolveTargets_circle_hostile_skips_a_dead_enemy_in_radius()
    {
        var caster = A(1, ActorKind.Player, 0);
        var live = A(2, ActorKind.Enemy, 50);
        var dead = A(3, ActorKind.Enemy, 60);
        Kill(dead);
        var ids = Targeting.ResolveTargets(State(caster, live, dead), caster, Ability(new AbilityShape(ShapeKind.Circle, Radius: 100), new[] { Dmg }), Affinity.Hostile);
        Assert.Equal(new[] { 2 }, ids.Select(a => a.Id));
    }

    [Fact]
    public void ResolveTargets_circle_friendly_skips_a_dead_ally_in_radius()
    {
        var caster = A(1, ActorKind.Player, 0);
        var live = A(2, ActorKind.Player, 50);
        var dead = A(3, ActorKind.Player, 60);
        Kill(dead);
        var ids = Targeting.ResolveTargets(State(caster, live, dead), caster, Ability(new AbilityShape(ShapeKind.Circle, Radius: 100), new[] { Heal }), Affinity.Friendly);
        Assert.Equal(new[] { 1, 2 }, ids.Select(a => a.Id));
    }

    [Fact]
    public void ResolveTargets_circle_friendly_does_not_add_a_dead_caster_to_its_own_heal()
    {
        var caster = A(1, ActorKind.Player, 0);
        var live = A(2, ActorKind.Player, 50);
        Kill(caster);
        var ids = Targeting.ResolveTargets(State(caster, live), caster, Ability(new AbilityShape(ShapeKind.Circle, Radius: 100), new[] { Heal }), Affinity.Friendly);
        Assert.Equal(new[] { 2 }, ids.Select(a => a.Id));
    }

    [Fact]
    public void ResolveTargets_cone_skips_a_dead_enemy_ahead()
    {
        var caster = A(1, ActorKind.Player, 0);
        var live = A(2, ActorKind.Enemy, 100);
        var dead = A(3, ActorKind.Enemy, 150);
        Kill(dead);
        var def = Ability(new AbilityShape(ShapeKind.Cone, Radius: 200, Angle1024: 256), new[] { Dmg });
        Assert.Equal(new[] { 2 }, Targeting.ResolveTargets(State(caster, live, dead), caster, def, Affinity.Hostile).Select(a => a.Id));
    }

    [Fact]
    public void ResolveTargets_line_skips_a_dead_enemy_ahead()
    {
        var caster = A(1, ActorKind.Player, 0);
        var live = A(2, ActorKind.Enemy, 100);
        var dead = A(3, ActorKind.Enemy, 150);
        Kill(dead);
        var def = Ability(new AbilityShape(ShapeKind.Line, Radius: 50, Length: 300), new[] { Dmg });
        Assert.Equal(new[] { 2 }, Targeting.ResolveTargets(State(caster, live, dead), caster, def, Affinity.Hostile).Select(a => a.Id));
    }

    [Fact]
    public void ResolveTargets_target_shape_resolves_to_nothing_when_the_target_is_dead()
    {
        var caster = A(1, ActorKind.Player, 0);
        var dead = A(2, ActorKind.Enemy, 100);
        Kill(dead);
        caster.TargetId = 2;
        Assert.Empty(Targeting.ResolveTargets(State(caster, dead), caster, Ability(new AbilityShape(ShapeKind.Target), new[] { Dmg }), Affinity.Hostile));
    }

    [Fact]
    public void Revive_restores_alive_and_health_and_damage_applies_again()
    {
        var a = A(2, ActorKind.Player, 0);
        Kill(a);
        Actors.Revive(a, 30);
        Assert.True(a.Alive);
        Assert.Equal(30, a.Health);
        Actors.Revive(a);
        Assert.Equal(100, a.Health);
        var (events, emit) = Collector();
        Damage.ApplyDamage(3, a, 5, 0, 0, NoDefs, emit, 1);
        Assert.Equal(95, a.Health);
        Assert.Single(events);
    }
}

/// <summary>
/// sim/combat/mitigation.ts at the dealDamage level (tests/sim/combat/mitigation-resource.test.ts drives it through
/// casting/combos/rules, which port with lane 2B; the expected numbers here are the same) plus actors.ts.
/// </summary>
public sealed class CombatMitigationTests
{
    private static readonly EffectDef BraceSelf = new("braceSelf", 1, null, new EffectModifiers(DamageTakenBp: -4000));
    private static readonly EffectDef Empowered = new("empowered", 1, null, new EffectModifiers(DamageDoneBp: 5000));
    private static readonly System.Collections.Generic.IReadOnlyDictionary<string, EffectDef> EffectsMap = Defs(BraceSelf, Empowered);

    [Fact]
    public void DealDamage_a_braced_actor_hit_for_70_loses_42()
    {
        var src = Actor(1, ActorKind.Enemy, maxHealth: 1000);
        var tgt = Actor(2, ActorKind.Player, maxHealth: 1000);
        Effects.ApplyEffectInstance(tgt, BraceSelf, 2, 100, 1, 0);
        var r = Mitigation.DealDamage(0, src, tgt, 70, EffectsMap, _ => { }, src.Id);
        Assert.Equal(42, r.ModifiedAmount);
        Assert.Equal(958, tgt.Health);
    }

    [Fact]
    public void DealDamage_the_attackers_damageDoneBp_scales_the_hit_70_times_1_5_is_105()
    {
        var src = Actor(1, ActorKind.Player, maxHealth: 1000);
        var tgt = Actor(2, ActorKind.Enemy, maxHealth: 1000);
        Effects.ApplyEffectInstance(src, Empowered, 1, 100, 1, 0);
        Assert.Equal(105, Mitigation.DealDamage(0, src, tgt, 70, EffectsMap, _ => { }, src.Id).ModifiedAmount);
    }

    /// <summary>mitigation.ts:33: a removed source (a DoT whose caster is gone) contributes 0; the target's mods still apply.</summary>
    [Fact]
    public void DealDamage_with_no_source_uses_0_source_bp_and_still_applies_the_targets_mods()
    {
        var tgt = Actor(2, ActorKind.Player, maxHealth: 1000);
        Effects.ApplyEffectInstance(tgt, BraceSelf, 2, 100, 1, 0);
        var (events, emit) = Collector();
        var r = Mitigation.DealDamage(4, null, tgt, 70, EffectsMap, emit, 77);
        Assert.Equal(42, r.ModifiedAmount);
        Assert.Equal(77, Assert.Single(events).Get("sourceId") is JsInt s ? s.Value : -1);
    }

    [Fact]
    public void DealDamage_both_sides_compose_source_then_target()
    {
        var src = Actor(1, ActorKind.Player, maxHealth: 1000);
        var tgt = Actor(2, ActorKind.Enemy, maxHealth: 1000);
        Effects.ApplyEffectInstance(src, Empowered, 1, 100, 1, 0);
        Effects.ApplyEffectInstance(tgt, BraceSelf, 2, 100, 1, 0);
        Assert.Equal(63, Mitigation.DealDamage(0, src, tgt, 70, EffectsMap, _ => { }, 1).ModifiedAmount); // 105 * 0.6
    }

    [Fact]
    public void CreditResource_adds_delta_clamped_to_0_and_maxResource()
    {
        var a = Actor(1, ActorKind.Player, maxResource: 100);
        a.Resource = 30;
        Mitigation.CreditResource(a, 40);
        Assert.Equal(70, a.Resource);
        Mitigation.CreditResource(a, 400);
        Assert.Equal(100, a.Resource);
        Mitigation.CreditResource(a, -500);
        Assert.Equal(0, a.Resource);
    }

    [Fact]
    public void CreateActor_starts_full_alive_untargeted_and_idle()
    {
        var a = Actors.Create(7, ActorKind.Player, "warden", ActorRole.Tank, new Vec3(1, 2, 3), 1000, 100);
        Assert.Equal((7, ActorKind.Player, "warden", ActorRole.Tank, new Vec3(1, 2, 3), 0), (a.Id, a.Kind, a.ClassId, a.Role, a.Pos, a.Yaw));
        Assert.Equal((1000L, 1000L, 100L, 100L, true), (a.Health, a.MaxHealth, a.Resource, a.MaxResource, a.Alive));
        Assert.Null(a.TargetId);
        Assert.Null(a.CastId);
        Assert.Null(a.CastEndsTick);
        Assert.Null(a.CastStartTick);
        Assert.Equal(0, a.Cooldowns.Count);
        Assert.Equal(0, a.GcdReadyTick);
        Assert.Empty(a.Effects);
        Assert.Empty(a.Threat);
        Assert.Null(a.SpeedMmPerTick);
        Assert.False(Actors.IsCasting(a));
        a.CastId = "x";
        Assert.True(Actors.IsCasting(a));
        Assert.Equal(512, Actors.Create(8, ActorKind.Enemy, "e", ActorRole.None, Vec3.Zero, 1, 0, 512).Yaw);
    }
}
