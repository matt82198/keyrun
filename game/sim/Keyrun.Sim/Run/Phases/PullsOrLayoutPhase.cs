using System.Linq;
using Keyrun.Sim.Encounter;

namespace Keyrun.Sim.Runs;

/// <summary>
/// Phase 9 of <see cref="RunPipeline"/>: without a layout the next pull spawns (with <c>pull_start</c>) when the index
/// advanced; with one the layout rules run (<see cref="RunRoot.StepLayout"/>) and newly engaged groups go on their clock.
/// </summary>
internal static class PullsOrLayoutPhase
{
    /// <summary>Spawns the next pull, or steps the layout and re-clocks the groups it engaged.</summary>
    /// <param name="c">The step context.</param>
    public static void Step(RunStepCtx c)
    {
        var run = c.Before;
        var content = run.Content;
        var layout = content.Layout;
        var nextState = c.State;
        var nextEncounter = c.Encounter;
        var activeScripts = c.ActiveScripts;
        if (layout is null)
        {
            if (nextEncounter.PullIndex != run.Encounter.PullIndex)
            {
                nextState = RunWiring.MergeActors(nextState, Pulls.SpawnPull(run.Dungeon, nextEncounter.PullIndex, nextState.Tick, c.Emit));
                if (nextEncounter.PullIndex < run.Dungeon.Pulls.Count)
                {
                    nextEncounter = nextEncounter with { Engaged = nextEncounter.Engaged.Append(run.Dungeon.Pulls[nextEncounter.PullIndex].Id).ToList() };
                }
            }
        }
        else
        {
            var before = nextEncounter.Engaged.Count;
            (nextState, nextEncounter) = RunRoot.StepLayout(layout, run, nextState, nextEncounter, c.Emit, c.Opts?.OpenSeats);
            var newly = nextEncounter.Engaged.Skip(before).ToList();
            if (newly.Count > 0)
            {
                // A pull or a pack (PATH-PACKS) runs its scripts on its own clock from its engage tick.
                var groups = Aggro.GroupsOf(run.Dungeon, nextState.Tick, content.Packs, content.Layout, content.Patrols).Where(g => newly.Contains(g.Id)).Select(g => g.ActorIds).ToList();
                activeScripts = RunRoot.OnEngageClock(activeScripts ?? content.EnemyScripts, groups, nextState.Tick, content.Abilities);
            }
        }
        c.State = nextState;
        c.Encounter = nextEncounter;
        c.ActiveScripts = activeScripts;
    }
}
