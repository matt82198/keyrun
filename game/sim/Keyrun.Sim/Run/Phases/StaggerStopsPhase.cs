namespace Keyrun.Sim.Runs;

/// <summary>Phase 7 of <see cref="RunPipeline"/>: the stagger stun ends the staggered bar actors' casts.</summary>
internal static class StaggerStopsPhase
{
    /// <summary>Stops the casts of the actors the armed boss's bar staggered this tick.</summary>
    /// <param name="c">The step context.</param>
    public static void Step(RunStepCtx c)
    {
        // f-8b3fff18 (STUN-STOPS-ENEMIES): the stagger stun lands now, so a staggered bar actor's cast that the
        // stop above left (uninterruptible, or no host stop ids) ends here, before the telegraph check reads the casts.
        c.State = RunWiring.StopCasts(c.State, c.Pre.Staggered, c.Emit);
    }
}
