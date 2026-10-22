namespace Keyrun.Sim.Runs;

/// <summary>Phase 4 of <see cref="RunPipeline"/>: the Break bars' onEmpty effect patches.</summary>
internal static class BreakBarPatchesPhase
{
    /// <summary>Applies the encounter step's break-bar patches to the post-combat state.</summary>
    /// <param name="c">The step context.</param>
    public static void Step(RunStepCtx c)
    {
        c.State = RunWiring.ApplyBreakBarPatches(c.State, c.Pre.EffectPatches, c.Before.Content.Effects);
        if (c.Before.Content.CleaveMark is { } cleaveMark)
        {
            // R-BOSS-4: an unanswered Cleave marks every seat it hit other than its target (a stacking Cleaved). In place:
            // this freshly stepped state is the run root's own. A snapshot of the events so far, since Apply emits.
            Encounter.CleaveMark.Apply(c.Before.State, c.State, c.Events.ToArray(), cleaveMark, c.Before.Content.Effects, c.Emit);
        }
    }
}
