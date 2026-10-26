using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
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
        // L5 caster-los: a caster out of sight of its target (a layout obstacle between) walks that path until the ray is clear.
        var chase = layout is { Obstacles.Count: > 0 }
            ? Chase.SeekSight(run.State, run.ActiveScripts ?? run.Content.EnemyScripts, c.Tables.ScriptAbilities, (a, b) => CastSight.Clear(layout.Obstacles, a, b), enc.Commands)
            : enc.Commands;
        var enemyCommands = layout is null ? enc.Commands : NavFollow.RouteChase(run.State, chase, NavGrid.Of(layout));
        if (layout is not null && Leash.EvadeMoves(run.State, NavGrid.Of(layout)) is { Count: > 0 } evades)
        {
            // AGGRO-CHASE leash: a pack that gave up the chase walks its grid path home.
            enemyCommands = enemyCommands.Concat(evades).ToList();
        }
        var merged = c.Commands.Concat(enemyCommands).ToList();
        // BIBLE.md:156 Tide: floor patches drift with the telegraphs, before combat reads them this tick.
        var stepped = Tick.Step(Clauses.DriftFloorWindows(StopOpenSeatCastsEndingNow(c, run.State), run.Encounter.Rules), merged, rules);
        c.Events.AddRange(stepped.Events);
        c.State = stepped.State;
    }

    /// <summary>
    /// The leave tick (WoW: a player who leaves is gone, and their cast does not land). <see cref="Rules"/> completes
    /// casts before the command loop, so an open seat's cast that ends on this tick would resolve before the backfill's
    /// stopcast runs. Such a cast is stopped here, on a clone, before the tick steps. An open seat whose cast ends later
    /// is left to the backfill's stopcast command, and a tick with no open seat is unchanged.
    /// </summary>
    /// <param name="c">The step context (its <see cref="RunStepCtx.Opts"/> carry the open seats).</param>
    /// <param name="state">The run's state at the start of the tick (read only).</param>
    /// <returns><paramref name="state"/> itself when no open seat's cast ends on this tick.</returns>
    private static SimState StopOpenSeatCastsEndingNow(RunStepCtx c, SimState state)
    {
        if (c.Opts?.OpenSeats is not { Count: > 0 } openSeats)
        {
            return state;
        }
        var stopped = new List<ActorState>();
        foreach (var id in openSeats)
        {
            if (state.Actors.TryGetValue(id, out var actor) && actor.CastId is not null && actor.CastEndsTick == state.Tick)
            {
                var copy = actor.Clone();
                Casting.Stopcast(copy, c.Emit, state.Tick);
                stopped.Add(copy);
            }
        }
        return RunWiring.MergeActors(state, stopped);
    }
}
