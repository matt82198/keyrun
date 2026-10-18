using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Xunit;
using static Keyrun.Sim.Tests.CombatCastTestKit;
using static Keyrun.Sim.Tests.CombatTestKit;

namespace Keyrun.Sim.Tests;

/// <summary>Port of tests/sim/combat/rules.test.ts and the rules-driven cases of dash.test.ts (names kept).</summary>
public sealed class CombatRulesTests
{
    private static readonly AbilityDef Poke = Def("poke", new AbilityShape(ShapeKind.Target),
        new EffectOp[] { new ApplyEffectOp("dot", 40, 1) }, gcdTicks: 15, interruptible: true);

    private static readonly EffectDef Dot = new("dot", 1, "magic", new EffectModifiers(), new EffectPeriodic(20, new DamageOp(10, "nature")));

    private static RulesConfig BaseCfg() => Config(AbilityMap(Poke), Defs(Dot), boundsHalf: 1000);

    [Fact]
    public void Movement_command_moves_the_actor_and_does_not_itself_advance_the_tick()
    {
        var a = Make(1, ActorKind.Player, role: ActorRole.Damage, maxHealth: 1);
        var state = At(0, a);
        Rules.Make(BaseCfg())(state, Cmds(new MoveCommand(0, 1, new Dir2(1, 0), 0)), _ => { });
        Assert.Equal(300, state.Actors[1].Pos.X);
        Assert.Equal(0, state.Tick);
    }

    [Fact]
    public void Instant_cast_command_completes_within_the_same_step_and_applies_its_effect()
    {
        var caster = Make(1, ActorKind.Player, role: ActorRole.Damage);
        var target = Make(2, ActorKind.Enemy);
        var state = At(0, caster, target);
        var (events, emit) = Collector();
        Rules.Make(BaseCfg())(state, Cmds(Cast(1, "poke", 2)), emit);
        Assert.Single(target.Effects);
        Assert.Contains(events, e => e.Type == "cast_complete");
    }

    [Fact]
    public void Periodic_effects_tick_a_dot_for_its_configured_damage_every_everyTicks_and_expire_it_after_duration()
    {
        var target = Make(2, ActorKind.Enemy);
        var state = At(0, target);
        target.Effects.Add(new EffectInstance { EffectId = "dot", SourceId = 1, Stacks = 1, EndsTick = 45, NextPeriodicTick = 20 });
        var rules = Rules.Make(BaseCfg());
        for (var i = 0; i < 21; i++)
        {
            Tick(state, rules, Cmds(), _ => { });
        }
        Assert.Equal(21, state.Tick);
        Assert.Equal(90, target.Health);
        Assert.Equal(40, target.Effects[0].NextPeriodicTick);
    }

    [Fact]
    public void Collision_and_bounds_push_overlapping_actors_apart_and_clamp_positions_to_arena_bounds()
    {
        var a = Make(1, ActorKind.Player, role: ActorRole.Damage, x: -990, maxHealth: 1);
        var b = Make(2, ActorKind.Player, role: ActorRole.Damage, x: -1050, maxHealth: 1);
        var state = At(0, a, b);
        Rules.Make(BaseCfg())(state, Cmds(), _ => { });
        Assert.InRange(a.Pos.X, -1000, 1000);
        Assert.True(b.Pos.X >= -1000);
        Assert.NotEqual(-990, a.Pos.X);
    }

    // ---- dash.test.ts (the rules-driven cases; validation and sort are 1C's CommandTests) ----

    private static RulesConfig DashCfg() =>
        Config(AbilityMap(), Defs(new EffectDef("root", 1, "magic", new EffectModifiers(MovementBp: -10000))), boundsHalf: 20000);

    [Fact]
    public void Dash_moves_exactly_DASH_DISTANCE_MM_along_dir_in_one_tick_and_sets_facing()
    {
        var state = At(0, Make(1, ActorKind.Player, role: ActorRole.Damage));
        Rules.Make(DashCfg())(state, Cmds(new DashCommand(0, 1, new Dir2(600, 800), 100)), _ => { });
        Assert.Equal(new Vec3(Movement.DashDistanceMm * 3 / 5, 0, Movement.DashDistanceMm * 4 / 5), state.Actors[1].Pos);
        Assert.Equal(100, state.Actors[1].Yaw);
        Assert.True(Movement.DashDistanceMm > 300);
    }

    [Fact]
    public void Dash_survives_steps_validate_and_sort_and_lands_after_the_same_ticks_move()
    {
        var state = At(0, Make(1, ActorKind.Player, role: ActorRole.Damage));
        var move = new MoveCommand(0, 1, new Dir2(0, 1000), 0);
        var dash = new DashCommand(0, 1, new Dir2(1000, 0), 256);
        var next = Core.Tick.Step(state, Cmds(dash, move), Rules.Make(DashCfg())).State;
        Assert.Equal(new Vec3(Movement.DashDistanceMm, 0, 300), next.Actors[1].Pos);
        Assert.Equal(256, next.Actors[1].Yaw);
    }

    [Fact]
    public void Dash_is_stopped_by_a_root_and_clamped_by_arena_bounds()
    {
        var rooted = Make(1, ActorKind.Player, role: ActorRole.Damage);
        rooted.Effects.Add(new EffectInstance { EffectId = "root", SourceId = 1, Stacks = 1, EndsTick = 100, NextPeriodicTick = null });
        var s1 = At(0, rooted);
        Rules.Make(DashCfg())(s1, Cmds(new DashCommand(0, 1, new Dir2(1000, 0), 256)), _ => { });
        Assert.Equal(Vec3.Zero, s1.Actors[1].Pos);

        var edge = Make(1, ActorKind.Player, role: ActorRole.Damage, x: 19000);
        var s2 = At(0, edge);
        Rules.Make(DashCfg())(s2, Cmds(new DashCommand(0, 1, new Dir2(1000, 0), 256)), _ => { });
        Assert.Equal(20000, s2.Actors[1].Pos.X);
    }

    // ---- rules.ts DEATH-CAST EXCEPTION ----

    [Fact]
    public void A_dead_actor_still_casts_but_every_other_command_kind_is_dropped()
    {
        var bloom = Def("bloom", new AbilityShape(ShapeKind.Self), new EffectOp[] { new WindowOp("seedbloom", 40, false, "boss") }, range: 0);
        var seed = Make(1, ActorKind.Enemy, "seed");
        seed.Alive = false;
        seed.Health = 0;
        var state = At(0, seed);
        var (events, emit) = Collector();
        Rules.Make(Config(AbilityMap(bloom)))(state, Cmds(new MoveCommand(0, 1, new Dir2(1000, 0), 9), Cast(1, "bloom")), emit);
        Assert.Equal(Vec3.Zero, seed.Pos);
        Assert.Equal(0, seed.Yaw);
        Assert.True(state.FloorWindows.ContainsKey("seedbloom"));
        Assert.Equal(new[] { "cast_complete", "window_opened" }, events.Select(e => e.Type));
    }
}

/// <summary>
/// Port of tests/sim/combat/rules-affinity-per-run.test.ts (names kept). C# has no affinity registry: the effect
/// table travels in <see cref="RulesConfig"/>, so the TS leak cannot exist; these cases pin that. The two "public
/// path" cases rebuild the rules every tick, as stepRun does; createRun/stepRun itself is lane 2D, so the runs are
/// built directly (party 1, 2 and enemy 100 at the origin) and the loadout is its patched ability.
/// </summary>
public sealed class CombatRulesAffinityPerRunTests
{
    private const int Player = 1;
    private const int Partner = 2;
    private const int Enemy = 100;

    private static AbilityDef CircleApply(string effectId) => Def("pulse", new AbilityShape(ShapeKind.Circle, Radius: 5000),
        new EffectOp[] { new ApplyEffectOp(effectId, 100, 1) }, range: 100000);

    private static EffectDef MarkDef(long damageTakenBp, string id = "mark") => new(id, 1, null, new EffectModifiers(DamageTakenBp: damageTakenBp));

    private static RulesConfig Content(IReadOnlyDictionary<string, EffectDef> effects, AbilityDef? pulse = null) =>
        Config(AbilityMap(pulse ?? CircleApply("mark")), effects, boundsHalf: 20000, actorRadiusMm: 400, lockoutTicks: 40);

    private static readonly RulesConfig Buff = Content(Defs(MarkDef(-1000)));
    private static readonly RulesConfig Curse = Content(Defs(MarkDef(1000)));
    private static readonly IReadOnlyDictionary<string, EffectDef> MixedEffects = Defs(MarkDef(1000), MarkDef(-1000, "ward"));

    private static SimState NewRun() => At(0,
        Make(Player, ActorKind.Player, "alpha", ActorRole.Damage, maxHealth: 200, maxResource: 100),
        Make(Partner, ActorKind.Player, "beta", ActorRole.Damage, maxHealth: 200, maxResource: 100),
        Make(Enemy, ActorKind.Enemy, "boss", maxHealth: 1000000));

    private static IReadOnlyList<Command> CmdsAt(int tick) =>
        tick == 10 || tick == 20 ? new Command[] { new CastCommand(tick, Player, "pulse") } : new Command[0];

    private static int[] Holding(SimState s, string effectId) =>
        s.Actors.Values.Where(a => a.Effects.Any(e => e.EffectId == effectId)).Select(a => a.Id).ToArray();

    private static string Fingerprint(SimState s)
    {
        var sb = new StringBuilder(Projection.Line(s, 0, 0));
        foreach (var a in s.Actors.Values)
        {
            foreach (var e in a.Effects)
            {
                sb.Append('|').Append(a.Id).Append(':').Append(e.EffectId).Append(',').Append(e.EndsTick);
            }
        }
        return sb.ToString();
    }

    private static SimState[] InterleaveRebuilt(RulesConfig[] cfgs, int ticks)
    {
        var states = cfgs.Select(_ => NewRun()).ToArray();
        for (var t = 0; t < ticks; t++)
        {
            for (var i = 0; i < states.Length; i++)
            {
                states[i] = Core.Tick.Step(states[i], CmdsAt(states[i].Tick), Rules.Make(cfgs[i])).State;
            }
        }
        return states;
    }

    private static SimState[] InterleaveHeld(RulesConfig[] cfgs, int ticks)
    {
        var rules = cfgs.Select(Rules.Make).ToArray();
        var states = cfgs.Select(_ => NewRun()).ToArray();
        for (var t = 0; t < ticks; t++)
        {
            for (var i = 0; i < states.Length; i++)
            {
                states[i] = Core.Tick.Step(states[i], CmdsAt(states[i].Tick), rules[i]).State;
            }
        }
        return states;
    }

    [Fact]
    public void Public_path_two_sequentially_created_runs_with_opposite_mark_polarity_interleaved()
    {
        var both = InterleaveRebuilt(new[] { Buff, Curse }, 30);
        Assert.Equal(new[] { Player, Partner }, Holding(both[0], "mark"));
        Assert.Equal(new[] { Enemy }, Holding(both[1], "mark"));
        Assert.Equal(Fingerprint(InterleaveRebuilt(new[] { Buff }, 30)[0]), Fingerprint(both[0]));
        Assert.Equal(Fingerprint(InterleaveRebuilt(new[] { Curse }, 30)[0]), Fingerprint(both[1]));
    }

    [Fact]
    public void Public_path_a_loadout_patched_run_and_a_plain_run_over_the_same_content_interleaved()
    {
        var both = InterleaveRebuilt(new[] { Content(MixedEffects, CircleApply("ward")), Content(MixedEffects) }, 30);
        Assert.Equal(new[] { Player, Partner }, Holding(both[0], "ward"));
        Assert.Empty(Holding(both[0], "mark"));
        Assert.Equal(new[] { Enemy }, Holding(both[1], "mark"));
        Assert.Empty(Holding(both[1], "ward"));
    }

    [Fact]
    public void Held_closures_run_As_StepRules_keeps_As_effects_after_run_Bs_makeRules_A_friendly_B_hostile()
    {
        var both = InterleaveHeld(new[] { Buff, Curse }, 30);
        Assert.Equal(new[] { Player, Partner }, Holding(both[0], "mark"));
        Assert.Equal(new[] { Enemy }, Holding(both[1], "mark"));
    }

    [Fact]
    public void Held_closures_order_reversed_A_hostile_B_friendly_so_neither_creation_order_hides_a_leak()
    {
        var both = InterleaveHeld(new[] { Curse, Buff }, 30);
        Assert.Equal(new[] { Enemy }, Holding(both[0], "mark"));
        Assert.Equal(new[] { Player, Partner }, Holding(both[1], "mark"));
    }

    [Fact]
    public void Held_closures_interleaved_match_each_runs_solo_held_replay_hash_for_hash()
    {
        var held = InterleaveHeld(new[] { Buff, Curse }, 30);
        Assert.Equal(Fingerprint(InterleaveHeld(new[] { Buff }, 30)[0]), Fingerprint(held[0]));
        Assert.Equal(Fingerprint(InterleaveHeld(new[] { Curse }, 30)[0]), Fingerprint(held[1]));
    }
}

/// <summary>Affinity.cs (sim/combat/affinity.ts abilityAffinity / matchesAffinity).</summary>
public sealed class CombatAffinityTests
{
    private static AbilityDef With(params EffectOp[] ops) => Def("a", new AbilityShape(ShapeKind.Circle, Radius: 1), ops);

    private static readonly IReadOnlyDictionary<string, EffectDef> Fx = Defs(
        new EffectDef("braced", 1, null, new EffectModifiers(DamageTakenBp: -1500)),
        new EffectDef("burning", 1, null, new EffectModifiers(), new EffectPeriodic(20, new DamageOp(5, "fire"))));

    [Fact]
    public void Damage_and_interrupt_signal_hostile_heal_signals_friendly()
    {
        Assert.Equal(Affinity.Hostile, Affinities.Of(With(new DamageOp(1, "x")), Fx));
        Assert.Equal(Affinity.Hostile, Affinities.Of(With(new InterruptOp()), Fx));
        Assert.Equal(Affinity.Friendly, Affinities.Of(With(new HealOp(1)), Fx));
    }

    [Fact]
    public void ApplyEffect_defers_to_the_effects_polarity_and_an_unknown_effect_gives_no_signal()
    {
        Assert.Equal(Affinity.Friendly, Affinities.Of(With(new ApplyEffectOp("braced", 10, 1)), Fx));
        Assert.Equal(Affinity.Hostile, Affinities.Of(With(new ApplyEffectOp("burning", 10, 1)), Fx));
        Assert.Equal(Affinity.Hostile, Affinities.Of(With(new ApplyEffectOp("braced", 10, 1)), NoDefs));
    }

    [Fact]
    public void Mixed_or_no_signal_resolves_hostile_fail_closed()
    {
        Assert.Equal(Affinity.Hostile, Affinities.Of(With(new HealOp(1), new DamageOp(1, "x")), Fx));
        Assert.Equal(Affinity.Hostile, Affinities.Of(With(new WindowOp("w", 1, false, "c"), new PayoffOp(), new ResourceOp(1), new ThreatOp(1)), Fx));
    }

    [Fact]
    public void Matches_keeps_the_other_kind_for_hostile_and_the_own_kind_for_friendly()
    {
        Assert.True(Affinities.Matches(Affinity.Hostile, ActorKind.Player, ActorKind.Enemy));
        Assert.False(Affinities.Matches(Affinity.Hostile, ActorKind.Player, ActorKind.Player));
        Assert.True(Affinities.Matches(Affinity.Friendly, ActorKind.Enemy, ActorKind.Enemy));
        Assert.False(Affinities.Matches(Affinity.Friendly, ActorKind.Enemy, ActorKind.Player));
    }
}

/// <summary>ClauseHooks.cs: the Clause seam (sim/combat/clause-hooks.ts) as an explicit per-step context.</summary>
public sealed class CombatClauseHooksTests
{
    /// <summary>A Hush-and-Pact stand-in: silence on [100, 110), solo hits -50%, combo hits +100%.</summary>
    private sealed class TestClause : ICombatClause
    {
        public bool IsSilenced(int tick) => tick >= 100 && tick < 110;

        public long WeightDamage(long rawAmount, bool wasCombo) =>
            IntMath.TruncDiv(rawAmount * (Effects.BpDenom + (wasCombo ? 10000 : -5000)), Effects.BpDenom);
    }

    private static readonly AbilityDef Hit = Def("hit", new AbilityShape(ShapeKind.Target), new EffectOp[] { new DamageOp(100, "physical") });
    private static readonly AbilityDef Mark = Def("mark", new AbilityShape(ShapeKind.Target), new EffectOp[] { new WindowOp("m", 60, true, "warden") });
    private static readonly AbilityDef Pay = Def("pay", new AbilityShape(ShapeKind.Target), new EffectOp[] { new PayoffOp() });
    private static readonly ComboTable Burst = Table(("warden", "cinder", new ComboDef("shatter", new EffectOp[] { new DamageOp(100, "physical") })));

    [Fact]
    public void StartCast_refuses_with_silenced_inside_a_silence_pulse_and_casts_outside_it()
    {
        var caster = Make(1, ActorKind.Player, maxResource: 100);
        var target = Make(2, ActorKind.Enemy);
        var state = At(100, caster, target);
        var hooks = new ClauseHooks(new TestClause());
        Assert.Equal("silenced", Casting.StartCast(state, caster, Hit, 2, null, 105, hooks));
        Assert.Null(caster.CastId);
        Assert.Null(Casting.StartCast(state, caster, Hit, 2, null, 110, hooks));
        Assert.Null(Casting.StartCast(state, Make(3, ActorKind.Player), Hit, 2, null, 105));
    }

    [Fact]
    public void Pact_weights_a_solo_hit_with_the_solo_bp_and_a_combo_hit_with_the_combo_bp()
    {
        var warden = Make(1, ActorKind.Player, "warden", ActorRole.Tank);
        var cinder = Make(2, ActorKind.Player, "cinder", ActorRole.Damage);
        var boss = Make(3, ActorKind.Enemy, "boss", maxHealth: 10000);
        var state = At(0, warden, cinder, boss);
        var rules = Rules.Make(Config(AbilityMap(Hit, Mark, Pay), combos: Burst, clause: new TestClause()));
        var (events, emit) = Collector();
        Tick(state, rules, Cmds(Cast(2, "hit", 3)), emit);
        Assert.Equal(50, Int(events.Single(e => e.Type == "damage"), "modifiedAmount"));
        Tick(state, rules, Cmds(Cast(1, "mark", 3)), emit);
        Tick(state, rules, Cmds(Cast(2, "pay", 3)), emit);
        var comboHit = events.Where(e => e.Type == "damage").Last();
        Assert.Equal(200, Int(comboHit, "modifiedAmount"));
        Assert.Equal(10000 - 50 - 200, boss.Health);
    }

    [Fact]
    public void No_clause_means_no_weighting_and_RunAsCombo_restores_the_flag_even_on_a_throw()
    {
        Assert.Null(new ClauseHooks(null).Weighting);
        var hooks = new ClauseHooks(new TestClause());
        Assert.False(hooks.IsComboActive);
        Assert.Throws<InvalidOperationException>(() => hooks.RunAsCombo(() => throw new InvalidOperationException("boom")));
        Assert.False(hooks.IsComboActive);
        long seen = 0;
        hooks.RunAsCombo(() => seen = hooks.Weighting!(10));
        Assert.Equal(20, seen);
        Assert.Equal(5, hooks.Weighting!(10));
    }
}

/// <summary>
/// FACET RUNTIME (BIBLE section 7, ENGINE-PLAN section 5 row 2B): every facet op kind content/data/facets uses runs
/// in the sim, one test per kind asserting its state change, and an op the sim does not run fails closed. The ops
/// are written the way content/data/facets/*.json writes them after the patch layer (content/apply.ts) has merged
/// them into the base ability.
/// </summary>
public sealed class CombatFacetRuntimeTests
{
    private static readonly ComboTable Burst = Table(
        ("warden", "cinder", new ComboDef("shatter", new EffectOp[] { new DamageOp(40, "physical") })),
        ("cinder", "warden", new ComboDef("forge", new EffectOp[] { new ResourceOp(40) })));

    private static AbilityDef Facet(AbilityDef b, params EffectOp[] append) => b with { Effects = b.Effects.Concat(append).ToList() };

    private static (SimState State, ActorState Warden, ActorState Cinder, ActorState Boss) Party()
    {
        var warden = Make(1, ActorKind.Player, "warden", ActorRole.Tank, maxResource: 100);
        var cinder = Make(2, ActorKind.Player, "cinder", ActorRole.Damage, maxResource: 100);
        var boss = Make(3, ActorKind.Enemy, "boss", x: 1000, maxHealth: 1000);
        warden.Resource = 0;
        cinder.Resource = 0;
        return (At(0, warden, cinder, boss), warden, cinder, boss);
    }

    [Fact]
    public void Window_effectsAppend_with_setupClass_opens_a_Window_another_class_can_close()
    {
        // warden.charm.opportunist: Taunt gains {op:"window", windowId:"sundered", durationTicks:60, onTarget:true}, setupClass warden.
        var taunt = Facet(Def("warden.taunt", new AbilityShape(ShapeKind.Target), new EffectOp[] { new ThreatOp(MultiplierBp: 30000) }),
            new WindowOp("sundered", 60, true, "warden"));
        var detonate = Def("cinder.detonate", new AbilityShape(ShapeKind.Target), new EffectOp[] { new PayoffOp() });
        var (state, _, _, boss) = Party();
        var rules = Rules.Make(Config(AbilityMap(taunt, detonate), combos: Burst));
        var (events, emit) = Collector();
        Tick(state, rules, Cmds(Cast(1, "warden.taunt", 3)), emit);
        Assert.Contains(boss.Effects, e => e.EffectId == "window:sundered:warden" && e.EndsTick == 60);
        Tick(state, rules, Cmds(Cast(2, "cinder.detonate", 3)), emit);
        Assert.Equal("shatter", Str(events.Single(e => e.Type == "combo"), "comboId"));
        Assert.Equal(960, boss.Health);
    }

    [Fact]
    public void Combo_comboTable_makes_a_neutral_ability_a_Payoff_that_closes_through_the_named_table()
    {
        // cinder.charm.overloadedBolt / warden.charm.releaseTheGuard: {op:"combo", comboTable:"combos"} adds a Payoff.
        var flare = Def("cinder.flare", new AbilityShape(ShapeKind.Target), new EffectOp[] { new WindowOp("kindled", 60, true, "cinder") });
        var plain = Def("warden.vigil", new AbilityShape(ShapeKind.Target), new EffectOp[] { new ThreatOp(1) });
        var (state, warden, _, boss) = Party();
        var (events, emit) = Collector();
        Tick(state, Rules.Make(Config(AbilityMap(flare, plain), combos: Burst)), Cmds(Cast(2, "cinder.flare", 3)), emit);
        Tick(state, Rules.Make(Config(AbilityMap(flare, plain), combos: Burst)), Cmds(Cast(1, "warden.vigil", 3)), emit);
        Assert.DoesNotContain(events, e => e.Type == "combo");
        Assert.True(boss.Effects.Exists(e => e.EffectId == "window:kindled:cinder"));

        var vigilantsMark = Facet(plain, new ComboOp("combos"));
        var rules = Rules.Make(Config(AbilityMap(flare, vigilantsMark), combos: Burst));
        Tick(state, rules, Cmds(Cast(1, "warden.vigil", 3)), emit);
        Assert.Equal("forge", Str(events.Single(e => e.Type == "combo"), "comboId"));
        Assert.Equal(40, warden.Resource);
        Assert.False(boss.Effects.Exists(e => e.EffectId.StartsWith("window:", StringComparison.Ordinal)));
    }

    [Fact]
    public void Resource_resource_credits_the_casters_pool_once_per_cast()
    {
        // cinder.ring.heatSink: emberBolt gains {op:"resource", resource:"heat", delta:8}; actors hold one pool.
        var emberBolt = Facet(Def("cinder.emberBolt", new AbilityShape(ShapeKind.Target), new EffectOp[] { new DamageOp(10, "fire") }), new ResourceOp(8));
        var (state, _, cinder, boss) = Party();
        Tick(state, Rules.Make(Config(AbilityMap(emberBolt))), Cmds(Cast(2, "cinder.emberBolt", 3)), _ => { });
        Assert.Equal(8, cinder.Resource);
        Assert.Equal(990, boss.Health);
    }

    [Fact]
    public void Shape_width_becomes_a_line_of_that_full_width()
    {
        // warden.weapon.twinStrike: {kind:"line", length:6000, width:1200} -> half-width 600 either side.
        var twin = Def("warden.reckoning", Abilities.LineShape(6000, 1200), new EffectOp[] { new DamageOp(10, "physical") });
        var caster = Make(1, ActorKind.Player, "warden", ActorRole.Tank);
        var inside = Make(2, ActorKind.Enemy, x: 3000, z: 600);
        var outside = Make(3, ActorKind.Enemy, x: 3000, z: -700);
        var state = At(0, caster, inside, outside);
        Tick(state, Rules.Make(Config(AbilityMap(twin))), Cmds(Cast(1, "warden.reckoning")), _ => { });
        Assert.Equal(90, inside.Health);
        Assert.Equal(100, outside.Health);
    }

    private sealed record ExplodeOp : EffectOp;

    [Fact]
    public void An_unknown_facet_op_fails_closed_when_the_rules_are_built()
    {
        var bad = Def("x.bad", new AbilityShape(ShapeKind.Self), new EffectOp[] { new HealOp(1), new ExplodeOp() });
        var ex = Assert.Throws<ArgumentException>(() => Rules.Make(Config(AbilityMap(bad))));
        Assert.Contains("x.bad: unknown effect op ExplodeOp", ex.Message, StringComparison.Ordinal);
        var badTable = Def("x.table", new AbilityShape(ShapeKind.Self), new EffectOp[] { new ComboOp("nope") });
        Assert.Throws<ArgumentException>(() => Rules.Make(Config(AbilityMap(badTable))));
        var badPeriodic = new EffectDef("weird", 1, null, new EffectModifiers(), new EffectPeriodic(5, new ExplodeOp()));
        Assert.Throws<ArgumentException>(() => Rules.Make(Config(AbilityMap(), Defs(badPeriodic))));
    }
}
