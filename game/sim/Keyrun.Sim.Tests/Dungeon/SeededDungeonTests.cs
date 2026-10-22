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

    // The graph contract (STATE.md D63) is checked on seeds 1 to 50.
    private static readonly long[] ShapeSeeds = Enumerable.Range(1, 50).Select(i => (long)i).ToArray();

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

    [Fact(DisplayName = "Dungeon: every graph forks once, right after the entrance, into two branches of 2 or 3 rooms that converge on the boss")]
    public void Every_graph_forks_once_after_the_entrance_into_two_branches_that_converge_on_the_boss()
    {
        var shapes = new HashSet<(int, int)>();
        foreach (var seed in ShapeSeeds)
        {
            var g = RoomGraphGen.Generate(seed, DungeonGenParams.Default);
            Assert.Equal(RoomKind.Entrance, g.Nodes.Single(n => n.Id == g.EntranceId).Kind);
            Assert.Equal(RoomKind.Boss, g.Nodes.Single(n => n.Id == g.BossId).Kind);
            Assert.Empty(g.Predecessors(g.EntranceId));
            Assert.Empty(g.Successors(g.BossId));
            // Exactly one fork, and it is the entrance, into exactly two rooms.
            var forks = g.Nodes.Where(n => g.Successors(n.Id).Count >= 2).Select(n => n.Id).ToList();
            Assert.Equal(new[] { g.EntranceId }, forks);
            var firsts = g.Successors(g.EntranceId);
            Assert.Equal(2, firsts.Count);
            // Each branch is a chain of 2 or 3 trash rooms ending at the boss; the only join is the boss.
            var joins = g.Nodes.Where(n => g.Predecessors(n.Id).Count >= 2).Select(n => n.Id).ToList();
            Assert.Equal(new[] { g.BossId }, joins);
            Assert.Equal(2, g.Predecessors(g.BossId).Count);
            var branches = firsts.Select(f => Chain(g, f)).ToList();
            Assert.All(branches, b =>
            {
                Assert.InRange(b.Count, 2, 3);
                Assert.All(b, id => Assert.Equal(RoomKind.Trash, g.Nodes[id].Kind));
                Assert.Equal(g.BossId, Assert.Single(g.Successors(b[b.Count - 1])));
            });
            Assert.Empty(branches[0].Intersect(branches[1]));
            // Every room is the entrance, the boss, or in exactly one branch.
            Assert.Equal(g.Nodes.Count, 2 + branches[0].Count + branches[1].Count);
            Assert.Equal(2, g.Routes().Count);
            shapes.Add((branches[0].Count, branches[1].Count));
        }
        // The seed varies the branch lengths: all four shapes appear over 50 seeds.
        Assert.Equal(4, shapes.Count);
    }

    [Fact(DisplayName = "Dungeon: once a branch's first room is entered, the entrance and the other branch are unreachable (one-way doors)")]
    public void A_branch_once_entered_never_reaches_the_entrance_or_the_untaken_branch()
    {
        foreach (var seed in ShapeSeeds)
        {
            var g = RoomGraphGen.Generate(seed, DungeonGenParams.Default);
            var layerOf = g.Nodes.ToDictionary(n => n.Id, n => n.Layer);
            // Forward-only: every edge points strictly deeper, and nothing points into the entrance.
            Assert.All(g.Edges, e => Assert.True(layerOf[e.To] > layerOf[e.From], $"seed {seed}: edge {e.From}>{e.To} not deeper"));
            Assert.DoesNotContain(g.Edges, e => e.To == g.EntranceId);
            // The doors are exactly the entrance-to-branch edges.
            var firsts = g.Successors(g.EntranceId);
            Assert.All(g.Edges, e => Assert.Equal(e.From == g.EntranceId, e.OneWayDoor));
            foreach (var first in firsts)
            {
                var other = firsts.Single(f => f != first);
                var reach = Reachable(g, first);
                var untaken = Chain(g, other);
                Assert.DoesNotContain(g.EntranceId, reach);
                Assert.Empty(reach.Intersect(untaken));
                Assert.Contains(g.BossId, reach);
            }
        }
    }

    [Fact(DisplayName = "Dungeon: the walkable grid keeps the two branches apart outside the entrance and boss rooms")]
    public void Grid_keeps_the_two_branches_apart_outside_the_entrance_and_boss_rooms()
    {
        foreach (var seed in Seeds)
        {
            var d = DungeonPipeline.Generate(seed, BibleRoster);
            var g = d.Graph;
            var hubs = new[] { d.Grid.Rooms[g.EntranceId], d.Grid.Rooms[g.BossId] };
            var firsts = g.Successors(g.EntranceId);
            var a = d.Grid.Rooms[firsts[0]];
            var b = d.Grid.Rooms[firsts[1]];
            Assert.False(Reaches(d.Grid, a.CenterCx, a.CenterCz, b.CenterCx, b.CenterCz, hubs),
                $"seed {seed}: the branches meet on the grid outside the entrance and boss rooms");
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

    // The graph has four shapes by contract (D63), so seed variety is the graph plus the pack fill.
    [Fact(DisplayName = "Dungeon: a different seed gives a different dungeon")]
    public void A_different_seed_gives_a_different_dungeon()
    {
        Assert.NotEqual(Shape(1), Shape(2));
        var distinct = Seeds.Select(Shape).Distinct().Count();
        Assert.True(distinct >= Seeds.Length / 2, $"only {distinct} distinct dungeons over {Seeds.Length} seeds");
    }

    private static string Shape(long seed)
    {
        var d = DungeonPipeline.Generate(seed, BibleRoster);
        return d.Graph.Signature() + "#" + string.Join(",", d.Packs.Select(p => $"{p.RoomId}:{p.Slot}:{p.PackId}"));
    }

    /// <summary>The chain of rooms from <paramref name="first"/> up to (not including) the boss.</summary>
    private static List<int> Chain(RoomGraph g, int first)
    {
        var chain = new List<int>();
        for (var at = first; at != g.BossId; at = Assert.Single(g.Successors(at)))
        {
            chain.Add(at);
        }
        return chain;
    }

    /// <summary>Every room reachable from <paramref name="from"/> along edges (independent of RoomGraph.Routes).</summary>
    private static HashSet<int> Reachable(RoomGraph g, int from)
    {
        var seen = new HashSet<int> { from };
        var stack = new Stack<int>();
        stack.Push(from);
        while (stack.Count > 0)
        {
            var at = stack.Pop();
            foreach (var e in g.Edges.Where(e => e.From == at))
            {
                if (seen.Add(e.To))
                {
                    stack.Push(e.To);
                }
            }
        }
        return seen;
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

    private static bool Reaches(WalkGrid grid, int fx, int fz, int tx, int tz, IReadOnlyList<RoomRect>? walls = null)
    {
        bool Open(int x, int z) => grid.IsWalkable(x, z) && (walls is null || !walls.Any(r => x >= r.MinCx && x <= r.MaxCx && z >= r.MinCz && z <= r.MaxCz));
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
                if (Open(nx, nz) && !seen[(nz * grid.Width) + nx])
                {
                    seen[(nz * grid.Width) + nx] = true;
                    queue.Enqueue((nx, nz));
                }
            }
        }
        return false;
    }
}
