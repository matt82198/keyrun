using System;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// RUN-TIMER-STOP: the run timer the HUD shows (<see cref="RunClock.ShownTicks"/>, read by Hud/TimerSlack.cs
/// SlackReading.Of) must stop when the run ends. <see cref="RunRoot.StepRun"/> keeps advancing
/// <c>State.RunTimerTicks</c> after <c>run_end</c> (RUN-END-FREEZE), so a HUD that reads the live clock kept counting
/// on the run-end screen (2:55 then 2:57). Everything runs through <see cref="RunRoot.StepRun"/> on the boss run.
/// </summary>
public sealed class RunClockTests
{
    /// <summary>Steps a few ticks, kills the boss, and steps once so the run ends timed.</summary>
    private static Run EndedRun()
    {
        var (run, _) = BossTestKit.StepUntil(BossTestKit.BuildRun(), 3);
        run.State.Actors[BossTestKit.Boss].Alive = false;
        run.State.Actors[BossTestKit.Boss].Health = 0;
        var stepped = RunRoot.StepRun(run, Array.Empty<Command>());
        Assert.Equal(RunResult.Timed, stepped.Run.RunEnd.Result);
        Assert.Contains(stepped.Events, e => e.Type == "run_end");
        return stepped.Run;
    }

    [Fact(DisplayName = "After run_end, stepping 600 more ticks does not move the shown run time")]
    public void Shown_time_does_not_move_after_run_end()
    {
        var ended = EndedRun();
        var atEnd = RunClock.ShownTicks(ended);

        var (after, _) = BossTestKit.StepUntil(ended, ended.State.Tick + 600);

        Assert.Equal(ended.State.Tick + 600, after.State.Tick);
        Assert.Equal(ended.State.RunTimerTicks + 600, after.State.RunTimerTicks);
        Assert.Equal(atEnd, RunClock.ShownTicks(after));
    }

    [Fact(DisplayName = "After run_end the shown run time equals the recorded end tick the run-end panel uses")]
    public void Shown_time_equals_the_recorded_run_time()
    {
        var ended = EndedRun();
        var endTick = ended.RunEnd.ResultTick!.Value;

        var (after, _) = BossTestKit.StepUntil(ended, ended.State.Tick + 600);

        Assert.Equal(endTick, after.RunEnd.ResultTick);
        Assert.Equal(endTick, RunClock.ShownTicks(after));
    }

    [Fact(DisplayName = "After run_end the shown time keeps any offset of the run clock from the tick (frozen, not ResultTick)")]
    public void Shown_time_is_the_run_clock_at_the_end_tick()
    {
        var ended = EndedRun();
        ended.State.RunTimerTicks += 40;
        var atEnd = ended.State.RunTimerTicks;

        var (after, _) = BossTestKit.StepUntil(ended, ended.State.Tick + 600);

        Assert.Equal(atEnd, RunClock.ShownTicks(after));
    }

    [Fact(DisplayName = "Control: while the run is live the shown time is the live run clock and advances")]
    public void Shown_time_follows_the_live_clock_while_the_run_is_live()
    {
        var (run, _) = BossTestKit.StepUntil(BossTestKit.BuildRun(), 3);
        var (later, _) = BossTestKit.StepUntil(run, 203);

        Assert.Null(later.RunEnd.Result);
        Assert.Equal(run.State.RunTimerTicks, RunClock.ShownTicks(run));
        Assert.Equal(later.State.RunTimerTicks, RunClock.ShownTicks(later));
        Assert.Equal(RunClock.ShownTicks(run) + 200, RunClock.ShownTicks(later));
    }
}
