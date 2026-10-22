using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Nav;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// f-bd160e36, enemy chase through NavFollow. Enemy chase shares the NavGrid waypoint logic that #612 fixed for the party
/// walker: a walker within WaypointReachMm of its start cell's centre, with no corner in sight, used to pace between the
/// centre and the same centre re-targeted from the far side. This guard drives a chaser through
/// <see cref="NavFollow.MoveAlong"/> on the Sunken Bellfoundry's first boss entrance (the geometry of
/// <see cref="NavWaypointOvershootTests"/>) and asserts it never returns to a position two ticks back, and that its distance
/// to the target falls over every 20-tick window until it is within melee range.
/// </summary>
public sealed class NavFollowChaseTests
{
    /// <summary>Melee reach used as the arrival distance, mm.</summary>
    private const long MeleeMm = 300;

    /// <summary>Window length over which the distance to the target must fall, ticks.</summary>
    private const int WindowTicks = 20;

    /// <summary>Upper bound on ticks the chaser may take to close, ticks.</summary>
    private const int MaxTicks = 400;

    private static readonly Bounds Hall = new(117000, 130000, 34000, 37000);
    private static readonly Bounds Room = new(129000, 153000, 14000, 49500);
    private static readonly Vec3 Start = new(128861, 0, 34127);
    private static readonly Vec3 Target = new(129883, 0, 32099);

    private static NavLayout Layout() => new(
        new[] { new NavBox("hall", Hall, false, null), new NavBox("room", Room, true, null) },
        new[] { new NavTrigger("p1", Room) },
        new[] { new Vec3(118000, 0, 35500), new Vec3(140000, 0, 32000) });

    private static long Dist(Vec3 a, Vec3 b) => Movement.Isqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Z - b.Z) * (a.Z - b.Z)));

    private static bool Same(Vec3 a, Vec3 b) => a.X == b.X && a.Z == b.Z;

    [Fact(DisplayName = "f-bd160e36: a chaser beside its start cell's centre through NavFollow never paces and closes to melee")]
    public void Chaser_beside_the_cell_centre_never_paces_and_closes()
    {
        var layout = Layout();
        var grid = NavGrid.Of(layout);
        Assert.False(grid.LineOfSight(Start, Target), "fixture: the straight line to the target must be blocked");
        var swing = RunTestKit.Ability("swing", new EffectOp[] { new DamageOp(1, "physical") }, range: 2000);
        var content = RunTestKit.Content(new Dictionary<string, AbilityDef>(StringComparer.Ordinal) { ["swing"] = swing }, bounds: layout.Extent) with
        {
            Layout = layout,
            EnemyScripts = new Dictionary<int, IReadOnlyList<ScriptStep>>(),
        };
        var dungeon = new DungeonDef("chase", new[] { new PullDef("p1", new[] { RunTestKit.Spawn(101, "dummy", 150000, 40000, 10) }) });
        var run = RunRoot.CreateRun(1, new[] { RunTestKit.Member(1, "warden", ActorRole.Tank, Start.X, Start.Z, 400, 100) }, dungeon, content);

        var positions = new List<Vec3> { run.State.Actors[1].Pos };
        for (var i = 0; i < MaxTicks; i++)
        {
            if (NavFollow.MoveAlong(run.State.Tick, run.State.Actors[1], grid, Target, MeleeMm) is not { } move)
            {
                break;
            }
            run = RunRoot.StepRun(run, new Command[] { move }).Run;
            positions.Add(run.State.Actors[1].Pos);
        }

        // No two-cycle: once the chaser has left a position, it never returns to it two ticks later (A, B, A pacing).
        for (var t = 2; t < positions.Count; t++)
        {
            if (!Same(positions[t - 1], positions[t - 2]))
            {
                Assert.False(Same(positions[t], positions[t - 2]),
                    $"the chaser paced: tick {t} returned to ({positions[t].X},{positions[t].Z}) of tick {t - 2}");
            }
        }

        // Progress: over every 20-tick window that starts and ends out of melee range, the distance to the target falls.
        var distances = positions.Select(p => Dist(p, Target)).ToList();
        for (var t = 0; t + WindowTicks < distances.Count; t++)
        {
            if (distances[t] > MeleeMm && distances[t + WindowTicks] > MeleeMm)
            {
                Assert.True(distances[t + WindowTicks] < distances[t],
                    $"no progress over the window from tick {t}: {distances[t]} mm to {distances[t + WindowTicks]} mm");
            }
        }

        var end = positions[^1];
        Assert.True(Dist(end, Target) <= MeleeMm,
            $"the chaser never closed: ended at ({end.X},{end.Z}), {Dist(end, Target)} mm from the target after {run.State.Tick} ticks");
    }
}
