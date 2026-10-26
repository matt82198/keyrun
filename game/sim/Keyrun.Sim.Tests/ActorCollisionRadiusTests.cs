using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// R-BOSSROOM-1 (b): collision radius is per actor. An enemy def may carry an optional radiusMm
/// (hollowWarden.boss = 900) that the sim stores on the spawned actor as <c>ActorState.RadiusMm</c> (null = the run default,
/// movement.radiusMm = 400, run.json:4). A pair separates to the SUM of the two radii, so the boss body no longer
/// overlaps a melee player. Two default actors keep today's 2 x 400 = 800 mm.
/// </summary>
public sealed class ActorCollisionRadiusTests
{
    private static readonly MovementConfig Config = new(BaseSpeedMmPerTick: 250, ActorRadiusMm: 400, Bounds: new Bounds(-40000, 40000, -40000, 40000));

    private static ActorState At(int id, ActorKind kind, long x, long? radiusMm = null) => new()
    {
        Id = id, Kind = kind, ClassId = "x", Role = ActorRole.None, Pos = new Vec3(x, 0, 0), MaxHealth = 1, Health = 1,
        RadiusMm = radiusMm,
    };

    private static long Gap(ActorState a, ActorState b) => System.Math.Abs(b.Pos.X - a.Pos.X);

    [Fact]
    public void A_900_mm_boss_and_a_400_mm_player_end_at_least_1300_mm_apart_symmetric_resolve()
    {
        var boss = At(1, ActorKind.Enemy, 0, radiusMm: 900);
        var player = At(2, ActorKind.Player, 500);
        Movement.ResolveCollision(boss, player, Config);
        Assert.True(Gap(boss, player) >= 1300, "gap " + Gap(boss, player));
    }

    [Fact]
    public void A_900_mm_boss_and_a_400_mm_player_end_at_least_1300_mm_apart_when_only_the_boss_moves()
    {
        var boss = At(1, ActorKind.Enemy, 0, radiusMm: 900);
        var player = At(2, ActorKind.Player, 500);
        Movement.ResolveCollisionMoverOnly(player, boss, Config);
        Assert.True(Gap(boss, player) >= 1300, "gap " + Gap(boss, player));
        Assert.Equal(500, player.Pos.X);
    }

    [Fact]
    public void Two_default_actors_stay_800_mm_apart()
    {
        var a = At(1, ActorKind.Enemy, 0);
        var b = At(2, ActorKind.Player, 500);
        Movement.ResolveCollision(a, b, Config);
        Assert.Equal(800, Gap(a, b));
    }

    [Fact]
    public void A_boss_already_clear_of_a_player_is_not_moved()
    {
        var boss = At(1, ActorKind.Enemy, 0, radiusMm: 900);
        var player = At(2, ActorKind.Player, 1300);
        Movement.ResolveCollision(boss, player, Config);
        Assert.Equal(0, boss.Pos.X);
        Assert.Equal(1300, player.Pos.X);
    }
}
