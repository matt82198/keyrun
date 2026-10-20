using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Xunit;

namespace Keyrun.Sim.Tests.Encounter;

/// <summary>
/// tests/sim/encounter/scripts-target.test.ts ported 1:1 (resolveScriptTarget: one test per targeting rule), plus
/// the chase-only cases of chase-tuning.test.ts (per-enemy margin). The rules.ts walking cases belong to lane 2B.
/// </summary>
public sealed class ScriptTargetTests
{
    private static ActorState Make(int id, ActorKind kind, Vec3? pos = null, bool alive = true, long health = 1000, params (int Id, long Val)[] threat)
    {
        var a = EncounterTestKit.Actor(id, kind, pos, alive, health);
        foreach (var (tid, v) in threat)
        {
            a.Threat[tid] = v;
        }
        return a;
    }

    private static SimState MakeState(params ActorState[] actors) => EncounterTestKit.State(100, new PrngState(111, 222, 333, 444), actors);

    private static readonly ScriptAbility TargetAbility = new("hostile.hit", 3000, "target", false);
    private static readonly ScriptAbility HealAbility = TargetAbility with { Id = "ally.heal", Heals = true };
    private static readonly ScriptAbility SelfAbility = TargetAbility with { Id = "self.buff", Range = 0, ShapeKind = "self" };
    private static readonly ScriptAbility CircleAbility = TargetAbility with { Id = "aoe.circle", Range = 0, ShapeKind = "circle" };

    private static ScriptTargetResult Resolve(SimState s, int caster, ScriptAbility? ability = null, ScriptTargetOverride? o = null) =>
        ScriptTarget.Resolve(s, s.Actors[caster], o, ability);

    [Fact]
    public void Hostile_default_picks_the_casters_highest_threat_living_player()
    {
        var s = MakeState(Make(1, ActorKind.Enemy, threat: new[] { (10, 50L), (11, 200L) }), Make(10, ActorKind.Player), Make(11, ActorKind.Player));
        Assert.Equal(new ScriptTargetResult(11, false), Resolve(s, 1, TargetAbility));
    }

    [Fact]
    public void Hostile_fallback_no_threat_entries_falls_back_to_the_nearest_living_player()
    {
        var s = MakeState(Make(1, ActorKind.Enemy), Make(10, ActorKind.Player, new Vec3(0, 0, 2000)), Make(11, ActorKind.Player, new Vec3(0, 0, 500)));
        Assert.Equal(new ScriptTargetResult(11, false), Resolve(s, 1, TargetAbility));
    }

    [Fact]
    public void Hostile_a_dead_top_threat_entry_is_skipped_in_favour_of_the_nearest_living_player()
    {
        var s = MakeState(Make(1, ActorKind.Enemy, threat: new[] { (10, 999L) }), Make(10, ActorKind.Player, alive: false), Make(11, ActorKind.Player, new Vec3(0, 0, 100)));
        Assert.Equal(new ScriptTargetResult(11, false), Resolve(s, 1, TargetAbility));
    }

    [Fact]
    public void Heal_ability_targets_the_lowest_health_living_ally_enemy_over_any_player()
    {
        var s = MakeState(Make(1, ActorKind.Enemy), Make(10, ActorKind.Player), Make(20, ActorKind.Enemy, health: 900), Make(21, ActorKind.Enemy, health: 100));
        Assert.Equal(new ScriptTargetResult(21, false), Resolve(s, 1, HealAbility));
    }

    [Fact]
    public void Heal_ability_falls_back_to_self_when_no_other_living_ally_exists()
    {
        var s = MakeState(Make(1, ActorKind.Enemy), Make(10, ActorKind.Player));
        Assert.Equal(new ScriptTargetResult(1, false), Resolve(s, 1, HealAbility));
    }

    [Fact]
    public void Self_shaped_ability_always_targets_itself_bypassing_threat_range_entirely()
    {
        var s = MakeState(Make(1, ActorKind.Enemy, new Vec3(5000, 0, 5000)), Make(10, ActorKind.Player));
        Assert.Equal(new ScriptTargetResult(1, false), Resolve(s, 1, SelfAbility));
    }

    [Fact]
    public void Range_gating_a_player_outside_ability_range_is_excluded_from_candidates()
    {
        var s = MakeState(Make(1, ActorKind.Enemy), Make(10, ActorKind.Player, new Vec3(0, 0, 5000)), Make(11, ActorKind.Player, new Vec3(0, 0, 1000)));
        Assert.Equal(new ScriptTargetResult(11, false), Resolve(s, 1, TargetAbility));
    }

    [Fact]
    public void Range_gating_nothing_in_range_on_a_target_shaped_ability_blocks_the_cast()
    {
        var s = MakeState(Make(1, ActorKind.Enemy), Make(10, ActorKind.Player, new Vec3(0, 0, 5000)));
        Assert.Equal(new ScriptTargetResult(null, true), Resolve(s, 1, TargetAbility));
    }

    [Fact]
    public void A_known_non_target_non_self_shape_needs_no_target_and_is_left_alone()
    {
        var s = MakeState(Make(1, ActorKind.Enemy), Make(10, ActorKind.Player));
        Assert.Equal(new ScriptTargetResult(null, false), Resolve(s, 1, CircleAbility));
    }

    [Fact]
    public void Override_self_forces_itself_even_on_a_hostile_target_shaped_ability()
    {
        var s = MakeState(Make(1, ActorKind.Enemy), Make(10, ActorKind.Player));
        Assert.Equal(new ScriptTargetResult(1, false), Resolve(s, 1, TargetAbility, ScriptTargetOverride.Self));
    }

    [Fact]
    public void Override_threat_forces_the_player_pool_plus_threat_nearest_selection_even_on_a_heal_ability()
    {
        var s = MakeState(Make(1, ActorKind.Enemy, threat: new[] { (10, 5L) }), Make(10, ActorKind.Player), Make(20, ActorKind.Enemy, health: 1));
        Assert.Equal(new ScriptTargetResult(10, false), Resolve(s, 1, HealAbility, ScriptTargetOverride.Threat));
    }

    [Fact]
    public void Override_random_deterministically_picks_one_in_range_candidate_from_the_same_state()
    {
        var s = MakeState(Make(1, ActorKind.Enemy), Make(10, ActorKind.Player), Make(11, ActorKind.Player));
        var a = Resolve(s, 1, TargetAbility, ScriptTargetOverride.Random);
        Assert.Equal(a, Resolve(s, 1, TargetAbility, ScriptTargetOverride.Random));
        Assert.Contains(a.TargetId!.Value, new[] { 10, 11 });
        Assert.False(a.Blocked);
        // script-target.ts:292 randomId: seed [(111^100)>>>0, (222^1)>>>0, 333, 444], nextInt(seed, 2) over ids [10, 11].
        var (idx, _) = Prng.NextInt(new PrngState(111u ^ 100u, 222u ^ 1u, 333u, 444u), 2);
        Assert.Equal(idx == 0 ? 10 : 11, a.TargetId);
    }

    [Fact]
    public void Ability_unknown_resolves_the_hostile_default_whenever_a_living_player_exists()
    {
        var s = MakeState(Make(1, ActorKind.Enemy), Make(10, ActorKind.Player, new Vec3(0, 0, 100)));
        Assert.Equal(new ScriptTargetResult(10, false), Resolve(s, 1));
    }

    [Fact]
    public void Ability_unknown_and_no_living_player_never_blocks()
    {
        var s = MakeState(Make(1, ActorKind.Enemy));
        Assert.Equal(new ScriptTargetResult(null, false), Resolve(s, 1));
    }

    [Fact]
    public void RangeMm_is_the_integer_xz_distance()
    {
        Assert.Equal(5000, ScriptTarget.RangeMm(Make(1, ActorKind.Enemy, new Vec3(0, 900, 0)), Make(2, ActorKind.Player, new Vec3(3000, 0, -4000))));
        Assert.Equal(1, ScriptTarget.RangeMm(Make(1, ActorKind.Enemy), Make(2, ActorKind.Player, new Vec3(1, 0, 1)))); // isqrt(2)
    }

    // ---- chase.ts per-enemy margin (chase-tuning.test.ts "chase.ts: per-enemy margin") ----

    private static readonly IReadOnlyDictionary<string, ScriptAbility> Swing = new Dictionary<string, ScriptAbility>
    {
        ["swing"] = new("swing", 3000, "target", false),
    };

    private static int ChaseAt(long distance, long? margin = null)
    {
        var enemy = EncounterTestKit.Actor(7, ActorKind.Enemy, health: 1);
        enemy.ChaseMarginMm = margin;
        var player = EncounterTestKit.Actor(1, ActorKind.Player, new Vec3(0, 0, distance), health: 1);
        var state = EncounterTestKit.State(0, new PrngState(0, 0, 0, 0), enemy, player);
        return Chase.ChaseCommands(state, EncounterTestKit.Scripts(7, ScriptStep.Loop(40, "swing")), Swing).Count;
    }

    // ENEMY-ENGAGE (D28 ruling 2026-10-20, twin of chase-tuning.test.ts lines 73/77): in reach the chaser still
    // emits a turn-only move (Dir 0,0) to face its target, so "stops at the margin" means no command that WALKS.
    private static int WalksAt(long distance, long? margin = null)
    {
        var enemy = EncounterTestKit.Actor(7, ActorKind.Enemy, health: 1);
        enemy.ChaseMarginMm = margin;
        var player = EncounterTestKit.Actor(1, ActorKind.Player, new Vec3(0, 0, distance), health: 1);
        var state = EncounterTestKit.State(0, new PrngState(0, 0, 0, 0), enemy, player);
        return Chase.ChaseCommands(state, EncounterTestKit.Scripts(7, ScriptStep.Loop(40, "swing")), Swing)
            .Count(c => c is MoveCommand m && (m.Dir.X != 0 || m.Dir.Z != 0));
    }

    [Fact]
    public void Chase_defaults_to_CHASE_MARGIN_MM()
    {
        Assert.Equal(0, WalksAt(3000 - Chase.ChaseMarginMm));
        Assert.Equal(1, ChaseAt(3000 - Chase.ChaseMarginMm + 1));
    }

    [Fact]
    public void Chase_honours_ActorState_chaseMarginMm()
    {
        Assert.Equal(0, WalksAt(2900, 100));
        Assert.Equal(1, ChaseAt(2901, 100));
        Assert.Equal(1, ChaseAt(2600));
    }

    [Fact]
    public void Chase_engage_range_is_the_shortest_hostile_target_shaped_cast_and_heals_and_onDeath_never_chase()
    {
        var abilities = new Dictionary<string, ScriptAbility>
        {
            ["long"] = new("long", 9000, "target", false),
            ["short"] = new("short", 2500, "target", false),
            ["heal"] = new("heal", 100, "target", true),
            ["cone"] = new("cone", 50, "cone", false),
            ["bloom"] = new("bloom", 10, "target", false),
        };
        var script = new[] { ScriptStep.Loop(40, "long"), ScriptStep.Loop(60, "short"), ScriptStep.Loop(80, "heal"), ScriptStep.Loop(90, "cone"), ScriptStep.OnDeath("bloom") };
        Assert.Equal(2500, Chase.EngageRangeMm(script, abilities));
        Assert.Null(Chase.EngageRangeMm(new[] { ScriptStep.Loop(80, "heal") }, abilities));
        Assert.Null(Chase.EngageRangeMm(script, new Dictionary<string, ScriptAbility>()));
    }

    [Fact]
    public void Chase_never_moves_a_casting_enemy()
    {
        var enemy = EncounterTestKit.Actor(7, ActorKind.Enemy);
        enemy.CastId = "swing";
        var player = EncounterTestKit.Actor(1, ActorKind.Player, new Vec3(0, 0, 9000));
        Assert.Empty(Chase.ChaseCommands(EncounterTestKit.State(0, enemy, player), EncounterTestKit.Scripts(7, ScriptStep.Loop(40, "swing")), Swing));
    }
}
