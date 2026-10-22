using System;
using System.Collections.Generic;
using Keyrun.Bots.Shared;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Nav;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// R-PATH-1 for the bots: <see cref="Follow.WalkTo"/> (the follow goal behind the lowest living seat, and every travel
/// goal built on it) walks the sim's computed path. The fixture is test-only: one room with a pillar obstacle between the
/// bot and its goal (the shipped layout has no props); the bot walks around it and arrives, where a straight walk stops
/// at the pillar face.
/// </summary>
public sealed class FollowPathingTests
{
    private static readonly Bounds Hall = new(-5000, 25000, -6000, 6000);
    private static readonly Bounds Room2 = new(24000, 40000, -5000, 5000);
    private static readonly Bounds Pillar = new(8000, 12000, -4000, 4000);
    private static readonly Vec3 Goal = new(16000, 0, 0);

    private static NavLayout Layout() => new(
        new[] { new NavBox("hall", Hall, true, null), new NavBox("room-2", Room2, true, null) },
        new[] { new NavTrigger("p1", new Bounds(30000, 40000, -5000, 5000)) },
        new[] { new Vec3(24500, 0, 0), new Vec3(35000, 0, 0) })
    {
        Obstacles = new[] { Pillar },
    };

    private static Run Create(NavLayout layout)
    {
        var dungeon = new DungeonDef("nav", new[]
        {
            new PullDef("p1", new[] { new PullSpawn(101, "dummy", new Vec3(38000, 0, 4000), 0, 10) }),
        });
        var content = new RunContent
        {
            Abilities = new Dictionary<string, AbilityDef>(StringComparer.Ordinal),
            Effects = new Dictionary<string, EffectDef>(StringComparer.Ordinal),
            LockoutTicks = 40,
            BaseSpeedMmPerTick = 180,
            ActorRadiusMm = 400,
            Bounds = layout.Extent,
            EnemyScripts = new Dictionary<int, IReadOnlyList<ScriptStep>>(),
            Layout = layout,
        };
        var party = new[] { new PartyMemberSpec(2, "chanter", ActorRole.Healer, new Vec3(4000, 0, 0), 400, 100) };
        return RunRoot.CreateRun(1, party, dungeon, content);
    }

    [Fact(DisplayName = "R-PATH-1: a following bot walks around the pillar to its slot and arrives")]
    public void Follow_walks_around_the_pillar()
    {
        var layout = Layout();
        var run = Create(layout);
        var first = Follow.WalkTo(run.State.Tick, run.State.Actors[2], layout, Goal);
        Assert.NotNull(first);
        var straight = Moves.MoveToward(run.State.Tick, 2, PointMm.Of(run.State.Actors[2].Pos), PointMm.Of(Goal))!;
        Assert.NotEqual(straight.Dir, first!.Dir);
        var waypoints = NavGrid.Of(layout).FindPath(run.State.Actors[2].Pos, Goal, 2).Count;
        Assert.True(waypoints > 2, $"the follow path has {waypoints} waypoints");

        int? arrived = null;
        for (var i = 0; i < 400; i++)
        {
            if (Follow.WalkTo(run.State.Tick, run.State.Actors[2], layout, Goal) is not { } move)
            {
                arrived = run.State.Tick;
                break;
            }
            run = RunRoot.StepRun(run, new Command[] { move }).Run;
            Assert.False(NavGrid.Blocked(layout.Obstacles, run.State.Actors[2].Pos), $"the bot stood inside the pillar at {run.State.Actors[2].Pos}");
        }
        Assert.NotNull(arrived);
    }
}
