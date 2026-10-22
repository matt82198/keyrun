using Keyrun.Sim.Encounter;

namespace Keyrun.Sim.Runs;

/// <summary>Phase 16 of <see cref="RunPipeline"/>: the Seed loot bonus off this tick's events.</summary>
internal static class SeedLootPhase
{
    /// <summary>Observes the tick's events against the run's Seed loot rule.</summary>
    /// <param name="c">The step context.</param>
    public static void Step(RunStepCtx c)
    {
        var run = c.Before;
        // SEED-LOOT-AWARD: the run root awards the Seed bonus itself (SeedLoot.cs summary: "the run root calls Observe
        // once per tick with that tick's events").
        c.Rewards = run.SeedRule is { } seedRule ? SeedLoot.Observe(run.Rewards, seedRule, c.Events) : run.Rewards;
    }
}
