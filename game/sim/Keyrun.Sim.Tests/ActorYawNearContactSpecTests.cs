using System;
using Keyrun.Host;
using Keyrun.Sim.Core;
using Xunit;
using static Keyrun.Sim.Tests.CombatCastTestKit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// S1 spec for R-VIEW-HEAD-1 residual (keyrun item 11): does the drawn yaw of an actor with a live target swing when
/// the target moves a little near contact? <see cref="ActorFacing.RenderYaw"/> is the rule ActorView draws with. Contact
/// is taken as two 200 mm cylinders touching (400 mm centre to centre) and a closer 200 mm centre spacing as the worst
/// case. Both are ASSUMED here: the shipped actor radius is set per run (Rules.ActorRadiusMm), not pinned in a constant.
/// </summary>
public class ActorYawNearContactSpecTests
{
    // 15 degrees in 1/1024 turn: 1024 * 15 / 360 = 42.67, so 42 units (14.77 degrees) is the bound per tick.
    // Justification: a 50 mm sideways step at 200 mm subtends atan(50/200) = 14.04 degrees (39.9 units), under the bound
    // with margin. A 50 mm step that swung the body more than 15 degrees in one tick is the snap the head-glitch report
    // describes, so the bound fails on that snap and allows a normal chase.
    private const int MaxYawStepUnits = 42;
    private const int CasterId = 1;
    private const int TargetId = 2;

    [Theory]
    [InlineData(200, 50)]
    [InlineData(200, -50)]
    [InlineData(400, 50)]
    [InlineData(400, -50)]
    public void AnAggroedYawDoesNotSnapWhenAContactTargetStepsSideways(long distanceMm, long stepMm)
    {
        var caster = Make(CasterId, ActorKind.Enemy, "caster", maxHealth: 1000, maxResource: 100);
        caster.TargetId = TargetId;
        var target = Make(TargetId, ActorKind.Player, "target", x: 0, z: distanceMm, maxHealth: 1000);
        SimState before = At(0, caster, target);
        int yawBefore = ActorFacing.RenderYaw(before, before.Actors[CasterId], null, null, followsInput: false);

        // One sim tick later: the target has stepped stepMm sideways (along x) and the caster has not moved.
        var casterAfter = Make(CasterId, ActorKind.Enemy, "caster", maxHealth: 1000, maxResource: 100);
        casterAfter.TargetId = TargetId;
        var targetAfter = Make(TargetId, ActorKind.Player, "target", x: stepMm, z: distanceMm, maxHealth: 1000);
        SimState after = At(1, casterAfter, targetAfter);
        int yawAfter = ActorFacing.RenderYaw(after, after.Actors[CasterId], null, null, followsInput: false);

        long step = Math.Abs(Fixed.AngleDiff(yawBefore, yawAfter));
        Assert.True(step > 0, "the yaw did not respond to the step at all, so the bound below would be vacuous");
        Assert.True(
            step <= MaxYawStepUnits,
            $"drawn yaw moved {step} units ({step * 360.0 / 1024:F1} degrees) in one tick with the target {distanceMm} mm away " +
            $"and stepping {stepMm} mm; the bound is {MaxYawStepUnits} units (15 degrees)");
    }
}
