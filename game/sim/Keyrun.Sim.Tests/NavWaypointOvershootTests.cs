using System;
using System.Collections.Generic;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Nav;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// R-PATH-1, centre overshoot (f-bellfoundry-route-red). A walker within a step of its start cell's centre, with no corner
/// in sight, used to be sent at that centre, step past it, and from the far side be sent at the same centre again, pacing
/// between two points for the rest of the run. The fixture is the Sunken Bellfoundry's first boss entrance (seed 1): a
/// 3000 mm hall that meets a room 1000 mm before the hall ends, the goal a formation slot in the room south of the hall's
/// wall, so the straight line leaves the walkable boxes and the path turns a corner at the doorway.
/// </summary>
public sealed class NavWaypointOvershootTests
{
    private static readonly Bounds Hall = new(117000, 130000, 34000, 37000);
    private static readonly Bounds Room = new(129000, 153000, 14000, 49500);
    private static readonly Vec3 Start = new(128861, 0, 34127);
    private static readonly Vec3 Goal = new(129883, 0, 32099);

    private static NavLayout Layout() => new(
        new[] { new NavBox("hall", Hall, false, null), new NavBox("room", Room, true, null) },
        new[] { new NavTrigger("p1", Room) },
        new[] { new Vec3(118000, 0, 35500), new Vec3(140000, 0, 32000) });

    private static long Dist(Vec3 a, Vec3 b) => Movement.Isqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Z - b.Z) * (a.Z - b.Z)));

    [Fact(DisplayName = "R-PATH-1: a walker beside its start cell's centre with no corner in sight walks round the corner to the goal, never pacing")]
    public void A_walker_beside_the_cell_centre_arrives()
    {
        var layout = Layout();
        var grid = NavGrid.Of(layout);
        Assert.False(grid.LineOfSight(Start, Goal), "fixture: the straight line to the goal must be blocked");
        var swing = RunTestKit.Ability("swing", new EffectOp[] { new DamageOp(1, "physical") }, range: 2000);
        var content = RunTestKit.Content(new Dictionary<string, AbilityDef>(StringComparer.Ordinal) { ["swing"] = swing }, bounds: layout.Extent) with
        {
            Layout = layout,
            EnemyScripts = new Dictionary<int, IReadOnlyList<ScriptStep>>(),
        };
        var dungeon = new DungeonDef("nav", new[] { new PullDef("p1", new[] { RunTestKit.Spawn(101, "dummy", 150000, 40000, 10) }) });
        var run = RunRoot.CreateRun(1, new[] { RunTestKit.Member(1, "warden", ActorRole.Tank, Start.X, Start.Z, 400, 100) }, dungeon, content);

        for (var i = 0; i < 200; i++)
        {
            if (NavFollow.MoveAlong(run.State.Tick, run.State.Actors[1], grid, Goal, 300) is not { } move)
            {
                break;
            }
            run = RunRoot.StepRun(run, new Command[] { move }).Run;
        }

        var end = run.State.Actors[1].Pos;
        Assert.True(Dist(end, Goal) <= 300, $"the walker never arrived: ended at ({end.X},{end.Z}), {Dist(end, Goal)} mm from the goal after {run.State.Tick} ticks");
    }
}
