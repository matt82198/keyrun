namespace Keyrun.Sim.Runs;

/// <summary>Phase 14 of <see cref="RunPipeline"/>: run-clock time for player deaths, and the wipe.</summary>
internal static class DeathsAndWipePhase
{
    /// <summary>Applies this tick's player deaths (from the events) and the wipe to the encounter.</summary>
    /// <param name="c">The step context.</param>
    public static void Step(RunStepCtx c)
    {
        c.Encounter = RunWiring.ApplyDeathsAndWipe(c.State, c.Encounter, c.Events, c.State.Tick, c.Emit, c.Opts?.OpenSeats);
    }
}
