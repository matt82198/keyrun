using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;

namespace Keyrun.Sim.Runs;

/// <summary>Phase 15 of <see cref="RunPipeline"/>: run-end detection and <c>run_end</c>.</summary>
internal static class RunEndPhase
{
    /// <summary>Checks for the run's end and emits <c>run_end</c> the step it first ends.</summary>
    /// <param name="c">The step context.</param>
    public static void Step(RunStepCtx c)
    {
        var run = c.Before;
        var opts = c.Opts;
        var nextState = c.State;
        var runEnd = RunWiring.CheckRunEnd(
            run.RunEnd, nextState, c.Encounter, run.Dungeon, nextState.Tick, opts?.AbandonRequested ?? false, c.Emit, opts?.OpenSeats,
            release: run.Content.Layout is not null);
        if (runEnd.Result is RunResult result && run.RunEnd.Result is null)
        {
            if (result == RunResult.Depleted)
            {
                c.Encounter = c.Encounter with { Depleted = true };
            }
            c.Emit(new SimEvent(nextState.Tick, "run_end", ("result", JsValue.Str(result.ToJs()))));
        }
        c.RunEnd = runEnd;
    }
}
