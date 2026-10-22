using System.Linq;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Hud;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// FINISH-NEEDS-FORCES (ledger f-8c50a787, WoW Mythic+): the host run carries the content forces goal, and once the boss
/// is dead with Enemy Forces short of 100% the objective line tells the player the run is still open:
/// "Enemy Forces NN% - finish the count". Driven from <see cref="HostRunSetup.Build"/> and <see cref="RunRoot.CreateRun"/>,
/// the same build the autorun uses.
/// </summary>
public sealed class FinishCountObjectiveTests
{
    private static Run CreateRun(ContentRegistry registry)
    {
        var setup = HostRunSetup.Build(registry, "hollow-vault", 2, "warden", withBots: true);
        return RunRoot.CreateRun(1, setup.PartyFor(setup.Dungeon), setup.Dungeon, setup.Content);
    }

    /// <summary>The run with every teaching pull's and the boss's actors dead and the last pull cleared.</summary>
    private static Run BossDeadCriticalPathOnly(Run run)
    {
        var state = run.State.Clone();
        foreach (var spawn in run.Dungeon.Pulls.SelectMany(p => p.Spawns))
        {
            state.Actors[spawn.ActorId].Alive = false;
        }
        return run with { State = state, Encounter = run.Encounter with { PullIndex = run.Dungeon.Pulls.Count } };
    }

    [Fact(DisplayName = "FINISH-NEEDS-FORCES: the host dungeon carries the forces.json goal")]
    public void Host_dungeon_carries_the_forces_goal()
    {
        var registry = ContentLoader.Load();
        var run = CreateRun(registry);

        var goal = Assert.IsType<Keyrun.Sim.Encounter.ForcesGoal>(run.Dungeon.ForcesGoal);
        Assert.Equal(registry.Forces.Required, goal.Required);
        Assert.All(registry.Forces.Values, v => Assert.Equal(v.Points, goal.PointsByEnemy[v.EnemyId]));
    }

    [Fact(DisplayName = "FINISH-NEEDS-FORCES: a --pull N harness slice past pull 1 carries no forces goal; a full run keeps it")]
    public void Pull_slice_drops_the_goal()
    {
        var run = CreateRun(ContentLoader.Load());

        Assert.Same(run.Dungeon, HostRunSetup.FromPull(run.Dungeon, 1));
        var slice = HostRunSetup.FromPull(run.Dungeon, 4);
        Assert.Null(slice.ForcesGoal);
        Assert.Equal(run.Dungeon.Pulls.Skip(3).Select(p => p.Id), slice.Pulls.Select(p => p.Id));
    }

    [Fact(DisplayName = "FINISH-NEEDS-FORCES: the boss dead short of the count reads \"Enemy Forces NN% - finish the count\"")]
    public void Boss_dead_short_of_the_count_says_finish_the_count()
    {
        var registry = ContentLoader.Load();
        var run = BossDeadCriticalPathOnly(CreateRun(registry));
        // The teaching pulls bank 600 of the 1000 required (OptionalBranchForcesTests): 60%.
        var line = ObjectiveModel.Build(run, registry);

        Assert.True(line.Complete);
        Assert.Equal(6000, line.ForcesShortBp);
        Assert.Equal("Enemy Forces 60% - finish the count", line.Task);
        Assert.Contains(" forces_short=6000 complete", line.LogLine());
    }

    [Fact(DisplayName = "FINISH-NEEDS-FORCES: with the count full the cleared objective keeps its standing-enemies task")]
    public void Count_full_keeps_the_cleared_task()
    {
        var registry = ContentLoader.Load();
        var run = BossDeadCriticalPathOnly(CreateRun(registry));
        foreach (var actor in run.State.Actors.Values.Where(a => a.Kind == Keyrun.Sim.Core.ActorKind.Enemy))
        {
            actor.Alive = false;
        }
        var line = ObjectiveModel.Build(run, registry);

        Assert.Null(line.ForcesShortBp);
        Assert.Equal("No enemies remain", line.Task);
    }
}
