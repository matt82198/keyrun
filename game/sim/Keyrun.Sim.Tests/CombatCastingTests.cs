using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Xunit;
using static Keyrun.Sim.Tests.CombatCastTestKit;
using static Keyrun.Sim.Tests.CombatTestKit;

namespace Keyrun.Sim.Tests;

/// <summary>Port of tests/sim/combat/abilities.test.ts (names kept).</summary>
public sealed class CombatAbilitiesTests
{
    private static AbilityDef BaseDef(
        string id = "fireball",
        int castTicks = 30,
        long charges = 1,
        AbilityShape? shape = null,
        IReadOnlyList<EffectOp>? effects = null) =>
        Def(id, shape ?? new AbilityShape(ShapeKind.Target), effects ?? new EffectOp[] { new DamageOp(100, "fire") },
            castTicks: castTicks, gcdTicks: 15, charges: charges, costResource: "mana", costAmount: 20, range: 30000, interruptible: true);

    [Fact]
    public void ValidateAbilityDef_accepts_a_well_formed_target_ability() => Assert.Empty(Abilities.ValidateAbilityDef(BaseDef()));

    [Fact]
    public void ValidateAbilityDef_rejects_a_missing_id() => Assert.Contains("id is required", Abilities.ValidateAbilityDef(BaseDef(id: "")));

    [Fact]
    public void ValidateAbilityDef_rejects_negative_castTicks() =>
        Assert.Contains("castTicks must be an integer >= 0", Abilities.ValidateAbilityDef(BaseDef(castTicks: -1)));

    [Fact]
    public void ValidateAbilityDef_rejects_zero_charges() =>
        Assert.Contains("charges must be an integer >= 1", Abilities.ValidateAbilityDef(BaseDef(charges: 0)));

    [Fact]
    public void ValidateAbilityDef_requires_radius_and_angle1024_for_a_cone_shape() =>
        Assert.Contains("cone shape requires radius and angle1024", Abilities.ValidateAbilityDef(BaseDef(shape: new AbilityShape(ShapeKind.Cone))));

    [Fact]
    public void ValidateAbilityDef_requires_radius_for_a_circle_shape() =>
        Assert.Contains("circle shape requires radius", Abilities.ValidateAbilityDef(BaseDef(shape: new AbilityShape(ShapeKind.Circle))));

    [Fact]
    public void ValidateAbilityDef_requires_length_and_radius_for_a_line_shape() =>
        Assert.Contains("line shape requires length and radius (half-width)", Abilities.ValidateAbilityDef(BaseDef(shape: new AbilityShape(ShapeKind.Line))));

    [Fact]
    public void ValidateAbilityDef_rejects_an_ability_with_no_effects() =>
        Assert.Contains("effects must not be empty", Abilities.ValidateAbilityDef(BaseDef(effects: new EffectOp[0])));

    [Fact]
    public void ValidateAbilityDef_rejects_applyEffect_with_zero_stacks() =>
        Assert.Contains("applyEffect burn stacks must be an integer >= 1", Abilities.ValidateAbilityDef(BaseDef(effects: new EffectOp[] { new ApplyEffectOp("burn", 40, 0) })));

    [Fact]
    public void AbilitySchool_returns_the_school_of_the_first_damage_effect() => Assert.Equal("fire", Abilities.AbilitySchool(BaseDef()));

    [Fact]
    public void AbilitySchool_defaults_to_physical_when_there_is_no_damage_effect() =>
        Assert.Equal("physical", Abilities.AbilitySchool(BaseDef(effects: new EffectOp[] { new HealOp(50) })));

    // ---- C#-only: the fail-closed op check (FACET RUNTIME, ENGINE-PLAN section 5 row 2B). ----

    private sealed record BogusOp : EffectOp;

    [Fact]
    public void ValidateAbilityDef_fails_closed_on_an_effect_op_kind_the_sim_does_not_run()
    {
        var errs = Abilities.ValidateAbilityDef(BaseDef(effects: new EffectOp[] { new DamageOp(1, "fire"), new BogusOp() }));
        Assert.Contains("unknown effect op BogusOp", errs);
    }

    [Fact]
    public void ValidateAbilityDef_rejects_a_combo_op_naming_an_unknown_table()
    {
        Assert.Empty(Abilities.ValidateAbilityDef(BaseDef(effects: new EffectOp[] { new ComboOp("combos") })));
        Assert.Contains("combo op comboTable must be \"combos\" or \"fieldCombos\" (got \"bursts\")", Abilities.ValidateAbilityDef(BaseDef(effects: new EffectOp[] { new ComboOp("bursts") })));
    }

    [Fact]
    public void LineShape_turns_a_facet_width_into_the_sim_half_width_radius()
    {
        Assert.Equal(new AbilityShape(ShapeKind.Line, Radius: 600, Length: 25000), Abilities.LineShape(25000, 1200));
        Assert.Throws<System.ArgumentOutOfRangeException>(() => Abilities.LineShape(6000, 0));
    }
}

/// <summary>
/// Port of tests/sim/combat/casting.test.ts, interrupt-scenario.test.ts, targeting-resolve.test.ts's two pipeline
/// cases, dead-actors.test.ts's two casting cases and mitigation-resource.test.ts (names kept; 2A ported the
/// damage/targeting halves of the last three).
/// </summary>
public sealed class CombatCastingTests
{
    private static readonly AbilityDef Fireball = Def("fireball", new AbilityShape(ShapeKind.Target), new EffectOp[] { new DamageOp(100, "fire") },
        castTicks: 30, gcdTicks: 15, costResource: "mana", costAmount: 20, range: 30000, interruptible: true);

    private static readonly AbilityDef Kick = Def("kick", new AbilityShape(ShapeKind.Target), new EffectOp[] { new InterruptOp() },
        castTicks: 0, gcdTicks: 15, cooldownTicks: 40, range: 5000);

    private static CastContext Ctx() => new(AbilityMap(Fireball, Kick), NoDefs, 60);

    private static (ActorState Caster, ActorState Target) Pair(long casterResource = 100, long targetX = 0)
    {
        var caster = Make(1, ActorKind.Player, "mage", ActorRole.Damage, maxResource: casterResource);
        var target = Make(2, ActorKind.Enemy, x: targetX);
        return (caster, target);
    }

    [Fact]
    public void StartCast_fails_when_out_of_range()
    {
        var (caster, target) = Pair(targetX: 40000);
        Assert.Equal("out_of_range", Casting.StartCast(At(0, caster, target), caster, Fireball, 2, null, 0));
    }

    [Fact]
    public void StartCast_fails_when_the_resource_cost_cannot_be_paid()
    {
        var (caster, target) = Pair(casterResource: 10);
        Assert.Equal("insufficient_resource", Casting.StartCast(At(0, caster, target), caster, Fireball, 2, null, 0));
    }

    [Fact]
    public void StartCast_succeeds_spends_resource_and_sets_castEndsTick_now_plus_castTicks()
    {
        var (caster, target) = Pair();
        Assert.Null(Casting.StartCast(At(10, caster, target), caster, Fireball, 2, null, 10));
        Assert.Equal(80, caster.Resource);
        Assert.Equal("fireball", caster.CastId);
        Assert.Equal(10, caster.CastStartTick);
        Assert.Equal(40, caster.CastEndsTick);
    }

    [Fact]
    public void StartCast_fails_while_the_abilitys_school_is_locked_out()
    {
        var (caster, target) = Pair();
        caster.Effects.Add(new EffectInstance { EffectId = "lockout:fire", SourceId = 2, Stacks = 1, EndsTick = 100, NextPeriodicTick = null });
        Assert.Equal("school_locked", Casting.StartCast(At(10, caster, target), caster, Fireball, 2, null, 10));
    }

    [Fact]
    public void CompleteCast_applies_the_abilitys_effects_to_the_casters_target_and_clears_the_cast()
    {
        var (caster, target) = Pair();
        var state = At(40, caster, target);
        Casting.StartCast(state, caster, Fireball, 2, null, 10);
        state.Tick = 40;
        var (events, emit) = Collector();
        Casting.CompleteCast(state, caster, Ctx(), emit);
        Assert.Equal(0, target.Health);
        Assert.Null(caster.CastId);
        Assert.Contains(events, e => e.Type == "cast_complete" && Str(e, "abilityId") == "fireball");
    }

    [Fact]
    public void Stopcast_clears_the_cast_without_applying_any_effects()
    {
        var (caster, target) = Pair();
        var state = At(10, caster, target);
        Casting.StartCast(state, caster, Fireball, 2, null, 10);
        var (events, emit) = Collector();
        Casting.Stopcast(caster, emit, 20);
        Assert.Null(caster.CastId);
        Assert.Equal(100, target.Health);
        Assert.Equal("{\"abilityId\":\"fireball\",\"actorId\":1,\"tick\":20,\"type\":\"cast_stopped\"}", Assert.Single(events).ToCanonicalJson());
    }

    [Fact]
    public void InterruptCast_is_a_no_op_against_a_non_interruptible_ability()
    {
        var (caster, target) = Pair();
        var state = At(10, caster, target);
        var nonInterruptible = Fireball with { Interruptible = false };
        var c = new CastContext(AbilityMap(nonInterruptible), NoDefs, 60);
        Casting.StartCast(state, caster, nonInterruptible, 2, null, 10);
        var (events, emit) = Collector();
        Casting.InterruptCast(state, caster, 2, c, emit);
        Assert.Equal("fireball", caster.CastId);
        Assert.Empty(events);
    }

    [Fact]
    public void InterruptCast_cancels_the_cast_and_applies_a_school_lockout_when_interruptible()
    {
        var (caster, target) = Pair();
        var state = At(10, caster, target);
        Casting.StartCast(state, caster, Fireball, 2, null, 10);
        state.Tick = 25;
        var (events, emit) = Collector();
        Casting.InterruptCast(state, caster, 2, Ctx(), emit);
        Assert.Null(caster.CastId);
        Assert.Contains(caster.Effects, e => e.EffectId == "lockout:fire" && e.EndsTick == 85);
        Assert.Equal(
            "{\"abilityId\":\"fireball\",\"school\":\"fire\",\"sourceId\":2,\"targetId\":1,\"tick\":25,\"type\":\"interrupt\"}",
            Assert.Single(events).ToCanonicalJson());
    }

    // ---- interrupt-scenario.test.ts ----

    private static (SimState State, StepRules Rules, List<SimEvent> Events) Scenario()
    {
        var caster = Make(1, ActorKind.Player, "mage", ActorRole.Damage, maxResource: 100);
        var dummy = Make(2, ActorKind.Enemy, "target-dummy", x: 1000, maxHealth: 500);
        var rogue = Make(3, ActorKind.Player, "rogue", ActorRole.Damage, z: 100, maxResource: 100);
        var state = At(0, caster, dummy, rogue);
        var rules = Rules.Make(Config(AbilityMap(Fireball, Kick), boundsHalf: 100000));
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
    public void Interrupt_strictly_before_completion_landing_at_tick_39_cancels_the_cast_and_locks_the_school()
    {
        var (state, rules, events) = Scenario();
        RunUntil(state, rules, events, new Dictionary<int, Command[]>
        {
            [10] = new Command[] { new CastCommand(10, 1, "fireball", 2) },
            [39] = new Command[] { new CastCommand(39, 3, "kick", 1) },
        }, 41);
        Assert.Null(state.Actors[1].CastId);
        Assert.Equal(500, state.Actors[2].Health);
        Assert.Contains(state.Actors[1].Effects, e => e.EffectId == "lockout:fire");
        Assert.Contains(events, e => e.Type == "interrupt" && e.Tick == 39);
        Assert.DoesNotContain(events, e => e.Type == "cast_complete" && Str(e, "abilityId") == "fireball");
    }

    [Fact]
    public void Interrupt_arriving_exactly_at_completion_landing_at_tick_40_is_too_late_the_cast_completes_and_the_interrupt_is_a_no_op()
    {
        var (state, rules, events) = Scenario();
        RunUntil(state, rules, events, new Dictionary<int, Command[]>
        {
            [10] = new Command[] { new CastCommand(10, 1, "fireball", 2) },
            [40] = new Command[] { new CastCommand(40, 3, "kick", 1) },
        }, 41);
        Assert.Equal(400, state.Actors[2].Health);
        Assert.DoesNotContain(state.Actors[1].Effects, e => e.EffectId.StartsWith("lockout:", System.StringComparison.Ordinal));
        Assert.Contains(events, e => e.Type == "cast_complete" && e.Tick == 40);
        Assert.DoesNotContain(events, e => e.Type == "interrupt");
    }

    // ---- targeting-resolve.test.ts: cast pipeline and window pipeline resolve identical target sets ----

    [Fact]
    public void CompleteCast_and_ApplyWindowsAndCombos_hit_the_same_cone_shaped_targets()
    {
        var caster = Make(1, ActorKind.Player, "warden", ActorRole.Damage, maxResource: 100);
        var near = Make(2, ActorKind.Enemy, x: 100);
        var far = Make(3, ActorKind.Enemy, x: 150);
        var outsideCone = Make(4, ActorKind.Enemy, z: 200);
        var def = Def("test_ability", new AbilityShape(ShapeKind.Cone, Radius: 200, Angle1024: 256), new EffectOp[]
        {
            new DamageOp(10, "physical"),
            new WindowOp("cleave-window", 60, true, "warden"),
        });
        var state = At(0, caster, near, far, outsideCone);
        var expectedIds = Targeting.ResolveTargets(state, caster, def, Affinities.Of(def, NoDefs)).Select(a => a.Id).ToList();
        Assert.Equal(new[] { 2, 3 }, expectedIds);

        caster.CastId = def.Id;
        var (cast, castEmit) = Collector();
        Casting.CompleteCast(state, caster, new CastContext(AbilityMap(def), NoDefs, 0), castEmit);
        Assert.Equal(expectedIds, cast.Where(e => e.Type == "damage").Select(e => (int)Int(e, "targetId")).OrderBy(i => i));

        var (win, winEmit) = Collector();
        Windows.ApplyWindowsAndCombos(state, caster, def, new WindowsContext(NoDefs), winEmit);
        Assert.Equal(expectedIds[0], Int(win.First(e => e.Type == "window_opened"), "targetId"));
    }

    [Fact]
    public void Both_pipelines_agree_the_caster_is_the_sole_target_for_a_self_shaped_ability()
    {
        var caster = Make(1, ActorKind.Player, "warden", ActorRole.Tank, maxResource: 100);
        var def = Def("test_ability", new AbilityShape(ShapeKind.Self), new EffectOp[]
        {
            new HealOp(5),
            new WindowOp("self-window", 60, true, "warden", Self: true),
        });
        var state = At(0, caster);
        caster.Health = 90;
        caster.CastId = def.Id;
        var (cast, castEmit) = Collector();
        Casting.CompleteCast(state, caster, new CastContext(AbilityMap(def), NoDefs, 0), castEmit);
        Assert.Equal(caster.Id, Int(cast.First(e => e.Type == "heal"), "targetId"));

        var (win, winEmit) = Collector();
        Windows.ApplyWindowsAndCombos(state, caster, def, new WindowsContext(NoDefs), winEmit);
        Assert.Equal(caster.Id, Int(win.First(e => e.Type == "window_opened"), "targetId"));
    }

    // ---- dead-actors.test.ts: single-target cast on a dead target ----

    private static void Kill(ActorState a) => Damage.ApplyDamage(1, a, a.Health, 0, 0, NoDefs, _ => { }, 99);

    [Fact]
    public void StartCast_already_refuses_with_invalid_target()
    {
        var caster = Make(1, ActorKind.Player, maxResource: 100);
        var dead = Make(2, ActorKind.Enemy, x: 100, maxResource: 100);
        Kill(dead);
        var d = Def("t", new AbilityShape(ShapeKind.Target), new EffectOp[] { new DamageOp(10, "physical") });
        Assert.Equal("invalid_target", Casting.StartCast(At(0, caster, dead), caster, d, 2, null, 0));
    }

    [Fact]
    public void A_target_that_dies_mid_cast_receives_no_damage_or_heal_events_when_the_cast_completes()
    {
        var caster = Make(1, ActorKind.Player, maxResource: 100);
        var t = Make(2, ActorKind.Enemy, x: 100, maxResource: 100);
        var d = Def("t", new AbilityShape(ShapeKind.Target), new EffectOp[] { new DamageOp(10, "physical") }, castTicks: 10);
        var state = At(0, caster, t);
        Assert.Null(Casting.StartCast(state, caster, d, 2, null, 0));
        Kill(t);
        var (events, emit) = Collector();
        Casting.CompleteCast(state, caster, new CastContext(AbilityMap(d), NoDefs, 0), emit);
        Assert.DoesNotContain(events, e => e.Type == "damage" || e.Type == "death");
        Assert.Equal(0, t.Health);
    }

    // ---- mitigation-resource.test.ts (through the cast, combo and rules paths) ----

    private static readonly EffectDef BraceSelf = new("braceSelf", 1, null, new EffectModifiers(DamageTakenBp: -4000));
    private static readonly EffectDef Empowered = new("empowered", 1, null, new EffectModifiers(DamageDoneBp: 5000));
    private static readonly EffectDef Burn = new("burn", 1, null, new EffectModifiers(), new EffectPeriodic(20, new DamageOp(70, "fire")));
    private static readonly IReadOnlyDictionary<string, EffectDef> MitigationEffects = Defs(BraceSelf, Empowered, Burn);

    private static AbilityDef Mit(string id, AbilityShape shape, params EffectOp[] ops) =>
        Def(id, shape, ops, costResource: "none", range: 30000);

    private static readonly AbilityDef Slam = Mit("slam", new AbilityShape(ShapeKind.Target), new DamageOp(70, "physical"));
    private static readonly AbilityDef Strike = Mit("strike", new AbilityShape(ShapeKind.Target), new DamageOp(10, "physical"), new ResourceOp(15));
    private static readonly AbilityDef Sweep = Mit("sweep", new AbilityShape(ShapeKind.Circle, Radius: 5000), new DamageOp(10, "physical"), new ResourceOp(15));

    private static ActorState Mk(int id, ActorKind kind)
    {
        var a = Make(id, kind, kind == ActorKind.Player ? "warden" : "mob", kind == ActorKind.Player ? ActorRole.Tank : ActorRole.None, maxHealth: 1000, maxResource: 100);
        a.Resource = 0;
        return a;
    }

    private static void CastNow(SimState state, ActorState caster, AbilityDef def, int? targetId)
    {
        Assert.Null(Casting.StartCast(state, caster, def, targetId, null, state.Tick));
        Casting.CompleteCast(state, caster, new CastContext(AbilityMap(Slam, Strike, Sweep), MitigationEffects, 60), _ => { });
    }

    private static void Brace(ActorState a, int sourceId) =>
        a.Effects.Add(new EffectInstance { EffectId = "braceSelf", SourceId = sourceId, Stacks = 1, EndsTick = 1000, NextPeriodicTick = null });

    private static RulesConfig MitigationRules(AbilityDef def) => Config(AbilityMap(def), MitigationEffects, boundsHalf: 100000, actorRadiusMm: 0);

    [Fact]
    public void Casting_a_Braced_actor_hit_for_70_loses_42()
    {
        var enemy = Mk(1, ActorKind.Enemy);
        var tank = Mk(2, ActorKind.Player);
        Brace(tank, 2);
        CastNow(State(enemy, tank), enemy, Slam, 2);
        Assert.Equal(42, 1000 - tank.Health);
    }

    [Fact]
    public void Casting_the_attackers_damageDoneBp_scales_the_hit_70_times_1_5_is_105()
    {
        var enemy = Mk(1, ActorKind.Enemy);
        var tank = Mk(2, ActorKind.Player);
        enemy.Effects.Add(new EffectInstance { EffectId = "empowered", SourceId = 1, Stacks = 1, EndsTick = 1000, NextPeriodicTick = null });
        CastNow(State(enemy, tank), enemy, Slam, 2);
        Assert.Equal(105, 1000 - tank.Health);
    }

    [Fact]
    public void Combos_a_combos_damage_op_respects_the_targets_Brace()
    {
        var payoff = Mk(1, ActorKind.Player);
        var target = Mk(2, ActorKind.Enemy);
        Brace(target, 2);
        Combos.ApplyComboEffects(State(payoff, target), payoff, target, new EffectOp[] { new DamageOp(70, "fire") }, MitigationEffects, _ => { });
        Assert.Equal(42, 1000 - target.Health);
    }

    [Fact]
    public void Rules_a_periodic_DoT_tick_respects_the_carriers_Brace()
    {
        var enemy = Mk(1, ActorKind.Enemy);
        var tank = Mk(2, ActorKind.Player);
        Brace(tank, 2);
        tank.Effects.Add(new EffectInstance { EffectId = "burn", SourceId = 1, Stacks = 1, EndsTick = 1000, NextPeriodicTick = 0 });
        Rules.Make(MitigationRules(Slam))(State(enemy, tank), new Command[0], _ => { });
        Assert.Equal(42, 1000 - tank.Health);
    }

    [Fact]
    public void A_Wardens_momentum_builder_on_an_enemy_raises_the_Wardens_momentum_not_the_enemys()
    {
        var warden = Mk(1, ActorKind.Player);
        var enemy = Mk(2, ActorKind.Enemy);
        CastNow(State(warden, enemy), warden, Strike, 2);
        Assert.Equal(15, warden.Resource);
        Assert.Equal(0, enemy.Resource);
    }

    [Fact]
    public void An_AoE_builder_hitting_two_enemies_credits_the_caster_once_per_cast()
    {
        var warden = Mk(1, ActorKind.Player);
        var e1 = Mk(2, ActorKind.Enemy);
        var e2 = Mk(3, ActorKind.Enemy);
        var state = State(warden, e1, e2);
        Rules.Make(MitigationRules(Sweep))(state, Cmds(Cast(1, "sweep")), _ => { });
        Assert.Equal(990, e1.Health);
        Assert.Equal(990, e2.Health);
        Assert.Equal(15, warden.Resource);
        Assert.Equal(0, e1.Resource + e2.Resource);
    }

    [Fact]
    public void Combos_the_Forge_combos_plus_40_credits_the_payoff_actor_not_the_Window_carrier()
    {
        var payoff = Mk(1, ActorKind.Player);
        var carrier = Mk(2, ActorKind.Enemy);
        Combos.ApplyComboEffects(State(payoff, carrier), payoff, carrier, new EffectOp[] { new ResourceOp(40) }, MitigationEffects, _ => { });
        Assert.Equal(40, payoff.Resource);
        Assert.Equal(0, carrier.Resource);
    }
}
