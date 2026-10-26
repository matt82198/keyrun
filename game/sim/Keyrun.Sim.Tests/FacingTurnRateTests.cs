using System;
using Keyrun.Host;
using Keyrun.Sim.Core;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// S5 capture 4135f4fc (lanes/s5-capture-4135f4fc.txt): the warden's sim yaw reversed 174 degrees between ticks 1593 and
/// 1594 while it was moving (yaw 2 to 497). Ruling 2 (event seq 376): facing turns at a capped rate per frame, never more
/// than 90 degrees in one tick. The drawn yaw is <see cref="ActorFacing.CarryStep"/> (ActorView.Place), so these cases pin
/// the per-tick change of that carried yaw for the captured 174 degree target change.
/// </summary>
public class FacingTurnRateTests
{
    /// <summary>The per-tick turn cap: 90 degrees, 256 of 1024 yaw units.</summary>
    private const int TurnCapPerTick = 256;

    /// <summary>The captured sim yaw before the reversal (tick 1593).</summary>
    private const int YawBefore = 2;

    /// <summary>The captured sim yaw after the reversal (tick 1594): 174 degrees away, in yaw units (495).</summary>
    private const int YawAfter = 497;

    [Fact]
    public void AWarden174DegreeReversalTurnsNoMoreThanTheCapInOneTick()
    {
        // Carried at the tick-1593 yaw, then the raw target jumps to the tick-1594 yaw.
        var (from, to) = ActorFacing.CarryStep(YawBefore, YawAfter, TurnCapPerTick);

        var turn = Math.Abs(Fixed.AngleDiff(from, to));
        Assert.True(
            turn <= TurnCapPerTick,
            $"the drawn yaw turns {turn} yaw units in one tick (from {from} to {to}); the cap is {TurnCapPerTick} (90 degrees)");
    }

    [Fact]
    public void AWarden174DegreeReversalIsReachedWithinTheCapsTicks()
    {
        // 495 units of turn at 256 per tick lands on the target in two ticks, each step starting from the last drawn yaw.
        int? carried = YawBefore;
        var ticks = 0;
        while (carried != YawAfter && ticks < 10)
        {
            var (from, to) = ActorFacing.CarryStep(carried, YawAfter, TurnCapPerTick);
            Assert.Equal(carried, from);
            Assert.True(Math.Abs(Fixed.AngleDiff(from, to)) <= TurnCapPerTick, $"tick {ticks} turns past the cap");
            carried = to;
            ticks++;
        }

        Assert.Equal(YawAfter, carried);
        Assert.Equal(2, ticks);
    }

    [Fact]
    public void AMovingWardensSmallTurnIsNotSlowedByTheCap()
    {
        // Anti-vacuity: the cap must not swallow an ordinary steering turn of about 10 degrees.
        var (from, to) = ActorFacing.CarryStep(YawBefore, YawBefore + 28, TurnCapPerTick);

        Assert.Equal(YawBefore, from);
        Assert.Equal(YawBefore + 28, to);
    }
}
