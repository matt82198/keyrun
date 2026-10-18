using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Xunit;
using static Keyrun.Sim.Tests.CombatTestKit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// Port of tests/sim/combat/targeting.test.ts and the shape cases of targeting-resolve.test.ts (names kept).
/// The two "cast pipeline and window pipeline" cases need casting.ts/windows.ts (lane 2B) and port there.
/// </summary>
public sealed class CombatTargetingTests
{
    private static ActorState At(int id, long x, long z, int yaw = 0) => Actor(id, ActorKind.Enemy, x, z, yaw);

    private static readonly EffectOp Dmg = new DamageOp(10, "physical");

    [Fact]
    public void InCircle_includes_a_target_exactly_on_the_radius_and_excludes_one_just_outside() =>
        Assert.Equal(new[] { 1 }, Targeting.InCircle(Vec3.Zero, 100, new[] { At(1, 60, 80), At(2, 100, 1) }));

    [Fact]
    public void InCone_includes_a_target_directly_ahead_within_a_90_degree_cone()
    {
        var caster = At(9, 0, 0);
        Assert.Equal(new[] { 1 }, Targeting.InCone(caster, 256, 200, new[] { caster, At(1, 100, 0) }));
    }

    [Fact]
    public void InCone_excludes_a_target_90_degrees_to_the_side_of_a_90_degree_cone()
    {
        var caster = At(9, 0, 0);
        Assert.Empty(Targeting.InCone(caster, 256, 200, new[] { caster, At(1, 0, 100) }));
    }

    [Fact]
    public void InCone_includes_a_target_just_inside_the_half_angle_and_excludes_one_just_outside()
    {
        var caster = At(9, 0, 0);
        Assert.Equal(new[] { 1 }, Targeting.InCone(caster, 256, 200, new[] { caster, At(1, 100, 90), At(2, 100, 110) }));
    }

    [Fact]
    public void InCone_excludes_targets_beyond_the_radius()
    {
        var caster = At(9, 0, 0);
        Assert.Empty(Targeting.InCone(caster, 256, 200, new[] { caster, At(1, 300, 0) }));
    }

    /// <summary>
    /// targeting.ts:53/:75 tableIndex(angle1024 / 2) uses JS Math.round (ties toward +infinity): angle 264 -> 132 / 8
    /// = 16.5 -> 17 (cos 2751), while the odd angle 263 -> 131.5 / 8 = 16.4375 -> 16 (cos 2896). A banker's or
    /// truncating round would give 16 for both. Target (100, 105): dist 145 exactly, dot 409600; 2896 * 145 =
    /// 419920 &gt; dot (outside the 263 cone), 2751 * 145 = 398895 &lt;= dot (inside the 264 cone).
    /// </summary>
    [Fact]
    public void InCone_rounds_the_half_angle_table_index_tie_up()
    {
        var caster = At(9, 0, 0);
        var t = At(1, 100, 105);
        Assert.Empty(Targeting.InCone(caster, 263, 500, new[] { caster, t }));
        Assert.Equal(new[] { 1 }, Targeting.InCone(caster, 264, 500, new[] { caster, t }));
    }

    [Fact]
    public void InCone_excludes_a_target_on_the_casters_own_position()
    {
        var caster = At(9, 0, 0);
        Assert.Empty(Targeting.InCone(caster, 1023, 200, new[] { caster, At(1, 0, 0) }));
    }

    [Fact]
    public void InLine_includes_a_target_ahead_and_within_the_half_width_excludes_one_wider_than_it()
    {
        var caster = At(9, 0, 0);
        Assert.Equal(new[] { 1 }, Targeting.InLine(caster, 200, 50, new[] { caster, At(1, 150, 20), At(2, 150, 60) }));
    }

    [Fact]
    public void InLine_excludes_a_target_beyond_the_line_length_and_one_behind_the_caster()
    {
        var caster = At(9, 0, 0);
        Assert.Empty(Targeting.InLine(caster, 200, 50, new[] { caster, At(1, 250, 0), At(2, -10, 0) }));
    }

    /// <summary>targeting.ts:99 Math.trunc: a target 1 mm behind on a rotated yaw truncates forward to 0 and is kept.</summary>
    [Fact]
    public void InLine_truncates_forward_toward_zero_so_a_sub_unit_negative_projection_counts_as_0()
    {
        var caster = At(9, 0, 0, yaw: 8); // face (4091, 201)
        var t = At(1, -1, 20); // forward = trunc((4091 * -1 + 201 * 20) / 4096) = trunc(-71 / 4096) = 0
        Assert.Equal(new[] { 1 }, Targeting.InLine(caster, 200, 50, new[] { caster, t }));
    }

    [Fact]
    public void CycleTarget_selects_the_lowest_id_when_starting_from_null() =>
        Assert.Equal(2, Targeting.CycleTarget(new[] { At(5, 0, 0), At(2, 0, 0), At(8, 0, 0) }, null));

    [Fact]
    public void CycleTarget_advances_to_the_next_id_in_ascending_order_and_wraps_around()
    {
        var actors = new[] { At(5, 0, 0), At(2, 0, 0), At(8, 0, 0) };
        Assert.Equal(5, Targeting.CycleTarget(actors, 2));
        Assert.Equal(8, Targeting.CycleTarget(actors, 5));
        Assert.Equal(2, Targeting.CycleTarget(actors, 8));
        Assert.Equal(2, Targeting.CycleTarget(actors, 77));
    }

    [Fact]
    public void CycleTarget_skips_dead_actors()
    {
        var actors = new[] { At(5, 0, 0), At(2, 0, 0), At(8, 0, 0) };
        actors[0].Alive = false;
        Assert.Equal(8, Targeting.CycleTarget(actors, 2));
    }

    [Fact]
    public void CycleTarget_returns_null_when_no_actors_are_alive()
    {
        var actors = new[] { At(5, 0, 0) };
        actors[0].Alive = false;
        Assert.Null(Targeting.CycleTarget(actors, null));
    }

    [Fact]
    public void FaceDir_yaw_0_faces_plus_x_and_yaw_256_faces_plus_z()
    {
        Assert.Equal(new Dir2(4096, 0), Targeting.FaceDir(0));
        Assert.Equal(new Dir2(0, 4096), Targeting.FaceDir(256));
    }

    // ---- targeting-resolve.test.ts "resolveTargets shapes" ----

    [Fact]
    public void ResolveTargets_self_resolves_to_the_caster_only()
    {
        var caster = Actor(1, ActorKind.Player, maxResource: 100, role: ActorRole.Tank, classId: "warden");
        var state = State(caster);
        Assert.Equal(new[] { caster }, Targeting.ResolveTargets(state, caster, Ability(new AbilityShape(ShapeKind.Self), new[] { Dmg }), Affinity.Hostile));
    }

    [Fact]
    public void ResolveTargets_target_resolves_to_the_casters_current_targetId()
    {
        var caster = Actor(1, ActorKind.Player, maxResource: 100, role: ActorRole.Tank, classId: "warden");
        var target = Actor(2, ActorKind.Enemy, x: 100);
        caster.TargetId = 2;
        var state = State(caster, target);
        Assert.Equal(new[] { target }, Targeting.ResolveTargets(state, caster, Ability(new AbilityShape(ShapeKind.Target), new[] { Dmg }), Affinity.Hostile));
    }

    [Fact]
    public void ResolveTargets_target_resolves_to_nothing_when_no_targetId_is_set()
    {
        var caster = Actor(1, ActorKind.Player, maxResource: 100, role: ActorRole.Tank, classId: "warden");
        Assert.Empty(Targeting.ResolveTargets(State(caster), caster, Ability(new AbilityShape(ShapeKind.Target), new[] { Dmg }), Affinity.Hostile));
    }

    [Fact]
    public void ResolveTargets_cone_resolves_to_actors_ahead_within_the_cone_and_excludes_the_caster_and_actors_to_the_side()
    {
        var caster = Actor(1, ActorKind.Player, maxResource: 100, role: ActorRole.Tank, classId: "warden");
        var ahead = Actor(2, ActorKind.Enemy, x: 100);
        var side = Actor(3, ActorKind.Enemy, z: 100);
        var state = State(caster, ahead, side);
        var def = Ability(new AbilityShape(ShapeKind.Cone, Radius: 200, Angle1024: 256), new[] { Dmg });
        Assert.Equal(new[] { ahead }, Targeting.ResolveTargets(state, caster, def, Affinity.Hostile));
    }

    [Fact]
    public void ResolveTargets_circle_resolves_around_the_caster_and_for_a_hostile_ability_excludes_the_caster_and_allies()
    {
        var caster = Actor(1, ActorKind.Player, 500, 500, maxResource: 100, role: ActorRole.Tank, classId: "warden");
        var inRange = Actor(2, ActorKind.Enemy, 550, 500);
        var allyInRange = Actor(4, ActorKind.Player, 540, 500, classId: "y");
        var outOfRange = Actor(3, ActorKind.Enemy, 5000, 5000);
        var state = State(caster, inRange, allyInRange, outOfRange);
        var def = Ability(new AbilityShape(ShapeKind.Circle, Radius: 100), new[] { Dmg });
        Assert.Equal(new[] { inRange }, Targeting.ResolveTargets(state, caster, def, Affinity.Hostile));
    }

    [Fact]
    public void ResolveTargets_circle_resolves_a_friendly_ability_to_the_casters_own_kind_including_the_caster_excluding_enemies()
    {
        var caster = Actor(1, ActorKind.Player, 500, 500, maxResource: 100, role: ActorRole.Healer, classId: "chanter");
        var allyInRange = Actor(2, ActorKind.Player, 550, 500, classId: "y");
        var enemyInRange = Actor(3, ActorKind.Enemy, 540, 500);
        var state = State(caster, allyInRange, enemyInRange);
        var def = Ability(new AbilityShape(ShapeKind.Circle, Radius: 100), new EffectOp[] { new HealOp(10) });
        Assert.Equal(new[] { caster, allyInRange }, Targeting.ResolveTargets(state, caster, def, Affinity.Friendly));
    }

    /// <summary>targeting.ts:143: a friendly cone adds the caster even though the cone geometry excludes it; ids come back sorted.</summary>
    [Fact]
    public void ResolveTargets_friendly_cone_adds_the_caster_and_sorts_ids_ascending()
    {
        var caster = Actor(5, ActorKind.Player, maxResource: 100);
        var ally = Actor(2, ActorKind.Player, 100, 0);
        var state = State(caster, ally);
        var def = Ability(new AbilityShape(ShapeKind.Cone, Radius: 200, Angle1024: 256), new EffectOp[] { new HealOp(10) });
        Assert.Equal(new[] { 2, 5 }, Targeting.ResolveTargets(state, caster, def, Affinity.Friendly).Select(a => a.Id));
    }

    /// <summary>targeting.ts:132-136 `?? 0`: a circle with no radius still hits an actor standing exactly on the caster.</summary>
    [Fact]
    public void ResolveTargets_missing_radius_defaults_to_0()
    {
        var caster = Actor(1, ActorKind.Player, maxResource: 100);
        var stacked = Actor(2, ActorKind.Enemy);
        var near = Actor(3, ActorKind.Enemy, 1, 0);
        var state = State(caster, stacked, near);
        var def = Ability(new AbilityShape(ShapeKind.Circle), new[] { Dmg });
        Assert.Equal(new[] { 2 }, Targeting.ResolveTargets(state, caster, def, Affinity.Hostile).Select(a => a.Id));
    }
}
