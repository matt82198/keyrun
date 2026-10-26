using System;
using System.Collections.Generic;
using Keyrun.Host;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Xunit;
using static Keyrun.Sim.Tests.CombatCastTestKit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// S5 a10186c8: a moving character's body snapped its yaw by a large angle in one render step (the head glitch Matt
/// reported while moving). The body interpolates the shortest arc from the yaw <see cref="ActorFacing.RenderYaw"/> gave
/// on the previous tick to the yaw it gives on the current tick (ActorView.cs, YawRadians). A target that flips 180
/// degrees in one tick therefore turned the whole body in one tick. The per-tick cap belongs to the render interpolation,
/// which is private and Godot-bound in ActorView, so these cases pin the yaw pair that interpolation consumes.
/// </summary>
public class ActorFacingTurnCapTests
{
    /// <summary>The most the drawn body may turn in one render tick: 90 degrees, which is 256 of 1024 yaw units.</summary>
    private const long TurnCapPerTick = 256L;

    /// <summary>A 10 degree turn is 28.4 yaw units; the band allows for the integer atan rounding.</summary>
    private const long SmallTurnMin = 26L;
    private const long SmallTurnMax = 30L;

    private const int CasterId = 1;
    private const int TargetId = 2;

    private static readonly IReadOnlyDictionary<string, AbilityDef> Abilities = new Dictionary<string, AbilityDef>();

    [Fact]
    public void ABodyTurnsNoMoreThanTheCapInOneTickWhenItsTargetFlipsBehindIt()
    {
        // Target straight ahead at +z (yaw 256), then straight behind at -z (yaw 768) on the next tick.
        var (from, to) = RenderPair(new Vec3(0, 0, 1000), new Vec3(0, 0, -1000));

        var turn = Math.Abs(Fixed.AngleDiff(from, to));
        Assert.True(
            turn <= TurnCapPerTick,
            $"the body turns {turn} yaw units in one tick (from {from} to {to}); the cap is {TurnCapPerTick} (90 degrees)");
    }

    [Fact]
    public void ASmallTenDegreeTargetShiftIsReachedFullyInOneTick()
    {
        // Anti-vacuity: the cap must not swallow an ordinary turn. The target moves about 10 degrees off +z.
        var (from, to) = RenderPair(new Vec3(0, 0, 1000), new Vec3(174, 0, 985));

        var turn = Math.Abs(Fixed.AngleDiff(from, to));
        Assert.True(
            turn >= SmallTurnMin && turn <= SmallTurnMax,
            $"a 10 degree shift turned {turn} yaw units (from {from} to {to}); expected about 28 units, fully reached");
        Assert.True(turn <= TurnCapPerTick, "a small turn must sit under the per-tick cap");
    }

    /// <summary>
    /// The yaw <see cref="ActorFacing.RenderYaw"/> draws the caster with on the previous tick (target at
    /// <paramref name="targetBefore"/>) and on the current tick (target at <paramref name="targetAfter"/>).
    /// </summary>
    private static (int From, int To) RenderPair(Vec3 targetBefore, Vec3 targetAfter)
    {
        var caster = Make(CasterId, ActorKind.Enemy, "caster", maxHealth: 1000, maxResource: 100, yaw: 768);
        var target = Make(TargetId, ActorKind.Player, "target", z: 1000, maxHealth: 1000, yaw: 768);
        var state = At(0, caster, target);
        caster.Pos = new Vec3(0, 0, 0);
        caster.TargetId = TargetId;

        target.Pos = targetBefore;
        var from = ActorFacing.RenderYaw(state, caster, Abilities, null, false);
        target.Pos = targetAfter;
        var rendered = ActorFacing.RenderYaw(state, caster, Abilities, null, false);
        var to = ActorFacing.TurnToward(from, rendered, (int)TurnCapPerTick);
        return (from, to);
    }
}
