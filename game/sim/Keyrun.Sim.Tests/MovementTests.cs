using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// Port of tests/sim/combat/movement.test.ts (sim/combat/movement.ts), plus cases that pin the
/// Math.trunc sites (movement.ts:46-48, :61, :67-68) on negative values, where floor would differ.
/// </summary>
public sealed class MovementTests
{
    private static ActorState At(int id, long x, long z) => new()
    {
        Id = id, Kind = ActorKind.Player, ClassId = "x", Role = ActorRole.Damage, Pos = new Vec3(x, 0, z), MaxHealth = 1, Health = 1,
    };

    [Fact]
    public void Isqrt_is_exact_for_perfect_squares_and_floors_otherwise()
    {
        Assert.Equal(0, Movement.Isqrt(0));
        Assert.Equal(1, Movement.Isqrt(1));
        Assert.Equal(12, Movement.Isqrt(144));
        Assert.Equal(1000, Movement.Isqrt(1000000));
        Assert.Equal(1, Movement.Isqrt(2));
        Assert.Equal(9, Movement.Isqrt(99));
        Assert.Equal(1000, Movement.Isqrt(1000001));
    }

    [Fact]
    public void Isqrt_short_circuits_non_positive_input_to_zero()
    {
        Assert.Equal(0, Movement.Isqrt(-5));
    }

    [Fact]
    public void Moves_at_exactly_base_speed_along_a_unit_axis()
    {
        var a = At(1, 0, 0);
        Movement.ApplyMovement(a, new Dir2(1, 0), 300, 0, 512);
        Assert.Equal(new Vec3(300, 0, 0), a.Pos);
        Assert.Equal(512, a.Yaw);
    }

    [Fact]
    public void Normalizes_a_diagonal_direction_to_the_same_integer_speed()
    {
        var a = At(1, 0, 0);
        Movement.ApplyMovement(a, new Dir2(3, 4), 500, 0, 0);
        Assert.Equal(new Vec3(300, 0, 400), a.Pos);
    }

    [Fact]
    public void Applies_a_movement_modifier_in_basis_points()
    {
        var a = At(1, 0, 0);
        Movement.ApplyMovement(a, new Dir2(1, 0), 300, 5000, 0);
        Assert.Equal(450, a.Pos.X);
    }

    [Fact]
    public void Updates_yaw_even_when_the_direction_is_zero()
    {
        var a = At(1, 10, 10);
        Movement.ApplyMovement(a, new Dir2(0, 0), 300, 0, 256);
        Assert.Equal(new Vec3(10, 0, 10), a.Pos);
        Assert.Equal(256, a.Yaw);
    }

    [Fact]
    public void Truncates_negative_step_components_toward_zero()
    {
        var a = At(1, 0, 0);
        Movement.ApplyMovement(a, new Dir2(-3, -4), 7, 0, 0); // -21/5 = -4.2, -28/5 = -5.6
        Assert.Equal(new Vec3(-4, 0, -5), a.Pos);
    }

    [Fact]
    public void A_slow_that_overshoots_clamps_speed_to_zero()
    {
        var a = At(1, 5, 5);
        Movement.ApplyMovement(a, new Dir2(1000, 0), 300, -15000, 0); // trunc(300 * -5000 / 10000) = -150 -> max(0)
        Assert.Equal(new Vec3(5, 0, 5), a.Pos);
    }

    [Fact]
    public void Speed_truncates_the_modifier_product()
    {
        var a = At(1, 0, 0);
        Movement.ApplyMovement(a, new Dir2(0, 1000), 175, -3333, 0); // trunc(175 * 6667 / 10000) = trunc(116.6725) = 116
        Assert.Equal(new Vec3(0, 0, 116), a.Pos);
    }

    [Fact]
    public void Pushes_two_overlapping_actors_apart_symmetrically_along_x()
    {
        var a = At(1, 0, 0);
        var b = At(2, 100, 0);
        Movement.ResolveCollision(a, b, 100);
        Assert.Equal(-50, a.Pos.X);
        Assert.Equal(150, b.Pos.X);
    }

    [Fact]
    public void Enemy_no_push_a_player_and_an_enemy_never_collide_but_same_kinds_do()
    {
        // ENEMY-NO-PUSH (STATE.md D50): WoW bodies, an enemy never shoves a player and a player never shoves an enemy.
        var player = At(1, 0, 0);
        var player2 = At(2, 50, 0);
        var enemy = At(3, 50, 0);
        enemy.Kind = ActorKind.Enemy;
        var enemy2 = At(4, 60, 0);
        enemy2.Kind = ActorKind.Enemy;
        Assert.False(Movement.BodiesCollide(player, enemy));
        Assert.False(Movement.BodiesCollide(enemy, player));
        Assert.True(Movement.BodiesCollide(player, player2));
        Assert.True(Movement.BodiesCollide(enemy, enemy2));
        enemy2.Idle = true;
        Assert.False(Movement.BodiesCollide(enemy, enemy2)); // PRESPAWN-AGGRO: an idle body is never pushed
        enemy2.Idle = false;
        enemy2.Alive = false;
        Assert.False(Movement.BodiesCollide(enemy, enemy2));
    }

    [Fact]
    public void Does_nothing_when_already_far_enough_apart()
    {
        var a = At(1, 0, 0);
        var b = At(2, 500, 0);
        Movement.ResolveCollision(a, b, 100);
        Assert.Equal(0, a.Pos.X);
        Assert.Equal(500, b.Pos.X);
    }

    [Fact]
    public void Exactly_touching_is_not_an_overlap()
    {
        var a = At(1, 0, 0);
        var b = At(2, 200, 0);
        Movement.ResolveCollision(a, b, 100);
        Assert.Equal(0, a.Pos.X);
        Assert.Equal(200, b.Pos.X);
    }

    [Fact]
    public void Handles_coincident_positions_without_dividing_by_zero()
    {
        var a = At(1, 5, 5);
        var b = At(2, 5, 5);
        Movement.ResolveCollision(a, b, 100);
        Assert.Equal(-95, a.Pos.X);
        Assert.Equal(105, b.Pos.X);
    }

    [Fact]
    public void Collision_push_truncates_toward_zero_on_negative_deltas()
    {
        // dx = -70, dz = -30: dist2 = 5800, dist = 76, overlap = 124; push = trunc(-8680/152) = -57, trunc(-3720/152) = -24.
        var a = At(1, 0, 0);
        var b = At(2, -70, -30);
        Movement.ResolveCollision(a, b, 100);
        Assert.Equal(new Vec3(57, 0, 24), a.Pos);
        Assert.Equal(new Vec3(-127, 0, -54), b.Pos);
    }

    [Fact]
    public void Clamps_position_into_the_bounds()
    {
        var a = At(1, -500, 5000);
        Movement.ClampToBounds(a, new Bounds(0, 1000, 0, 1000));
        Assert.Equal(new Vec3(0, 0, 1000), a.Pos);
    }

    [Fact]
    public void Dash_distance_is_the_d10_placeholder()
    {
        Assert.Equal(3000, Movement.DashDistanceMm);
    }
}
