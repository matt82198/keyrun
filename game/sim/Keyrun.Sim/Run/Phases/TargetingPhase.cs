namespace Keyrun.Sim.Runs;

/// <summary>Phase 13 of <see cref="RunPipeline"/>: targeting fixup on the post-release party.</summary>
internal static class TargetingPhase
{
    /// <summary>Resolves every actor's target against the state as it now stands.</summary>
    /// <param name="c">The step context.</param>
    public static void Step(RunStepCtx c)
    {
        RunWiring.FixupTargeting(c.State);
    }
}
