using Keyrun.Sim.Encounter;

namespace Keyrun.Sim.Runs;

/// <summary>Phase 10 of <see cref="RunPipeline"/>: the echo spawns this tick's events call for.</summary>
internal static class EchoSpawnsPhase
{
    /// <summary>Spawns the echoes (reads the tick's events so far) and merges them into the state.</summary>
    /// <param name="c">The step context.</param>
    public static void Step(RunStepCtx c)
    {
        var echo = EchoSpawns.Step(c.Encounter, c.State, c.Events, c.State.Tick, c.Emit);
        c.Encounter = echo.Encounter;
        c.State = RunWiring.MergeActors(c.State, echo.Spawned);
    }
}
