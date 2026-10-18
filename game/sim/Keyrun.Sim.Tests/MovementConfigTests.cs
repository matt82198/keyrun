using System;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// <see cref="MovementConfig"/> is the content-supplied walk speed, actor radius and arena bounds
/// (sim/combat/rules.ts RulesConfig.baseSpeedMmPerTick + run-setup.ts ACTOR_RADIUS_MM and
/// DUNGEON_BOUNDS). The config overloads of <see cref="Movement"/> must read every value from the
/// config: each case uses a value no caller hardcodes, so a constant in its place goes red.
/// </summary>
public sealed class MovementConfigTests
{
    private static readonly MovementConfig Config = new(BaseSpeedMmPerTick: 250, ActorRadiusMm: 300, Bounds: new Bounds(-4000, 18000, -6000, 9000));

    private static ActorState At(long x, long z, long? speed = null) => new()
    {
        Id = 1, Kind = ActorKind.Player, ClassId = "warden", Role = ActorRole.Tank, Pos = new Vec3(x, 0, z), MaxHealth = 1, Health = 1,
        SpeedMmPerTick = speed,
    };

    [Fact]
    public void Move_steps_at_the_config_base_speed()
    {
        var a = At(0, 0);
        Movement.ApplyMovement(a, new Dir2(0, 1000), Config, 0, 256);
        Assert.Equal(new Vec3(0, 0, 250), a.Pos);
        Assert.Equal(256, a.Yaw);
    }

    [Fact]
    public void Move_applies_the_movement_modifier_to_the_config_speed()
    {
        var a = At(0, 0);
        Movement.ApplyMovement(a, new Dir2(1, 0), Config, -2000, 0);
        Assert.Equal(200, a.Pos.X);
    }

    [Fact]
    public void Per_actor_speed_overrides_the_config_base_speed_as_in_rules_ts()
    {
        // rules.ts:114 actor.speedMmPerTick ?? cfg.baseSpeedMmPerTick
        var a = At(0, 0, speed: 90);
        Movement.ApplyMovement(a, new Dir2(1, 0), Config, 0, 0);
        Assert.Equal(90, a.Pos.X);
    }

    [Fact]
    public void Collision_uses_the_config_actor_radius()
    {
        // radius 300: minDist 600, actors 500 apart overlap by 100 and split it 50/50.
        var a = At(0, 0);
        var b = At(500, 0);
        Movement.ResolveCollision(a, b, Config);
        Assert.Equal(-50, a.Pos.X);
        Assert.Equal(550, b.Pos.X);
    }

    [Fact]
    public void Clamp_uses_the_config_bounds_which_are_asymmetric()
    {
        var low = At(-50000, -50000);
        var high = At(50000, 50000);
        Movement.ClampToBounds(low, Config);
        Movement.ClampToBounds(high, Config);
        Assert.Equal(new Vec3(-4000, 0, -6000), low.Pos);
        Assert.Equal(new Vec3(18000, 0, 9000), high.Pos);
    }

    [Theory]
    [InlineData(0, 400, -1, 1, -1, 1)]
    [InlineData(180, 0, -1, 1, -1, 1)]
    [InlineData(180, 400, 1, 1, -1, 1)]
    [InlineData(180, 400, -1, 1, 5, -5)]
    public void Config_rejects_non_positive_speed_or_radius_and_empty_bounds(long speed, long radius, long minX, long maxX, long minZ, long maxZ)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MovementConfig(speed, radius, new Bounds(minX, maxX, minZ, maxZ)));
    }
}
