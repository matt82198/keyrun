using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Dungeon;
using Xunit;

namespace Keyrun.Sim.Tests.Dungeon;

/// <summary>
/// The seeded dungeon pipeline (game/design/ARCHITECTURE.md section 2, STATE.md D45/D46): seed -> room graph -> walkable
/// grid -> pack slots -> fill from the roster. Every stage is a pure integer function of the seed, so these tests drive
/// <see cref="DungeonPipeline.Generate"/> over many seeds and assert the properties, never a pinned hash.
/// </summary>
public sealed class SeededDungeonTests
{
    // BIBLE section 9.2 optional packs and their points (W1 Hound Kennel .. E3 Floodgate Watch).
    internal static readonly IReadOnlyList<RosterPack> BibleRoster = new[]
    {
        new RosterPack("w1-hound-kennel", 220),
        new RosterPack("w2-bellows-crew", 400),
        new RosterPack("e1-reading-watch", 140),
        new RosterPack("e2-loft-choir", 180),
        new RosterPack("e3-floodgate-watch", 260),
    };

    private static readonly long[] Seeds = Enumerable.Range(1, 48).Select(i => (long)i * 7919).ToArray();

    // Graph-only checks are cheap, so they sweep enough seeds to hit the rare all-one-wide draw the fork rule repairs.
    private static readonly long[] ShapeSeeds = Enumerable.Range(1, 400).Select(i => (long)i).ToArray();

    [Fact(DisplayName = "Dungeon: the same seed gives an identical graph and grid twice")]
    public void Same_seed_gives_identical_graph_and_grid_twice()
    {
        foreach (var seed in Seeds)
        {
            var a = DungeonPipeline.Generate(seed, BibleRoster);
            var b = DungeonPipeline.Generate(seed, BibleRoster);
            Assert.Equal(a.Graph.Nodes, b.Graph.Nodes);
            Assert.Equal(a.Graph.Edges, b.Graph.Edges);
            Assert.Equal(a.Grid.Width, b.Grid.Width);
            Assert.Equal(a.Grid.Height, b.Grid.Height);
            Assert.True(a.Grid.Cells.SequenceEqual(b.Grid.Cells), $"seed {seed}: grid cells differ");
            Assert.Equal(a.Grid.Digest(), b.Grid.Digest());
            Assert.Equal(a.Packs, b.Packs);
            Assert.True(a.Grid.WalkableCount > 0, $"seed {seed}: empty grid");
        }
    }

    [Fact(DisplayName = "Dungeon: every graph is a forward-only DAG from entrance to boss with a fork that rejoins before the boss")]
    public void Every_graph_is_a_forward_only_dag_with_a_fork_that_rejoins_before_the_boss()
    {
        foreach (var seed in ShapeSeeds)
        {
            var g = RoomGraphGen.Generate(seed, DungeonGenParams.Default);
            var layerOf = g.Nodes.ToDictionary(n => n.Id, n => n.Layer);
            Assert.Equal(RoomKind.Entrance, g.Nodes.Single(n => n.Id == g.EntranceId).Kind);
            Assert.Equal(RoomKind.Boss, g.Nodes.Single(n => n.Id == g.BossId).Kind);
            // Forward-only: every edge goes exactly one layer deeper, so no edge can point back or sideways.
            Assert.All(g.Edges, e => Assert.Equal(layerOf[e.From] + 1, layerOf[e.To]));
            Assert.Empty(g.Predecessors(g.EntranceId));
            Assert.Empty(g.Successors(g.BossId));
            // Every room lies on some route: reachable from the entrance and reaching the boss.
            var onRoute = g.Routes().SelectMany(r => r).ToHashSet();
            Assert.All(g.Nodes, n => Assert.Contains(n.Id, onRoute));
            // Never linear: at least one fork and one rejoin, and every route passes one room just before the boss.
            Assert.Contains(g.Nodes, n => g.Successors(n.Id).Count >= 2);
            Assert.Contains(g.Nodes, n => g.Predecessors(n.Id).Count >= 2);
            var beforeBoss = Assert.Single(g.Predecessors(g.BossId));
            Assert.All(g.Routes(), r => Assert.Equal(beforeBoss, r[r.Count - 2]));
            Assert.True(g.Routes().Count >= 2, $"seed {seed}: one route only");
        }
    }

    [Fact(DisplayName = "Dungeon: every route reaches 100 percent forces via at least two disjoint pack choices")]
    public void Every_route_reaches_full_forces_by_two_disjoint_pack_choices()
    {
        var routesChecked = 0;
        foreach (var seed in Seeds)
        {
            var d = DungeonPipeline.Generate(seed, BibleRoster);
            Assert.Equal(1000, d.ForcesRequired);
            foreach (var route in d.Graph.Routes())
            {
                var points = d.Packs.Where(p => route.Contains(p.RoomId)).Select(p => p.Points).ToList();
                Assert.True(HasTwoDisjointFills(points, d.ForcesRequired),
                    $"seed {seed} route {string.Join(">", route)}: packs [{string.Join(",", points)}] have no two disjoint sets reaching {d.ForcesRequired}");
                routesChecked++;
            }
        }
        Assert.True(routesChecked >= Seeds.Length * 2, $"only {routesChecked} routes checked");
    }

    [Fact(DisplayName = "Dungeon: a different seed gives a different graph")]
    public void A_different_seed_gives_a_different_graph()
    {
        var one = DungeonPipeline.Generate(1, BibleRoster).Graph.Signature();
        var two = DungeonPipeline.Generate(2, BibleRoster).Graph.Signature();
        Assert.NotEqual(one, two);
        var shapes = Seeds.Select(s => DungeonPipeline.Generate(s, BibleRoster).Graph.Signature()).Distinct().Count();
        Assert.True(shapes >= Seeds.Length / 2, $"only {shapes} distinct graphs over {Seeds.Length} seeds");
    }

    [Fact(DisplayName = "Dungeon: the walkable grid connects the entrance room to the boss room")]
    public void Grid_walks_from_entrance_to_boss()
    {
        foreach (var seed in Seeds)
        {
            var d = DungeonPipeline.Generate(seed, BibleRoster);
            var from = d.Grid.Rooms.Single(r => r.RoomId == d.Graph.EntranceId);
            var to = d.Grid.Rooms.Single(r => r.RoomId == d.Graph.BossId);
            Assert.True(Reaches(d.Grid, from.CenterCx, from.CenterCz, to.CenterCx, to.CenterCz), $"seed {seed}: boss unreachable");
            Assert.All(d.Packs, p => Assert.True(d.Grid.Contains(p.Center), $"seed {seed}: pack {p.PackId} off the grid"));
        }
    }

    [Fact(DisplayName = "Dungeon: the filler fails closed on an empty or duplicate roster")]
    public void Filler_fails_closed_on_a_bad_roster()
    {
        Assert.Throws<ArgumentException>(() => DungeonPipeline.Generate(1, Array.Empty<RosterPack>()));
        Assert.Throws<ArgumentException>(() => DungeonPipeline.Generate(1, new[] { new RosterPack("a", 100), new RosterPack("a", 200) }));
        Assert.Throws<ArgumentException>(() => DungeonPipeline.Generate(1, new[] { new RosterPack("a", 0) }));
    }

    /// <summary>
    /// Independent check (not the filler's own rule): is there a pair of disjoint subsets each summing to at least
    /// <paramref name="required"/>? DP over (A, B) sums capped at the requirement, in units of the points' gcd.
    /// </summary>
    internal static bool HasTwoDisjointFills(IReadOnlyList<int> points, int required)
    {
        if (points.Count == 0)
        {
            return false;
        }
        var g = points.Aggregate(required, Gcd);
        var cap = required / g;
        var reach = new bool[cap + 1, cap + 1];
        reach[0, 0] = true;
        foreach (var p in points.Select(x => x / g))
        {
            var next = (bool[,])reach.Clone();
            for (var a = 0; a <= cap; a++)
            {
                for (var b = 0; b <= cap; b++)
                {
                    if (!reach[a, b])
                    {
                        continue;
                    }
                    next[Math.Min(cap, a + p), b] = true;
                    next[a, Math.Min(cap, b + p)] = true;
                }
            }
            reach = next;
        }
        return reach[cap, cap];
    }

    private static int Gcd(int a, int b) => b == 0 ? a : Gcd(b, a % b);

    private static bool Reaches(WalkGrid grid, int fx, int fz, int tx, int tz)
    {
        var seen = new bool[grid.Width * grid.Height];
        var queue = new Queue<(int X, int Z)>();
        queue.Enqueue((fx, fz));
        seen[(fz * grid.Width) + fx] = true;
        while (queue.Count > 0)
        {
            var (x, z) = queue.Dequeue();
            if (x == tx && z == tz)
            {
                return true;
            }
            foreach (var (nx, nz) in new[] { (x + 1, z), (x - 1, z), (x, z + 1), (x, z - 1) })
            {
                if (grid.IsWalkable(nx, nz) && !seen[(nz * grid.Width) + nx])
                {
                    seen[(nz * grid.Width) + nx] = true;
                    queue.Enqueue((nx, nz));
                }
            }
        }
        return false;
    }
}
