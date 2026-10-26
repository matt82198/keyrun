using System;
using Keyrun.Sim.Encounter;

namespace Keyrun.Sim.Runs;

/// <summary>Phase 2 of <see cref="RunPipeline"/>: the encounter step on the pre-combat state.</summary>
internal static class EncounterPrePhase
{
    /// <summary>
    /// Runs <see cref="EncounterStep.Step"/> on the pre-combat state: enemy casts, telegraphs, break-bar drains off this
    /// tick's caller commands, pull advance.
    /// </summary>
    /// <param name="c">The step context.</param>
    public static void Step(RunStepCtx c)
    {
        var run = c.Before;
        var content = run.Content;
        var wiring = new BreakBarWiring(
            content.BreakBarSources ?? Array.Empty<BreakBarSource>(),
            c.Commands,
            c.Tables.Tags);
        var enc = EncounterStep.Step(
            run.State,
            run.Encounter,
            run.Dungeon,
            run.ActiveScripts ?? content.EnemyScripts,
            c.Emit,
            wiring,
            c.Tables.ScriptAbilities,
            run.Content.Effects);
        c.Pre = enc;
        c.Encounter = enc.Encounter;
        // The encounter step changes no actor; it stamps the landed telegraph poses (TELEGRAPH-AIM), which combat must read.
        c.State = enc.State;
    }
}
