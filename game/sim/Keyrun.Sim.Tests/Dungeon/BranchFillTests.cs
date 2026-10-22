using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Dungeon;
using Xunit;

namespace Keyrun.Sim.Tests.Dungeon;

/// <summary>
/// The branch fill (STATE.md D45/D46, f-46043bc6): over a seeded room graph that forks at the entrance into two branches
/// converging on the boss, the first teaching pull opens every branch, the later teaching pulls are dealt one per branch
/// (which branch gets which is a seeded draw), and optional roster packs fill the remaining slots until each branch
/// carries its forces target. Properties over many seeds and every 2/3-room branch shape, never a pinned hash.
/// </summary>
public sealed class BranchFillTests
{
    // The Hollow Vault's teaching pulls with their content points (choir 2x40+80, pair 5x52, seed 1x180).
    internal static readonly IReadOnlyList<TeachingPull> Teaching = new[]
    {
        new TeachingPull("choir", 0, 160),
        new TeachingPull("pair", 1, 260),
        new TeachingPull("seed", 2, 180),
    };

    // The Hollow Vault's optional packs (cantor-vigil, hall-sentries, warrior-remnant).
    internal static readonly IReadOnlyList<RosterPack> Optional = new[]
    {
        new RosterPack("cantor-vigil", 400),
        new RosterPack("hall-sentries", 240),
        new RosterPack("warrior-remnant", 420),
    };

    private static readonly long[] Seeds = Enumerable.Range(1, 64).Select(i => (long)i).ToArray();

    internal static readonly (int A, int B)[] Shapes = { (2, 2), (2, 3), (3, 2), (3, 3) };

    /// <summary>
    /// A D45 graph: room 0 the entrance, then branch A's rooms, then branch B's, then the boss. Each branch is a chain;
    /// the entrance links to both chains' first rooms and both chains' last rooms link to the boss.
    /// </summary>
    internal static RoomGraph TwoBranchGraph(int a, int b)
    {
        var nodes = new List<RoomNode> { new(0, 0, 0, RoomKind.Entrance) };
        for (var i = 0; i < a; i++)
        {
            nodes.Add(new RoomNode(nodes.Count, i + 1, 0, RoomKind.Trash));
        }
        for (var i = 0; i < b; i++)
        {
            nodes.Add(new RoomNode(nodes.Count, i + 1, 1, RoomKind.Trash));
        }
        var boss = nodes.Count;
        nodes.Add(new RoomNode(boss, Math.Max(a, b) + 1, 0, RoomKind.Boss));
        var edges = new List<RoomEdge> { new(0, 1), new(0, 1 + a) };
        for (var i = 1; i < a; i++)
        {
            edges.Add(new RoomEdge(i, i + 1));
        }
        edges.Add(new RoomEdge(a, boss));
        for (var i = 1; i < b; i++)
        {
            edges.Add(new RoomEdge(a + i, a + i + 1));
        }
        edges.Add(new RoomEdge(a + b, boss));
        return new RoomGraph(nodes, edges.OrderBy(e => e.From).ThenBy(e => e.To).ToList());
    }

    private static IEnumerable<(long Seed, RoomGraph Graph, BranchFillResult Fill)> All()
    {
        foreach (var (a, b) in Shapes)
        {
            var g = TwoBranchGraph(a, b);
            foreach (var seed in Seeds)
            {
                yield return (seed, g, BranchFill.Fill(seed, g, Teaching, Optional));
            }
        }
    }

    [Fact(DisplayName = "Branch fill: the entrance forks into two branches that each walk room by room to the boss")]
    public void Branches_are_the_two_chains_from_the_entrance()
    {
        var g = TwoBranchGraph(2, 3);
        var branches = BranchFill.Branches(g);
        Assert.Equal(2, branches.Count);
        Assert.Equal(new[] { 1, 2 }, branches[0]);
        Assert.Equal(new[] { 3, 4, 5 }, branches[1]);
    }

    [Fact(DisplayName = "Branch fill: the same seed gives the same fill twice")]
    public void Same_seed_gives_the_same_fill()
    {
        foreach (var (seed, g, fill) in All())
        {
            var again = BranchFill.Fill(seed, g, Teaching, Optional);
            Assert.Equal(fill.Packs, again.Packs);
            Assert.Equal(fill.PairBranch, again.PairBranch);
        }
    }

    [Fact(DisplayName = "Branch fill: the first teaching pull opens both branches and the next two are dealt one per branch")]
    public void Teaching_pulls_open_each_branch()
    {
        foreach (var (seed, g, fill) in All())
        {
            Assert.Equal(2, fill.Branches.Count);
            for (var b = 0; b < 2; b++)
            {
                var rooms = fill.Branches[b].Rooms;
                var first = fill.Packs.Where(p => p.RoomId == rooms[0] && p.PullIndex >= 0).ToList();
                var second = fill.Packs.Where(p => p.RoomId == rooms[1] && p.PullIndex >= 0).ToList();
                Assert.Equal("choir", Assert.Single(first).PackId);
                var expected = b == fill.PairBranch ? "pair" : "seed";
                Assert.True(second.Count == 1 && second[0].PackId == expected, $"seed {seed}: branch {b} room 2 holds [{string.Join(",", second.Select(p => p.PackId))}], want {expected}");
                Assert.All(rooms.Skip(2), r => Assert.DoesNotContain(fill.Packs, p => p.RoomId == r && p.PullIndex >= 0));
            }
            Assert.Equal(new[] { "pair", "seed" }, fill.Branches.Select(br => br.SecondPull).OrderBy(s => s, StringComparer.Ordinal));
        }
    }

    [Fact(DisplayName = "Branch fill: which branch gets the Pair is mirrored by the seed")]
    public void Pair_branch_varies_by_seed()
    {
        var g = TwoBranchGraph(2, 2);
        var sides = Seeds.Select(s => BranchFill.Fill(s, g, Teaching, Optional).PairBranch).ToList();
        Assert.Contains(0, sides);
        Assert.Contains(1, sides);
        Assert.All(sides, s => Assert.InRange(s, 0, 1));
    }

    [Fact(DisplayName = "Branch fill: each branch carries its forces target, the dungeon carries more than 100 percent")]
    public void Each_branch_carries_its_target()
    {
        var target = BranchFillParams.Default.TargetPoints;
        Assert.Equal(1200, target);
        foreach (var (seed, g, fill) in All())
        {
            foreach (var br in fill.Branches)
            {
                var points = fill.Packs.Where(p => br.Rooms.Contains(p.RoomId)).Sum(p => p.Points);
                Assert.Equal(points, br.Points);
                Assert.True(points >= target, $"seed {seed}: branch {br.Index} carries {points} < {target}");
                // "Most of its branch": the last pack dealt crossed the target, so the branch overshoots by less than one pack.
                Assert.True(points < target + Optional.Max(o => o.Points), $"seed {seed}: branch {br.Index} carries {points}, overfilled");
            }
            Assert.Equal(fill.Packs.Sum(p => p.Points), fill.TotalPoints);
            Assert.True(fill.TotalPoints > 1000, $"seed {seed}: total {fill.TotalPoints}");
        }
    }

    [Fact(DisplayName = "Branch fill: every pack stands in a trash room on a free slot, at most one teaching pull per room")]
    public void Slots_and_pull_indices_are_unique_per_room()
    {
        foreach (var (seed, g, fill) in All())
        {
            Assert.All(fill.Packs, p => Assert.Equal(RoomKind.Trash, g.Nodes[p.RoomId].Kind));
            Assert.All(fill.Packs, p => Assert.InRange(p.Slot, 0, PackSlots.MaxSlotsPerRoom - 1));
            foreach (var room in fill.Packs.GroupBy(p => p.RoomId))
            {
                Assert.Equal(room.Count(), room.Select(p => p.Slot).Distinct().Count());
                Assert.True(room.Count(p => p.PullIndex >= 0) <= 1, $"seed {seed}: room {room.Key} holds two teaching pulls");
                Assert.True(room.Where(p => p.PullIndex >= 0).Select(p => p.PullIndex).Distinct().Count() == room.Count(p => p.PullIndex >= 0));
            }
            Assert.Equal(fill.Packs.Count, fill.Packs.Select(p => p.InstanceId).Distinct(StringComparer.Ordinal).Count());
            Assert.All(fill.Packs.Where(p => p.PullIndex < 0), p => Assert.Contains(Optional, o => o.Id == p.PackId && o.Points == p.Points));
        }
    }

    [Fact(DisplayName = "Branch fill: the optional packs reach every branch room past the teaching rooms")]
    public void Optional_packs_fill_the_remaining_rooms()
    {
        foreach (var (seed, g, fill) in All())
        {
            foreach (var br in fill.Branches)
            {
                Assert.All(br.Rooms.Skip(2), r => Assert.Contains(fill.Packs, p => p.RoomId == r));
            }
        }
    }

    [Fact(DisplayName = "Branch fill: every generated graph fills, both branches teach and carry the target")]
    public void Every_generated_graph_fills()
    {
        var target = BranchFillParams.Default.TargetPoints;
        var shapes = new HashSet<string>(StringComparer.Ordinal);
        for (long seed = 1; seed <= 200; seed++)
        {
            var g = RoomGraphGen.Generate(seed, DungeonGenParams.Default);
            var fill = BranchFill.Fill(seed, g, Teaching, Optional);
            shapes.Add(string.Join("/", fill.Branches.Select(b => b.Rooms.Count)));
            Assert.Equal(g.Nodes.Count - 2, fill.Branches.Sum(b => b.Rooms.Count));
            Assert.All(fill.Branches, b => Assert.True(b.Points >= target, $"seed {seed}: branch {b.Index} carries {b.Points}"));
            Assert.All(fill.Branches, b => Assert.Equal(2, fill.Packs.Count(p => b.Rooms.Contains(p.RoomId) && p.PullIndex >= 0)));
            // Every branch room lies on the graph's route through that branch's first room.
            Assert.All(fill.Branches, b => Assert.Contains(g.Routes(), r => r.Skip(1).Take(r.Count - 2).SequenceEqual(b.Rooms)));
        }
        Assert.Equal(4, shapes.Count);
    }

    [Fact(DisplayName = "Branch fill: fails closed on a graph without the entrance fork, a rejoin, or a short roster")]
    public void Fails_closed()
    {
        var linear = new RoomGraph(
            new[] { new RoomNode(0, 0, 0, RoomKind.Entrance), new RoomNode(1, 1, 0, RoomKind.Trash), new RoomNode(2, 2, 0, RoomKind.Trash), new RoomNode(3, 3, 0, RoomKind.Boss) },
            new[] { new RoomEdge(0, 1), new RoomEdge(1, 2), new RoomEdge(2, 3) });
        Assert.Throws<ArgumentException>(() => BranchFill.Fill(1, linear, Teaching, Optional));

        // A crossover: branch A's first room also leads into branch B.
        var crossed = new RoomGraph(
            new[] { new RoomNode(0, 0, 0, RoomKind.Entrance), new RoomNode(1, 1, 0, RoomKind.Trash), new RoomNode(2, 2, 0, RoomKind.Trash), new RoomNode(3, 1, 1, RoomKind.Trash), new RoomNode(4, 2, 1, RoomKind.Trash), new RoomNode(5, 3, 0, RoomKind.Boss) },
            new[] { new RoomEdge(0, 1), new RoomEdge(0, 3), new RoomEdge(1, 2), new RoomEdge(1, 4), new RoomEdge(2, 5), new RoomEdge(3, 4), new RoomEdge(4, 5) });
        Assert.Throws<ArgumentException>(() => BranchFill.Fill(1, crossed, Teaching, Optional));

        var g = TwoBranchGraph(2, 2);
        Assert.Throws<ArgumentException>(() => BranchFill.Fill(1, g, Teaching.Take(2).ToList(), Optional));
        Assert.Throws<ArgumentException>(() => BranchFill.Fill(1, g, Teaching, Array.Empty<RosterPack>()));
        // A target no slots can carry: 1-point packs never reach 1200.
        Assert.Throws<InvalidOperationException>(() => BranchFill.Fill(1, g, Teaching, new[] { new RosterPack("tiny", 1) }));
    }
}
