using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// S5 capture 4135f4fc (lanes/s5-capture-4135f4fc.txt): a moving actor flips Move and Idle every time the sim steps its
/// position. The sim moves a body once every 3 ticks, so the interpolated speed reads zero on the ticks between steps and
/// the clip choice (<see cref="AnimRule.Decide"/>, Moving = speed above <c>ActorAnimator.MoveSpeedMps</c>) drops to Idle.
/// Ruling 1 (event seq 376): an actor whose position changed within the last N ticks stays on its Move clip, with N picked
/// from the sim step cadence and at least 3. <see cref="MoveHold"/> is the pure seam that carries that memory; the view
/// feeds it the tick of the last position change, and the clip rule stays stateless.
/// </summary>
public sealed class MoveClipHysteresisTests
{
    // The sim's position cadence in the capture: a step every 3 ticks.
    private const int StepEveryTicks = 3;

    // Ticks of walking to simulate (a few steps in, well past the first hold window).
    private const int WalkTicks = 60;

    private static AnimClip ClipWhileMoving(bool moving) =>
        AnimRule.Decide(new AnimFacts(
            Alive: true, NewTick: true, Moving: moving, Casting: false, Acted: false, Hurt: false,
            Staggered: false, Playing: AnimClip.None)).Clip;

    [Fact]
    public void HoldWindowIsAtLeastTheStepCadence()
    {
        // Ruling 1: N is at least 3 and must bridge the 3-tick gap between two position steps.
        Assert.True(
            MoveHold.HoldTicks >= StepEveryTicks,
            $"MoveHold.HoldTicks is {MoveHold.HoldTicks}; the hold must cover the {StepEveryTicks}-tick step cadence");
    }

    [Fact]
    public void AWalkingActorStaysOnItsMoveClipBetweenPositionSteps()
    {
        // Position changes on ticks 0, 3, 6, ...; the speed reads zero on the others. The hold carries Moving across them.
        var lastMoved = -1;
        for (var tick = 0; tick < WalkTicks; tick++)
        {
            if (tick % StepEveryTicks == 0)
            {
                lastMoved = tick;
            }
            var moving = MoveHold.Holds(tick, lastMoved);
            Assert.True(
                moving,
                $"tick {tick}: the actor moved at tick {lastMoved} but the hold dropped it to idle");
            Assert.Equal(AnimClip.Move, ClipWhileMoving(moving));
        }
    }

    [Fact]
    public void NoIdleClipAppearsBetweenStepsOfAWalk()
    {
        // The capture's count was 95 Move/Idle flips over one walk. With the hold in place there are none in the walk.
        var lastMoved = -1;
        var previous = AnimClip.None;
        var flips = 0;
        for (var tick = 0; tick < WalkTicks; tick++)
        {
            if (tick % StepEveryTicks == 0)
            {
                lastMoved = tick;
            }
            var clip = ClipWhileMoving(MoveHold.Holds(tick, lastMoved));
            if (previous != AnimClip.None && clip != previous)
            {
                flips++;
            }
            previous = clip;
        }
        Assert.Equal(0, flips);
    }

    [Fact]
    public void AnActorThatStopsGoesIdleOnceTheHoldRunsOut()
    {
        // Anti-vacuity: the hold must not pin a stopped body on Move. Last step at tick 10, then no more steps.
        const int lastMoved = 10;
        for (var tick = lastMoved; tick < lastMoved + MoveHold.HoldTicks; tick++)
        {
            Assert.True(MoveHold.Holds(tick, lastMoved), $"tick {tick} is still inside the hold window");
        }
        Assert.False(
            MoveHold.Holds(lastMoved + MoveHold.HoldTicks, lastMoved),
            "a body that has not moved for the full hold window must fall back to Idle");
    }

    [Fact]
    public void AnActorThatNeverMovedIsNotHeld()
    {
        // No position change on record (-1): the hold never reports Moving.
        Assert.False(MoveHold.Holds(0, -1));
        Assert.Equal(AnimClip.Idle, ClipWhileMoving(MoveHold.Holds(0, -1)));
    }
}
