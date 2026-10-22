namespace Keyrun.Sim.Runs;

/// <summary>Phase 4 of <see cref="RunPipeline"/>: the Break bars' onEmpty effect patches.</summary>
internal static class BreakBarPatchesPhase
{
    /// <summary>Applies the encounter step's break-bar patches to the post-combat state.</summary>
    /// <param name="c">The step context.</param>
    public static void Step(RunStepCtx c)
    {
        c.State = RunWiring.ApplyBreakBarPatches(c.State, c.Pre.EffectPatches, c.Before.Content.Effects);
    }
}
