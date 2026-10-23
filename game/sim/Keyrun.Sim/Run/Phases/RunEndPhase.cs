using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;

namespace Keyrun.Sim.Runs;

/// <summary>Phase 15 of <see cref="RunPipeline"/>: run-end detection, <c>run_end</c> and the completion rolls.</summary>
internal static class RunEndPhase
{
    /// <summary>Checks for the run's end and emits <c>run_end</c> the step it first ends, then one <c>completion_roll</c> per seat.</summary>
    /// <param name="c">The step context.</param>
    public static void Step(RunStepCtx c)
    {
        var run = c.Before;
        var opts = c.Opts;
        var nextState = c.State;
        var runEnd = RunWiring.CheckRunEnd(
            run.RunEnd, nextState, c.Encounter, run.Dungeon, nextState.Tick, opts?.AbandonRequested ?? false, c.Emit, opts?.OpenSeats,
            release: run.Content.Layout is not null);
        if (runEnd.Result is RunResult result && run.RunEnd.Result is null)
        {
            if (result == RunResult.Depleted)
            {
                c.Encounter = c.Encounter with { Depleted = true };
            }
            c.Emit(new SimEvent(nextState.Tick, "run_end", ("result", JsValue.Str(result.ToJs()))));
            EmitCompletionRolls(c, result, nextState, run.Content.Completion);
        }
        c.RunEnd = runEnd;
    }

    // R-ROLL-1: one completion_roll per party seat (the player actors, in actor id order) at the run_end tick. The
    // reward seed is the run's (EndOfRun.RewardSeedOf: seed x 1000003 + the end tick) unless the host pins it with
    // StepRunOpts.RewardSeedOverride (R-K4-16); the key level is the encounter's.
    private static void EmitCompletionRolls(RunStepCtx c, RunResult result, SimState state, CompletionRollTable? table)
    {
        var rewardSeed = c.Opts?.RewardSeedOverride ?? EndOfRun.RewardSeedOf(state.Seed, state.Tick);
        state.RewardSeed = rewardSeed;
        // R-ROLL-1 (b)(iii): each seat draws over its own class's facet pool (CompletionFacets), never the placeholders.
        foreach (var roll in CompletionRoll.ForSeats(state, rewardSeed, c.Encounter.KeyLevel, result, table))
        {
            var fields = new List<(string Key, JsValue Value)>
            {
                ("seat", JsValue.Int(roll.Seat)),
                ("outcome", JsValue.Str(roll.Item is null ? "nothing" : "item")),
            };
            if (roll.Item is { } item)
            {
                fields.Add(("itemId", JsValue.Str(item.Id)));
            }
            c.Emit(new SimEvent(state.Tick, "completion_roll", fields.ToArray()));
        }
    }
}
