using System.Collections.Generic;
using System.Linq;
using System.Text;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Xunit;
using static Keyrun.Sim.Tests.CombatCastTestKit;
using static Keyrun.Sim.Tests.CombatTestKit;

namespace Keyrun.Sim.Tests;

/// <summary>Port of tests/sim/combat/combos.test.ts (names kept).</summary>
public sealed class CombatCombosTests
{
    private static readonly ComboTable ComboRows = Table(
        ("warden", "cinder", new ComboDef("shatter", new EffectOp[] { new DamageOp(40, "physical") })),
        ("warden", "chanter", new ComboDef("consecrate", new EffectOp[] { new DamageOp(20, "physical"), new ApplyEffectOp("heal_pulse", 120, 1) })),
        ("cinder", "warden", new ComboDef("forge", new EffectOp[] { new ResourceOp(40) })));

    private static readonly EffectDef HealPulse = new("heal_pulse", 1, null, new EffectModifiers());

    [Fact]
    public void LookupCombo_finds_the_combo_for_a_setupClass_payoffClass_pair()
    {
        Assert.Equal("shatter", Combos.LookupCombo(ComboRows, "warden", "cinder")?.Id);
        Assert.Equal("forge", Combos.LookupCombo(ComboRows, "cinder", "warden")?.Id);
    }

    [Fact]
    public void LookupCombo_returns_undefined_for_an_unlisted_pair() => Assert.Null(Combos.LookupCombo(ComboRows, "chanter", "warden"));

    [Fact]
    public void ApplyComboEffects_applies_damage_to_the_target_and_attributes_it_to_the_payoff_actor()
    {
        var source = Make(1, ActorKind.Player, "cinder", ActorRole.Damage);
        var target = Make(2, ActorKind.Enemy, "boss");
        var (events, emit) = Collector();
        Combos.ApplyComboEffects(State(), source, target, ComboRows.Lookup("warden", "cinder")!.Effects, NoDefs, emit);
        Assert.Equal(60, target.Health);
        var dmg = events.First(e => e.Type == "damage");
        Assert.Equal(1, Int(dmg, "sourceId"));
        Assert.Equal(2, Int(dmg, "targetId"));
    }

    [Fact]
    public void ApplyComboEffects_credits_a_Forge_style_combos_resource_delta_to_the_payoff_actor_not_the_Window_carrier()
    {
        var source = Make(1, ActorKind.Player, "warden", ActorRole.Tank, maxResource: 100);
        var target = Make(2, ActorKind.Enemy, "boss", maxResource: 100);
        source.Resource = 0;
        target.Resource = 0;
        Combos.ApplyComboEffects(State(), source, target, ComboRows.Lookup("cinder", "warden")!.Effects, NoDefs, _ => { });
        Assert.Equal(40, source.Resource);
        Assert.Equal(0, target.Resource);
    }

    [Fact]
    public void ApplyComboEffects_applies_a_bundled_applyEffect_alongside_damage_Consecrate()
    {
        var source = Make(1, ActorKind.Player, "chanter", ActorRole.Healer);
        var target = Make(2, ActorKind.Enemy, "boss");
        Combos.ApplyComboEffects(State(), source, target, ComboRows.Lookup("warden", "chanter")!.Effects, Defs(HealPulse), _ => { });
        Assert.Equal(80, target.Health);
        Assert.Contains(target.Effects, e => e.EffectId == "heal_pulse");
    }
}

/// <summary>Port of tests/sim/combat/windows.test.ts and lock.test.ts (names kept), driven through Rules.Make.</summary>
public sealed class CombatWindowsTests
{
    private static readonly AbilityDef SetupAbility = Def("sundered_strike", new AbilityShape(ShapeKind.Target),
        new EffectOp[] { new WindowOp("sundered", 60, true, "warden") }, tags: new[] { "setup" });

    private static readonly AbilityDef PayoffAbility = Def("detonate", new AbilityShape(ShapeKind.Target),
        new EffectOp[] { new PayoffOp() }, tags: new[] { "payoff" });

    private static readonly AbilityDef FloorSetupAbility = Def("bulwark", new AbilityShape(ShapeKind.Self),
        new EffectOp[] { new WindowOp("bulwark-patch", 60, false, "warden") }, range: 0, tags: new[] { "setup" });

    private static readonly AbilityDef NarrowFloorSetupAbility = Def("bulwark-narrow", new AbilityShape(ShapeKind.Self),
        new EffectOp[] { new WindowOp("bulwark-patch", 60, false, "warden", RadiusMm: 1500) }, range: 0, tags: new[] { "setup" });

    private static readonly ComboTable BurstCombos = Table(
        ("warden", "cinder", new ComboDef("shatter", new EffectOp[] { new DamageOp(50, "physical") })),
        ("warden", "chanter", new ComboDef("consecrate", new EffectOp[] { new DamageOp(30, "physical") })));

    private static readonly ComboTable FieldCombos = Table(
        ("warden", "cinder", new ComboDef("shatter-field", new EffectOp[] { new DamageOp(10, "physical") })));

    private static StepRules WindowRules() =>
        Rules.Make(Config(Abilities(SetupAbility, PayoffAbility, FloorSetupAbility, NarrowFloorSetupAbility), combos: BurstCombos, fieldCombos: FieldCombos));

    private static bool HasWindow(ActorState a, string prefix) => a.Effects.Any(e => e.EffectId.StartsWith(prefix, System.StringComparison.Ordinal));

    [Fact]
    public void Window_open_then_Payoff_by_another_class_fires_the_looked_up_Combo_and_applies_its_effects()
    {
        var warden = Make(1, ActorKind.Player, "warden", ActorRole.Tank);
        var cinder = Make(2, ActorKind.Player, "cinder", ActorRole.Damage);
        var boss = Make(3, ActorKind.Enemy, "boss", maxHealth: 1000);
        var state = At(0, warden, cinder, boss);
        var rules = WindowRules();
        var (events, emit) = Collector();

        Tick(state, rules, Cmds(Cast(1, "sundered_strike", 3)), emit);
        Assert.Contains(events, e => e.Type == "window_opened" && Str(e, "windowId") == "sundered");
        Assert.True(HasWindow(boss, "window:sundered:"));

        Tick(state, rules, Cmds(Cast(2, "detonate", 3)), emit);
        var combo = events.First(e => e.Type == "combo");
        Assert.Equal("shatter", Str(combo, "comboId"));
        Assert.Equal(1, Int(combo, "setupActorId"));
        Assert.Equal(2, Int(combo, "payoffActorId"));
        Assert.Equal("sundered", Str(combo, "windowId"));
        Assert.Equal(950, boss.Health);
        Assert.False(HasWindow(boss, "window:"));
    }

    [Fact]
    public void Same_class_Payoff_does_nothing_to_the_Window_and_emits_combo_denied()
    {
        var warden1 = Make(1, ActorKind.Player, "warden", ActorRole.Tank);
        var warden2 = Make(2, ActorKind.Player, "warden", ActorRole.Damage);
        var boss = Make(3, ActorKind.Enemy, "boss", maxHealth: 1000);
        var state = At(0, warden1, warden2, boss);
        var rules = WindowRules();
        var (events, emit) = Collector();

        Tick(state, rules, Cmds(Cast(1, "sundered_strike", 3)), emit);
        Tick(state, rules, Cmds(Cast(2, "detonate", 3)), emit);

        Assert.DoesNotContain(events, e => e.Type == "combo");
        var denied = events.First(e => e.Type == "combo_denied");
        Assert.Equal("self_combo", Str(denied, "reason"));
        Assert.Equal(1000, boss.Health);
        Assert.True(HasWindow(boss, "window:"));
    }

    [Fact]
    public void Window_expiry_emits_window_expired_when_nobody_pays_it_off_in_time()
    {
        var warden = Make(1, ActorKind.Player, "warden", ActorRole.Tank);
        var boss = Make(3, ActorKind.Enemy, "boss", maxHealth: 1000);
        var state = At(0, warden, boss);
        var rules = WindowRules();
        var (events, emit) = Collector();

        Tick(state, rules, Cmds(Cast(1, "sundered_strike", 3)), emit);
        for (var i = 0; i < 61; i++)
        {
            Tick(state, rules, Cmds(), emit);
        }
        var expired = events.First(e => e.Type == "window_expired");
        Assert.Equal("sundered", Str(expired, "windowId"));
        Assert.False(HasWindow(boss, "window:"));
    }

    [Fact]
    public void Floor_Window_field_variant_accepts_any_other_classs_payoff_standing_in_the_patch_for_the_smaller_field_combo()
    {
        var warden = Make(1, ActorKind.Player, "warden", ActorRole.Tank);
        var cinder = Make(2, ActorKind.Player, "cinder", ActorRole.Damage, x: 100);
        var state = At(0, warden, cinder);
        var rules = WindowRules();
        var (events, emit) = Collector();

        Tick(state, rules, Cmds(Cast(1, "bulwark", 1)), emit);
        Assert.Contains(events, e => e.Type == "window_opened" && Bool(e, "onTarget") == false);

        Tick(state, rules, Cmds(Cast(2, "detonate", 2)), emit);
        var combo = events.First(e => e.Type == "combo");
        Assert.Equal("shatter-field", Str(combo, "comboId"));
        Assert.True(Bool(combo, "field"));
        Assert.Equal(90, cinder.Health);
    }

    [Fact]
    public void Floor_Window_field_variant_expires_with_window_expired_if_nobody_stands_in_it()
    {
        var warden = Make(1, ActorKind.Player, "warden", ActorRole.Tank);
        var state = At(0, warden);
        var rules = WindowRules();
        var (events, emit) = Collector();

        Tick(state, rules, Cmds(Cast(1, "bulwark", 1)), emit);
        for (var i = 0; i < 61; i++)
        {
            Tick(state, rules, Cmds(), emit);
        }
        Assert.Single(events, e => e.Type == "window_expired" && Bool(e, "onTarget") == false);
    }

    [Fact]
    public void Floor_Window_radiusMm_1500_does_NOT_catch_a_payoff_2000mm_away()
    {
        var warden = Make(1, ActorKind.Player, "warden", ActorRole.Tank);
        var cinder = Make(2, ActorKind.Player, "cinder", ActorRole.Damage, x: 2000);
        var state = At(0, warden, cinder);
        var rules = WindowRules();
        var (events, emit) = Collector();

        rules(state, Cmds(Cast(1, "bulwark-narrow", 1)), emit);
        rules(state, Cmds(Cast(2, "detonate", 2)), emit);
        Assert.DoesNotContain(events, e => e.Type == "combo");
        Assert.Equal(100, cinder.Health);
    }

    [Fact]
    public void Floor_Window_default_no_radiusMm_still_catches_a_payoff_2000mm_away()
    {
        var warden = Make(1, ActorKind.Player, "warden", ActorRole.Tank);
        var cinder = Make(2, ActorKind.Player, "cinder", ActorRole.Damage, x: 2000);
        var state = At(0, warden, cinder);
        var rules = WindowRules();
        var (events, emit) = Collector();

        rules(state, Cmds(Cast(1, "bulwark", 1)), emit);
        rules(state, Cmds(Cast(2, "detonate", 2)), emit);
        Assert.Equal("shatter-field", Str(events.First(e => e.Type == "combo"), "comboId"));
        Assert.Equal(90, cinder.Health);
    }

    // ---- lock.test.ts ----

    private static readonly AbilityDef LockAbility = Def("the_lock", new AbilityShape(ShapeKind.Self),
        new EffectOp[] { new WindowOp("lock", 60, true, "boss", Self: true, Alternate: true) }, range: 0);

    private static readonly ComboTable LockCombos = Table(
        ("boss", "warden", new ComboDef("lock-break", new EffectOp[] { new DamageOp(25, "physical") })),
        ("boss", "cinder", new ComboDef("lock-break", new EffectOp[] { new DamageOp(25, "physical") })),
        ("boss", "chanter", new ComboDef("lock-break", new EffectOp[] { new DamageOp(25, "physical") })));

    private static (SimState State, ActorState Boss, StepRules Rules) FreshLock()
    {
        var boss = Make(1, ActorKind.Enemy, "boss", maxHealth: 10000);
        var warden = Make(2, ActorKind.Player, "warden", ActorRole.Tank);
        var cinder = Make(3, ActorKind.Player, "cinder", ActorRole.Damage);
        var state = At(0, boss, warden, cinder);
        return (state, boss, Rules.Make(Config(Abilities(LockAbility, PayoffAbility), combos: LockCombos, fieldCombos: new ComboTable())));
    }

    [Fact]
    public void Lock_alternation_lets_warden_close_the_first_Lock_then_denies_warden_and_requires_cinder_on_the_second()
    {
        var (state, boss, rules) = FreshLock();
        var (events, emit) = Collector();

        Tick(state, rules, Cmds(Cast(1, "the_lock")), emit);
        Tick(state, rules, Cmds(Cast(2, "detonate", 1)), emit);
        Assert.Equal(2, Int(events.First(e => e.Type == "combo"), "payoffActorId"));
        Assert.Equal(9975, boss.Health);

        Tick(state, rules, Cmds(Cast(1, "the_lock")), emit);
        Tick(state, rules, Cmds(Cast(2, "detonate", 1)), emit);
        var denied = events.Where(e => e.Type == "combo_denied").ToList();
        Assert.Single(denied);
        Assert.Equal("lock_alternate", Str(denied[0], "reason"));
        Assert.Equal(9975, boss.Health);

        Tick(state, rules, Cmds(Cast(3, "detonate", 1)), emit);
        var combos = events.Where(e => e.Type == "combo").ToList();
        Assert.Equal(2, combos.Count);
        Assert.Equal(3, Int(combos[1], "payoffActorId"));
        Assert.Equal(9950, boss.Health);
    }

    [Fact]
    public void Three_failed_Locks_emit_lock_enrage_on_the_third_unpaid_expiry_and_not_before()
    {
        var (state, boss, rules) = FreshLock();
        var (events, emit) = Collector();
        for (var cycle = 0; cycle < 3; cycle++)
        {
            Tick(state, rules, Cmds(Cast(1, "the_lock")), emit);
            for (var i = 0; i < 61; i++)
            {
                Tick(state, rules, Cmds(), emit);
            }
        }
        var enrages = events.Where(e => e.Type == "lock_enrage").ToList();
        Assert.Single(enrages);
        Assert.Equal(boss.Id, Int(enrages[0], "actorId"));
        var expiredLocks = events.Where(e => e.Type == "window_expired" && Bool(e, "lock") == true).ToList();
        Assert.Equal(3, expiredLocks.Count);
        Assert.True(events.IndexOf(enrages[0]) > events.IndexOf(expiredLocks[1]));
    }

    private static (string Final, List<string> EventTypes) RunFullSequence()
    {
        var (state, _, rules) = FreshLock();
        var (events, emit) = Collector();
        Tick(state, rules, Cmds(Cast(1, "the_lock")), emit);
        Tick(state, rules, Cmds(Cast(2, "detonate", 1)), emit);
        Tick(state, rules, Cmds(Cast(1, "the_lock")), emit);
        Tick(state, rules, Cmds(Cast(2, "detonate", 1)), emit);
        Tick(state, rules, Cmds(Cast(3, "detonate", 1)), emit);
        Tick(state, rules, Cmds(Cast(1, "the_lock")), emit);
        for (var i = 0; i < 61; i++)
        {
            Tick(state, rules, Cmds(), emit);
        }
        var sb = new StringBuilder(Projection.Line(state, 0, 0));
        foreach (var a in state.Actors.Values)
        {
            foreach (var e in a.Effects)
            {
                sb.Append('|').Append(e.EffectId).Append(',').Append(e.SourceId).Append(',').Append(e.Stacks).Append(',').Append(e.EndsTick);
            }
        }
        return (Fnv1a.Hex(Fnv1a.Hash(sb.ToString())) + ":" + string.Join(",", events.Select(e => e.ToCanonicalJson())), events.Select(e => e.Type).ToList());
    }

    [Fact]
    public void Determinism_of_the_Windows_and_Combos_sequence_produces_an_identical_final_state_hash_and_event_sequence_across_independent_runs()
    {
        var runs = new[] { RunFullSequence(), RunFullSequence(), RunFullSequence() };
        Assert.Equal(runs[0].Final, runs[1].Final);
        Assert.Equal(runs[0].Final, runs[2].Final);
        Assert.Equal(runs[0].EventTypes, runs[1].EventTypes);
        Assert.Equal(runs[0].EventTypes, runs[2].EventTypes);
    }
}

/// <summary>Port of tests/sim/combat/floor-telegraphs.test.ts (names kept).</summary>
public sealed class CombatFloorTelegraphsTests
{
    private static SimState Base(int tick = 10, params ActorState[] actors) => At(tick, actors);

    private static FloorWindow Fw(string windowId = "cinderfield", string setupClass = "cinder", int setupActorId = 1, long x = 4500, long z = -1200, int endsTick = 170, long radiusMm = 1800) => new()
    {
        WindowId = windowId,
        SetupClass = setupClass,
        SetupActorId = setupActorId,
        Pos = new Vec3(x, 0, z),
        EndsTick = endsTick,
        RadiusMm = radiusMm,
    };

    private static ActorState CinderActor() => Make(1, ActorKind.Player, "cinder", ActorRole.Damage, maxResource: 100);

    private static ActorState SeedBoss() => Make(9, ActorKind.Enemy, "seed", maxHealth: 5000);

    [Fact]
    public void IsFriendlyFloorWindow_is_friendly_when_the_setup_actor_is_a_live_player() =>
        Assert.True(FloorTelegraphs.IsFriendlyFloorWindow(Base(10, CinderActor()), Fw(setupActorId: 1)));

    [Fact]
    public void IsFriendlyFloorWindow_fails_closed_to_hostile_when_the_setup_actor_is_an_enemy() =>
        Assert.False(FloorTelegraphs.IsFriendlyFloorWindow(Base(10, SeedBoss()), Fw(setupActorId: 9, setupClass: "boss")));

    [Fact]
    public void IsFriendlyFloorWindow_fails_closed_to_hostile_when_the_setup_actor_can_no_longer_be_found() =>
        Assert.False(FloorTelegraphs.IsFriendlyFloorWindow(Base(), Fw(setupActorId: 404)));

    [Fact]
    public void FloorTelegraphId_nests_window_id_setupClass_after_floor_for_a_friendly_patch() =>
        Assert.Equal(FloorTelegraphs.FloorPatchPrefix + "window:bulwark:warden", FloorTelegraphs.FloorTelegraphId(Fw("bulwark", "warden"), true));

    [Fact]
    public void FloorTelegraphId_is_bare_floor_windowId_for_a_hostile_patch() =>
        Assert.Equal(FloorTelegraphs.FloorPatchPrefix + "seedbloom", FloorTelegraphs.FloorTelegraphId(Fw("seedbloom", "boss"), false));

    [Fact]
    public void DeriveFloorTelegraphs_emits_one_danger_false_circle_Telegraph_per_open_patch_with_the_right_radius_expiry_id()
    {
        var state = Base(10, CinderActor());
        var fw = Fw();
        state.FloorWindows[fw.WindowId] = fw;
        var t = Assert.Single(FloorTelegraphs.DeriveFloorTelegraphs(state));
        Assert.Equal(new FloorTelegraph("floor:window:cinderfield:cinder", 4500, -1200, 1800, 170), t);
        Assert.Equal("{\"center\":{\"x\":4500,\"z\":-1200},\"danger\":false,\"id\":\"floor:window:cinderfield:cinder\",\"radius\":1800,\"resolvesTick\":170,\"shape\":\"circle\"}", t.ToJs().ToCanonicalJson());
    }

    [Fact]
    public void DeriveFloorTelegraphs_classifies_a_boss_sourced_floor_Window_as_hostile()
    {
        var state = Base(250, SeedBoss());
        var fw = Fw("seedbloom", "boss", 9, 1000, 2000, 300, 2500);
        state.FloorWindows[fw.WindowId] = fw;
        var t = Assert.Single(FloorTelegraphs.DeriveFloorTelegraphs(state));
        Assert.Equal("floor:seedbloom", t.Id);
        Assert.False(t.Danger);
        Assert.Equal((1000L, 2000L), (t.CenterX, t.CenterZ));
        Assert.Equal(2500, t.Radius);
    }

    [Fact]
    public void DeriveFloorTelegraphs_excludes_a_patch_whose_endsTick_has_already_passed()
    {
        var state = Base(10, CinderActor());
        var fw = Fw(endsTick: 5);
        state.FloorWindows[fw.WindowId] = fw;
        Assert.Empty(FloorTelegraphs.DeriveFloorTelegraphs(state));
    }

    [Fact]
    public void DeriveFloorTelegraphs_returns_empty_when_floorWindows_is_empty_hand_built_fixtures_that_predate_the_field() =>
        Assert.Empty(FloorTelegraphs.DeriveFloorTelegraphs(Base()));

    [Fact]
    public void DeriveFloorTelegraphs_emits_multiple_friendly_and_hostile_patches_independently_in_the_same_tick()
    {
        var state = Base(10, CinderActor(), SeedBoss());
        state.FloorWindows["bulwark"] = Fw("bulwark", "warden", 1);
        state.FloorWindows["seedbloom"] = Fw("seedbloom", "boss", 9);
        var ids = FloorTelegraphs.DeriveFloorTelegraphs(state).Select(t => t.Id).OrderBy(s => s, System.StringComparer.Ordinal);
        Assert.Equal(new[] { "floor:seedbloom", "floor:window:bulwark:warden" }, ids);
    }

    [Fact]
    public void FloorWindowOpenedTelegraph_matches_DeriveFloorTelegraphs_output_for_the_same_single_patch()
    {
        var state = Base(10, CinderActor());
        var fw = Fw();
        state.FloorWindows[fw.WindowId] = fw;
        Assert.Equal(FloorTelegraphs.DeriveFloorTelegraphs(state)[0], FloorTelegraphs.FloorWindowOpenedTelegraph(state, fw));
    }
}
