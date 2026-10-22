namespace Keyrun.Sim.Runs;

/// <summary>Phase 1 of <see cref="RunPipeline"/>: a finished run only advances its clock.</summary>
internal static class RunEndFreezePhase
{
    /// <summary>Halts the step with the clock advanced once the run has ended.</summary>
    /// <param name="c">The step context.</param>
    public static void Step(RunStepCtx c)
    {
        var run = c.Before;
        // RUN-END-FREEZE: once run_end has fired the run is over. No rule, script, cast or death resolves any more, so
        // an add left standing (a Sundered Twin when the boss dies) cannot take damage or die after the run record is
        // written. The clock still advances exactly as Tick.Step advances it, so a host that keeps stepping stays in step.
        if (run.RunEnd.Result is not null)
        {
            var frozen = run.State.Clone();
            frozen.Tick += 1;
            frozen.RunTimerTicks += 1;
            c.Halted = run with { State = frozen };
        }
    }
}
