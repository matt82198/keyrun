using Keyrun.Sim.Encounter;

namespace Keyrun.Sim.Runs;

/// <summary>Phase 8 of <see cref="RunPipeline"/>: floor telegraphs whose cast did not land are cancelled.</summary>
internal static class TelegraphCancelPhase
{
    /// <summary>Cancels every telegraph whose cast this tick's combat kicked, stopped or replaced.</summary>
    /// <param name="c">The step context.</param>
    public static void Step(RunStepCtx c)
    {
        var run = c.Before;
        // TELEGRAPH-CANCEL (BIBLE s3): a floor telegraph whose cast this tick's combat kicked, stopped or replaced is
        // cancelled on this tick and never resolves (no telegraph_hit for a hit that cannot come).
        var cancel = Telegraphs.CancelUnlanded(c.State, c.Encounter.Telegraphs, run.State.Tick, c.Emit, run.Content.Abilities);
        c.Encounter = cancel.Cancelled.Count == 0 ? c.Encounter : c.Encounter with { Telegraphs = cancel.Telegraphs };
    }
}
