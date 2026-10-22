using System.Linq;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Nav;

namespace Keyrun.Sim.Runs;

/// <summary>Phase 3 of <see cref="RunPipeline"/>: caller then enemy commands through <see cref="Tick.Step"/>.</summary>
internal static class CombatPhase
{
    /// <summary>Steps combat with the run's cached rules (armed when the boss is).</summary>
    /// <param name="c">The step context.</param>
    public static void Step(RunStepCtx c)
    {
        var run = c.Before;
        var enc = c.Pre;
        var layout = run.Content.Layout;

        // The run's Clause rides in its rules for exactly this step (clause-hooks.ts withEncounterRules); LOCK-TELEGRAPH
        // swaps an armed boss's combat abilities in (RunTables.ArmedRules). Both are built once per content, not per tick.
        var rules = run.Encounter.Boss is { } armedBoss
            ? c.Tables.ArmedRules(run.Encounter.Rules, armedBoss.Config)
            : c.Tables.Rules(run.Encounter.Rules);
        // R-PATH-1: on a layout run an enemy chase walks its grid path to its target, not a straight line into a wall.
        var enemyCommands = layout is null ? enc.Commands : NavFollow.RouteChase(run.State, enc.Commands, NavGrid.Of(layout));
        var merged = c.Commands.Concat(enemyCommands).ToList();
        // BIBLE.md:156 Tide: floor patches drift with the telegraphs, before combat reads them this tick.
        var stepped = Tick.Step(Clauses.DriftFloorWindows(run.State, run.Encounter.Rules), merged, rules);
        c.Events.AddRange(stepped.Events);
        c.State = stepped.State;
    }
}
