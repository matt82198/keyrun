using System;
using Keyrun.Host;
using Keyrun.Sim.Core;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// The carried turn (ActorFacing.CarryStep, used by ActorView.Place): a body keeps the yaw it was last drawn at and turns at
/// most the cap per sim tick toward the raw target. A 180 degree target flip therefore turns over several ticks, and each
/// tick interpolates from the previous tick's drawn yaw, never from a fresh raw yaw that would snap the second tick.
/// </summary>
public class ActorFacingTurnCarryTests
{
    /// <summary>The per-tick turn cap: 90 degrees, 256 of 1024 yaw units.</summary>
    private const int Cap = 256;

    [Fact]
    public void AFlipHeldForThreeTicksTurnsOverTwoTicksWithoutSnapping()
    {
        // Carried at 256 (facing +z), then the target sits at 768 (behind) for three ticks.
        int? carried = 256;
        var drawn = new (int From, int To)[3];
        for (var tick = 0; tick < 3; tick++)
        {
            drawn[tick] = ActorFacing.CarryStep(carried, 768, Cap);
            carried = drawn[tick].To;
        }

        Assert.Equal((256, 512), drawn[0]);
        Assert.Equal((512, 768), drawn[1]);
        Assert.Equal((768, 768), drawn[2]);
    }

    [Fact]
    public void EveryCarriedStepIsWithinTheCapAndStartsFromThePreviousDrawnYaw()
    {
        int? carried = 256;
        var previousTo = 256;
        for (var tick = 0; tick < 6; tick++)
        {
            var (from, to) = ActorFacing.CarryStep(carried, 768, Cap);

            Assert.Equal(previousTo, from);
            Assert.True(
                Math.Abs(Fixed.AngleDiff(from, to)) <= Cap,
                $"tick {tick} turns {Fixed.AngleDiff(from, to)} yaw units (from {from} to {to}); the cap is {Cap}");
            previousTo = to;
            carried = to;
        }
        Assert.Equal(768, previousTo);
    }

    [Fact]
    public void TheFirstTickStartsFacingTheRawTargetAndNeverTurnsFromZero()
    {
        var (from, to) = ActorFacing.CarryStep(null, 768, Cap);

        Assert.Equal(768, from);
        Assert.Equal(768, to);
    }

    [Fact]
    public void ASmallTurnIsReachedFullyInOneTick()
    {
        var (from, to) = ActorFacing.CarryStep(100, 110, Cap);

        Assert.Equal(100, from);
        Assert.Equal(110, to);
    }

    [Fact]
    public void TheCarryTurnsAcrossTheZeroYawSeamByTheShortArc()
    {
        // 1000 to 20 is 44 units forward across the seam, well under the cap, so it lands fully on 20.
        var (from, to) = ActorFacing.CarryStep(1000, 20, Cap);

        Assert.Equal(1000, from);
        Assert.Equal(20, to);
    }
}
