using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Sim.Dungeon;
using Xunit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// The bots' walk through a multi-segment seeded dungeon (STATE.md D65, <see cref="SeededLayout.Walk"/>): one branch
/// per segment on the route, every untaken branch as side boxes, every route box overlapping the next and moving east,
/// and the one-segment walk is the route <see cref="SeededLayout.Build"/> already ships.
/// </summary>
public sealed class SegmentWalkTests
{
    private static readonly long[] Seeds = Enumerable.Range(1, 50).Select(i => (long)i).ToArray();

    [Fact(DisplayName = "Segment walk: the route takes one branch per segment through every boss room; the other branches are side boxes")]
    public void Route_takes_one_branch_per_segment_and_the_rest_are_side()
    {
        foreach (var s in new[] { 2, 3 })
        {
            var p = SeededLayout.Params with { Segments = s };
            foreach (var seed in Seeds)
            {
                var ctx = $"S={s} seed {seed}";
                var graph = RoomGraphGen.Generate(seed, p);
                var grid = WalkGridGen.Emit(graph, p);
                var branches = SeededLayout.BotBranches(seed, s);
                Assert.Equal(SeededLayout.BotBranch(seed), branches[0]);
                var walk = SeededLayout.Walk(graph, grid, branches);

                var expected = new List<int> { graph.EntranceId };
                var side = new List<int>();
                for (var seg = 0; seg < s; seg++)
                {
                    var trash = graph.Nodes.Where(n => n.Kind == RoomKind.Trash && n.Segment == seg).ToList();
                    expected.AddRange(trash.Where(n => n.Branch == branches[seg]).Select(n => n.Id));
                    expected.Add(graph.BossRooms[seg]);
                    side.AddRange(trash.Where(n => n.Branch != branches[seg]).Select(n => n.Id));
                }
                Assert.Equal(expected, walk.Chain);
                Assert.Equal(expected.Select(SeededLayout.RoomBoxId), walk.Boxes.Where(b => b.Kind == LayoutBoxKind.Room).Select(b => b.Id));
                Assert.Equal(side.Select(SeededLayout.RoomBoxId), walk.SideBoxes.Where(b => b.Kind == LayoutBoxKind.Room).Select(b => b.Id));
                Assert.Equal(graph.RoomCount, walk.Chain.Count + side.Count);
                Assert.Empty(walk.Boxes.Select(b => b.Id).Intersect(walk.SideBoxes.Select(b => b.Id), StringComparer.Ordinal));

                // Every route box overlaps the next and starts east of it (LayoutValidator's +x route rule).
                for (var k = 0; k + 1 < walk.Boxes.Count; k++)
                {
                    var (a, b) = (walk.Boxes[k], walk.Boxes[k + 1]);
                    Assert.True(a.MinX < b.MaxX && b.MinX < a.MaxX && a.MinZ < b.MaxZ && b.MinZ < a.MaxZ, $"{ctx}: {a.Id} and {b.Id} do not overlap");
                    Assert.True(b.MinX > a.MinX, $"{ctx}: {b.Id} does not start east of {a.Id}");
                }
                // One one-way door per segment on the route (out of its fork), and one on each untaken branch.
                Assert.Equal(s, walk.Boxes.Count(b => b.OneWay));
                Assert.Equal(s, walk.SideBoxes.Count(b => b.OneWay));
            }
        }
    }

    [Fact(DisplayName = "Segment walk: one segment is the route and side boxes SeededLayout.Build ships")]
    public void One_segment_walk_is_the_built_route()
    {
        var reg = ContentLoader.Load();
        foreach (var seed in Seeds)
        {
            var d = SeededLayout.Build(reg, seed);
            var walk = SeededLayout.Walk(d.Graph, d.Grid, new[] { d.Branch });
            Assert.Equal(d.Registry.Layout.Boxes.Select(b => b.Id), walk.Boxes.Select(b => b.Id));
            Assert.Equal(d.Registry.Layout.SideBoxes.Select(b => b.Id), walk.SideBoxes.Select(b => b.Id));
        }
    }

    [Fact(DisplayName = "Segment walk: a branch list that is not one 0-or-1 per segment fails closed")]
    public void Bad_branch_list_fails_closed()
    {
        var p = SeededLayout.Params with { Segments = 2 };
        var graph = RoomGraphGen.Generate(1, p);
        var grid = WalkGridGen.Emit(graph, p);
        Assert.Throws<ArgumentException>(() => SeededLayout.Walk(graph, grid, new[] { 0 }));
        Assert.Throws<ArgumentException>(() => SeededLayout.Walk(graph, grid, new[] { 0, 2 }));
    }
}
