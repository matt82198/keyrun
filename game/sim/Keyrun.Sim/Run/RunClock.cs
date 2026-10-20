using System;

namespace Keyrun.Sim.Runs;

/// <summary>
/// The run time a host shows (the HUD run timer, "+2  2:55 / par 12:00"). While the run is live it is
/// <c>State.RunTimerTicks</c>. Once <c>run_end</c> has fired it stops at the run time of the end tick
/// (<see cref="RunEndState.ResultTick"/>), the same moment the run-end panel and the key result record use, even though
/// <see cref="RunRoot.StepRun"/> keeps advancing the state's clock so a host that keeps stepping stays in step.
/// </summary>
public static class RunClock
{
    /// <summary>The run time to show, in ticks.</summary>
    /// <param name="run">The run.</param>
    /// <returns>Ticks of run time; frozen at the end tick once the run has ended.</returns>
    public static int ShownTicks(Run run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var live = run.State.RunTimerTicks;
        if (run.RunEnd.ResultTick is not { } endTick)
        {
            return live;
        }
        // Every StepRun after run_end advances Tick and RunTimerTicks by exactly one each, so the ticks stepped since
        // the end are Tick - ResultTick; take them back off the run clock.
        return Math.Max(0, live - Math.Max(0, run.State.Tick - endTick));
    }
}
