using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Nav;
using Keyrun.Sim.Runs;
using Xunit;
using Xunit.Abstractions;

namespace Keyrun.Sim.Tests;

/// <summary>
/// R-PATH-1 (core dungeon loop, WoW decides: actors path around walls and props, following a computed path to their
/// goal; movement stays deterministic and integer in the sim). The shipped layout is a convex box chain with no props, so
/// this fixture (test-only, never shipped content) puts one pillar obstacle in a hall: the straight line from the walker
/// to its goal runs through it. Every computed path prints a <c>PATH actor= goal= waypoints=</c> line.
/// </summary>
public sealed class NavPathingTests
{
    private static readonly Bounds Hall = new(-5000, 25000, -6000, 6000);
    private static readonly Bounds HallTrigger = new(13000, 25000, -6000, 6000);
    private static readonly Bounds Corridor = new(24000, 30000, -1000, 1000);
    private static readonly Bounds Room2 = new(29000, 40000, -5000, 5000);
    private static readonly Bounds Room2Trigger = new(31000, 40000, -5000, 5000);

    // The pillar: 4000 x 8000 mm in the middle of the hall, 2000 mm gaps to the north and south walls.
    private static readonly Bounds Pillar = new(8000, 12000, -4000, 4000);

    private static readonly Vec3 Start = new(4000, 0, 0);
    private static readonly Vec3 Goal = new(16000, 0, 0);

    private readonly ITestOutputHelper _out;

    public NavPathingTests(ITestOutputHelper output) => _out = output;

    private static NavLayout Layout() => new(
        new[]
        {
            new NavBox("hall", Hall, true, null),
            new NavBox("corridor", Corridor, false, null),
            new NavBox("room-2", Room2, true, null),
        },
        new[] { new NavTrigger("p1", HallTrigger), new NavTrigger("p2", Room2Trigger) },
        new[] { new Vec3(24500, 0, 0), new Vec3(29500, 0, 0), new Vec3(35000, 0, 0) })
    {
        Obstacles = new[] { Pillar },
    };

    private static Run Create(NavLayout layout, DungeonDef dungeon, IReadOnlyDictionary<int, IReadOnlyList<ScriptStep>>? scripts = null)
    {
        var swing = RunTestKit.Ability("swing", new EffectOp[] { new DamageOp(1, "physical") }, range: 2000);
        var content = RunTestKit.Content(new Dictionary<string, AbilityDef>(StringComparer.Ordinal) { ["swing"] = swing }, bounds: layout.Extent) with
        {
            Layout = layout,
            EnemyScripts = scripts ?? new Dictionary<int, IReadOnlyList<ScriptStep>>(),
        };
        return RunRoot.CreateRun(1, new[] { RunTestKit.Member(1, "warden", ActorRole.Tank, Start.X, Start.Z, 400, 100) }, dungeon, content);
    }

    // Both pulls far from the walk (room-2), so nothing engages while the Warden walks the hall.
    private static readonly DungeonDef FarDungeon = new("nav", new[]
    {
        new PullDef("p1", new[] { RunTestKit.Spawn(101, "dummy", 24000, 5000, 10) }),
        new PullDef("p2", new[] { RunTestKit.Spawn(201, "dummy", 38000, 4000, 10) }),
    });

    private void Print(NavGrid grid) =>
        grid.Observer = (id, goal, n) => _out.WriteLine(FormattableString.Invariant($"PATH actor={id} goal={goal.X},{goal.Z} waypoints={n}"));

    private static long Dist(Vec3 a, Vec3 b) => Movement.Isqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Z - b.Z) * (a.Z - b.Z)));

    [Fact(DisplayName = "R-PATH-1: the straight line to the goal is blocked; a walker that walks it stops at the pillar")]
    public void Straight_line_is_blocked_by_the_pillar()
    {
        var layout = Layout();
        var grid = NavGrid.Of(layout);
        Assert.False(grid.LineOfSight(Start, Goal), "fixture: the straight line from start to goal must cross the pillar");
        var run = Create(layout, FarDungeon);
        for (var i = 0; i < 200; i++)
        {
            run = RunRoot.StepRun(run, new Command[] { NavFollow.Toward(run.State.Tick, 1, run.State.Actors[1].Pos, Goal)! }).Run;
            Assert.False(NavGrid.Blocked(layout.Obstacles, run.State.Actors[1].Pos), $"the walker stood inside the pillar at {run.State.Actors[1].Pos}");
        }
        var end = run.State.Actors[1].Pos;
        Assert.True(end.X <= Pillar.MinX, $"a straight walker passed the pillar: {end}");
        Assert.True(Dist(end, Goal) > 300, $"a straight walker reached the goal through the pillar: {end}");
    }

    [Fact(DisplayName = "R-PATH-1: a walker following its computed path goes around the pillar and arrives, by more than two waypoints")]
    public void Path_follower_arrives_around_the_pillar()
    {
        var layout = Layout();
        var grid = NavGrid.Of(layout);
        Print(grid);
        var first = grid.FindPath(Start, Goal, 1);
        _out.WriteLine("waypoints: " + string.Join(" ", first.Select(p => FormattableString.Invariant($"({p.X},{p.Z})"))));
        Assert.True(first.Count > 2, $"the path around the pillar has {first.Count} waypoints");
        Assert.All(first.Zip(first.Skip(1)), seg => Assert.True(grid.LineOfSight(seg.First, seg.Second), $"segment {seg.First} -> {seg.Second} is not clear"));

        var run = Create(layout, FarDungeon);
        int? arrived = null;
        for (var i = 0; i < 400 && arrived is null; i++)
        {
            var me = run.State.Actors[1];
            if (NavFollow.MoveAlong(run.State.Tick, me, grid, Goal, 300) is not { } move)
            {
                arrived = run.State.Tick;
                break;
            }
            run = RunRoot.StepRun(run, new Command[] { move }).Run;
            Assert.False(NavGrid.Blocked(layout.Obstacles, run.State.Actors[1].Pos), $"the walker stood inside the pillar at {run.State.Actors[1].Pos}");
            Assert.True(grid.Walkable(run.State.Actors[1].Pos), $"the walker left walkable space at {run.State.Actors[1].Pos}");
        }
        _out.WriteLine(FormattableString.Invariant($"arrived tick={arrived} at={run.State.Actors[1].Pos}"));
        Assert.NotNull(arrived);
        Assert.True(Dist(run.State.Actors[1].Pos, Goal) <= 300);
    }

    [Fact(DisplayName = "R-PATH-1: same layout and inputs, identical path and identical walk, twice")]
    public void Paths_are_deterministic()
    {
        var a = new NavGrid(new[] { Hall, Corridor, Room2 }, new[] { Pillar });
        var b = new NavGrid(new[] { Hall, Corridor, Room2 }, new[] { Pillar });
        var pa = a.FindPath(Start, Goal, 1);
        var pb = b.FindPath(Start, Goal, 1);
        Assert.Equal(pa, pb);
        Assert.Equal(a.CellPath(a.CellAt(Start), a.CellAt(Goal)), b.CellPath(b.CellAt(Start), b.CellAt(Goal)));
        // A memo hit returns the same path as the computation.
        Assert.Equal(pa, a.FindPath(Start, Goal, 1));

        IReadOnlyList<Vec3> Walk()
        {
            var layout = Layout();
            var grid = NavGrid.Of(layout);
            var run = Create(layout, FarDungeon);
            var trail = new List<Vec3>();
            for (var i = 0; i < 150; i++)
            {
                var move = NavFollow.MoveAlong(run.State.Tick, run.State.Actors[1], grid, Goal, 300);
                run = RunRoot.StepRun(run, move is null ? Array.Empty<Command>() : new Command[] { move }).Run;
                trail.Add(run.State.Actors[1].Pos);
            }
            return trail;
        }
        Assert.Equal(Walk(), Walk());
    }

    [Fact(DisplayName = "R-PATH-1: an engaged enemy behind the pillar chases its target around it, not into it")]
    public void Enemy_chase_paths_around_the_pillar()
    {
        var layout = Layout();
        Print(NavGrid.Of(layout));
        var dungeon = new DungeonDef("nav", new[]
        {
            new PullDef("p1", new[] { RunTestKit.Spawn(101, "dummy", Goal.X, Goal.Z, 10) }),
            new PullDef("p2", new[] { RunTestKit.Spawn(201, "dummy", 38000, 4000, 10) }),
        });
        var scripts = new Dictionary<int, IReadOnlyList<ScriptStep>> { [101] = new[] { new ScriptStep(StepTrigger.Loop, "swing") { Every = 20 } } };
        var run = RunRoot.EngageStartPull(Create(layout, dungeon, scripts));
        Assert.False(run.State.Actors[101].Idle);
        int? reached = null;
        for (var i = 0; i < 300 && reached is null; i++)
        {
            run = RunRoot.StepRun(run, Array.Empty<Command>()).Run;
            var enemy = run.State.Actors[101];
            Assert.False(NavGrid.Blocked(layout.Obstacles, enemy.Pos), $"the enemy stood inside the pillar at {enemy.Pos}");
            if (Dist(enemy.Pos, run.State.Actors[1].Pos) <= 2000)
            {
                reached = run.State.Tick;
            }
        }
        _out.WriteLine(FormattableString.Invariant($"enemy reached tick={reached} at={run.State.Actors[101].Pos}"));
        Assert.NotNull(reached);
    }
}
