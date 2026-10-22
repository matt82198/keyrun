using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Keyrun.Sim.Core;
using Keyrun.Sim.Dungeon;
using Xunit;

namespace Keyrun.Sim.Tests.Dungeon;

/// <summary>
/// Multi-segment seeded dungeons (STATE.md D65, D45/D46): entrance, fork, boss, fork, boss, ..., final boss. Each
/// segment is today's single-fork shape whose boss room is the next segment's fork. Properties over seeds 1 to 50 for
/// two and three segments; one segment is pinned byte-identical to the single-fork generator it extends.
/// </summary>
public sealed class SegmentedDungeonTests
{
    private static readonly long[] Seeds = Enumerable.Range(1, 50).Select(i => (long)i).ToArray();

    private static readonly int[] MultiSegments = { 2, 3 };

    // The bots' run params (SeededLayout.Params): 24-cell rooms, 3-cell corridors.
    private static readonly DungeonGenParams RunParams = DungeonGenParams.Default with { RoomCells = 24, CorridorCells = 3 };

    // FNV-1a of Snapshot(seed) for seeds 1..50, captured from the single-fork generator (keyrun #552 head 97d3b590)
    // before segments existed. Byte-identical proof for a mechanical extension (R-ORACLE-1); the shape properties are
    // asserted by name below and in SeededDungeonTests.
    private static readonly uint[] GoldenDefault =
    {
        0xc7c24665u, 0xdcd80c46u, 0xa70eaf61u, 0xdcd80c46u, 0x78c53bbdu, 0xdcd80c46u, 0xdcd80c46u, 0xc7c24665u, 0xa70eaf61u, 0xc7c24665u,
        0xa70eaf61u, 0xdcd80c46u, 0xa70eaf61u, 0x78c53bbdu, 0x78c53bbdu, 0x78c53bbdu, 0x78c53bbdu, 0xdcd80c46u, 0xa70eaf61u, 0x78c53bbdu,
        0xdcd80c46u, 0x78c53bbdu, 0xc7c24665u, 0xc7c24665u, 0xdcd80c46u, 0xa70eaf61u, 0xa70eaf61u, 0xa70eaf61u, 0xdcd80c46u, 0xdcd80c46u,
        0xa70eaf61u, 0xc7c24665u, 0xdcd80c46u, 0x78c53bbdu, 0xa70eaf61u, 0x78c53bbdu, 0xa70eaf61u, 0xdcd80c46u, 0xa70eaf61u, 0xa70eaf61u,
        0xa70eaf61u, 0xdcd80c46u, 0xdcd80c46u, 0xdcd80c46u, 0xc7c24665u, 0xc7c24665u, 0xa70eaf61u, 0xdcd80c46u, 0xa70eaf61u, 0xa70eaf61u,
    };

    private static readonly uint[] GoldenRun =
    {
        0xe8e3c952u, 0x70963a1fu, 0xfebc8634u, 0x70963a1fu, 0x1e848077u, 0x70963a1fu, 0x70963a1fu, 0xe8e3c952u, 0xfebc8634u, 0xe8e3c952u,
        0xfebc8634u, 0x70963a1fu, 0xfebc8634u, 0x1e848077u, 0x1e848077u, 0x1e848077u, 0x1e848077u, 0x70963a1fu, 0xfebc8634u, 0x1e848077u,
        0x70963a1fu, 0x1e848077u, 0xe8e3c952u, 0xe8e3c952u, 0x70963a1fu, 0xfebc8634u, 0xfebc8634u, 0xfebc8634u, 0x70963a1fu, 0x70963a1fu,
        0xfebc8634u, 0xe8e3c952u, 0x70963a1fu, 0x1e848077u, 0xfebc8634u, 0x1e848077u, 0xfebc8634u, 0x70963a1fu, 0xfebc8634u, 0xfebc8634u,
        0xfebc8634u, 0x70963a1fu, 0x70963a1fu, 0x70963a1fu, 0xe8e3c952u, 0xe8e3c952u, 0xfebc8634u, 0x70963a1fu, 0xfebc8634u, 0xfebc8634u,
    };

    private static RoomGraph Gen(long seed, int segments, DungeonGenParams? p = null) =>
        RoomGraphGen.Generate(seed, (p ?? DungeonGenParams.Default) with { Segments = segments });

    [Fact(DisplayName = "Segments: one segment is byte-identical to the single-fork generator for seeds 1-50 (graph and grid)")]
    public void One_segment_is_byte_identical_to_the_single_fork_generator()
    {
        Assert.Equal(1, DungeonGenParams.Default.Segments);
        for (var i = 0; i < Seeds.Length; i++)
        {
            Assert.True(GoldenDefault[i] == Snapshot(Seeds[i], DungeonGenParams.Default), $"seed {Seeds[i]} (default params) differs from the single-fork generator");
            Assert.True(GoldenRun[i] == Snapshot(Seeds[i], RunParams), $"seed {Seeds[i]} (run params) differs from the single-fork generator");
            Assert.True(GoldenDefault[i] == Snapshot(Seeds[i], DungeonGenParams.Default with { Segments = 1 }), $"seed {Seeds[i]}: explicit Segments=1 differs");
        }
    }

    [Fact(DisplayName = "Segments: params fail closed outside 1..MaxSegments")]
    public void Segments_out_of_range_fail_closed()
    {
        Assert.Throws<ArgumentException>(() => Gen(1, 0));
        Assert.Throws<ArgumentException>(() => Gen(1, DungeonGenParams.MaxSegments + 1));
        Assert.Equal(DungeonGenParams.MaxSegments, Gen(1, DungeonGenParams.MaxSegments).SegmentCount);
    }

    [Fact(DisplayName = "Segments: room budget is 1 + S*(two branches of 2-3 rooms + a boss room); room, segment and branch ids are consistent")]
    public void Room_budget_and_ids_are_consistent()
    {
        foreach (var s in MultiSegments)
        {
            var counts = new HashSet<int>();
            foreach (var seed in Seeds)
            {
                var g = Gen(seed, s);
                var ctx = $"S={s} seed {seed}";
                Assert.Equal(g.Nodes.Count, g.RoomCount);
                Assert.Equal(s, g.SegmentCount);
                var expected = 1;
                for (var seg = 0; seg < s; seg++)
                {
                    for (var b = 0; b < RoomGraphGen.BranchCount; b++)
                    {
                        var len = g.Nodes.Count(n => n.Kind == RoomKind.Trash && n.Segment == seg && n.Branch == b);
                        Assert.True(len >= RoomGraphGen.MinBranchRooms && len <= RoomGraphGen.MaxBranchRooms, $"{ctx}: segment {seg} branch {b} has {len} rooms");
                        expected += len;
                    }
                    expected++;
                }
                Assert.Equal(expected, g.RoomCount);
                Assert.InRange(g.RoomCount, 1 + (s * 5), 1 + (s * 7));
                counts.Add(g.RoomCount);

                // Bosses: one per segment, ascending, the last one is the final boss and the last room.
                Assert.Equal(s, g.BossRooms.Count);
                Assert.Equal(g.BossRooms.OrderBy(x => x), g.BossRooms);
                Assert.Equal(g.RoomCount - 1, g.BossId);
                Assert.Equal(g.BossId, g.BossRooms[^1]);
                Assert.Equal(g.Nodes.Where(n => n.Kind == RoomKind.Boss).Select(n => n.Id), g.BossRooms);
                for (var seg = 0; seg < s; seg++)
                {
                    var boss = g.Nodes[g.BossRooms[seg]];
                    Assert.Equal(seg, boss.Segment);
                    Assert.Equal(-1, boss.Branch);
                    Assert.Equal(seg == 0 ? g.EntranceId : g.BossRooms[seg - 1], g.SegmentEntry(seg));
                }
                var entrance = g.Nodes[g.EntranceId];
                Assert.Equal((0, 0, -1, RoomKind.Entrance), (entrance.Id, entrance.Segment, entrance.Branch, entrance.Kind));
                Assert.Single(g.Nodes, n => n.Kind == RoomKind.Entrance);
                Assert.All(g.Nodes.Where(n => n.Kind == RoomKind.Trash), n =>
                {
                    Assert.InRange(n.Branch, 0, RoomGraphGen.BranchCount - 1);
                    Assert.Equal(n.Branch, n.Lane);
                    Assert.InRange(n.Segment, 0, s - 1);
                });
                // Ids are dense in walking order: segment and layer never decrease with the id.
                for (var i = 1; i < g.RoomCount; i++)
                {
                    Assert.True(g.Nodes[i].Segment >= g.Nodes[i - 1].Segment, $"{ctx}: segment drops at room {i}");
                    Assert.True(g.Nodes[i].Layer >= g.Nodes[i - 1].Layer, $"{ctx}: layer drops at room {i}");
                }
            }
            Assert.True(counts.Count > 1, $"S={s}: the seed never varies the room count");
        }
    }

    [Fact(DisplayName = "Segments: exactly one fork per segment; both branches of a segment meet at that segment's boss")]
    public void One_fork_per_segment_and_every_branch_meets_its_boss()
    {
        foreach (var s in MultiSegments)
        {
            foreach (var seed in Seeds)
            {
                var g = Gen(seed, s);
                var ctx = $"S={s} seed {seed}";
                var forks = g.Nodes.Where(n => g.Successors(n.Id).Count >= 2).Select(n => n.Id).ToList();
                Assert.Equal(new[] { g.EntranceId }.Concat(g.BossRooms.Take(s - 1)), forks);
                var joins = g.Nodes.Where(n => g.Predecessors(n.Id).Count >= 2).Select(n => n.Id).ToList();
                Assert.Equal(g.BossRooms, joins);
                Assert.Empty(g.Successors(g.BossId));
                Assert.Empty(g.Predecessors(g.EntranceId));
                for (var seg = 0; seg < s; seg++)
                {
                    var roots = g.Successors(g.SegmentEntry(seg));
                    Assert.Equal(RoomGraphGen.BranchCount, roots.Count);
                    var chains = new List<List<int>>();
                    for (var b = 0; b < roots.Count; b++)
                    {
                        var chain = new List<int>();
                        var at = roots[b];
                        while (g.Nodes[at].Kind == RoomKind.Trash)
                        {
                            Assert.Equal((seg, b), (g.Nodes[at].Segment, g.Nodes[at].Branch));
                            chain.Add(at);
                            at = Assert.Single(g.Successors(at));
                        }
                        Assert.True(at == g.BossRooms[seg], $"{ctx}: segment {seg} branch {b} ends at {at}, not its boss {g.BossRooms[seg]}");
                        Assert.InRange(chain.Count, RoomGraphGen.MinBranchRooms, RoomGraphGen.MaxBranchRooms);
                        chains.Add(chain);
                    }
                    Assert.Empty(chains[0].Intersect(chains[1]));
                    Assert.Equal(g.Nodes.Count(n => n.Kind == RoomKind.Trash && n.Segment == seg), chains.Sum(c => c.Count));
                }
                Assert.Equal(1 << s, g.Routes().Count);
            }
        }
    }

    [Fact(DisplayName = "Segments: forward-only, no edge back into a finished segment, one-way doors at every fork and boss exit, every room reachable")]
    public void Forward_only_with_one_way_doors_and_every_room_reachable()
    {
        foreach (var s in MultiSegments)
        {
            foreach (var seed in Seeds)
            {
                var g = Gen(seed, s);
                var ctx = $"S={s} seed {seed}";
                var hubs = new HashSet<int>(g.BossRooms.Take(s - 1).Prepend(g.EntranceId));
                foreach (var e in g.Edges)
                {
                    var (a, b) = (g.Nodes[e.From], g.Nodes[e.To]);
                    Assert.True(b.Layer > a.Layer, $"{ctx}: edge {e.From}>{e.To} not deeper");
                    var step = b.Segment - a.Segment;
                    Assert.True(step == 0 || (step == 1 && a.Kind == RoomKind.Boss), $"{ctx}: edge {e.From}>{e.To} leaves segment {a.Segment} for {b.Segment}");
                    Assert.True(e.OneWayDoor == hubs.Contains(e.From), $"{ctx}: edge {e.From}>{e.To} door {e.OneWayDoor}");
                }
                Assert.DoesNotContain(g.Edges, e => e.To == g.EntranceId);
                Assert.Equal(g.RoomCount, Reachable(g, g.EntranceId).Count);
                for (var seg = 0; seg < s; seg++)
                {
                    var entry = g.SegmentEntry(seg);
                    var roots = g.Successors(entry);
                    foreach (var root in roots)
                    {
                        var other = roots.Single(r => r != root);
                        var reach = Reachable(g, root);
                        Assert.DoesNotContain(entry, reach);
                        Assert.DoesNotContain(other, reach);
                        Assert.All(reach, id => Assert.True(g.Nodes[id].Segment >= seg, $"{ctx}: room {id} of finished segment {g.Nodes[id].Segment} reachable from segment {seg}"));
                        Assert.Contains(g.BossRooms[seg], reach);
                        Assert.Contains(g.BossId, reach);
                    }
                }
            }
        }
    }

    [Fact(DisplayName = "Segments: the walk grid lays segments end to end, room boxes disjoint, no corridor between segments bypasses a boss room")]
    public void Grid_lays_segments_end_to_end()
    {
        foreach (var p in new[] { DungeonGenParams.Default, RunParams })
        {
            foreach (var s in MultiSegments)
            {
                foreach (var seed in Seeds)
                {
                    var g = Gen(seed, s, p);
                    var grid = WalkGridGen.Emit(g, p with { Segments = s });
                    var ctx = $"S={s} seed {seed} rooms {p.RoomCells}";
                    Assert.Equal(g.RoomCount, grid.Rooms.Count);
                    for (var i = 0; i < grid.Rooms.Count; i++)
                    {
                        for (var j = i + 1; j < grid.Rooms.Count; j++)
                        {
                            Assert.False(Overlap(grid.Rooms[i], grid.Rooms[j]), $"{ctx}: rooms {i} and {j} overlap");
                        }
                    }
                    for (var seg = 0; seg + 1 < s; seg++)
                    {
                        var boss = grid.Rooms[g.BossRooms[seg]];
                        Assert.All(g.Nodes.Where(n => n.Id != boss.RoomId && n.Segment <= seg), n => Assert.True(grid.Rooms[n.Id].MaxCx < boss.MinCx, $"{ctx}: room {n.Id} not west of boss {boss.RoomId}"));
                        Assert.All(g.Nodes.Where(n => n.Segment > seg), n => Assert.True(grid.Rooms[n.Id].MinCx > boss.MaxCx, $"{ctx}: room {n.Id} not east of boss {boss.RoomId}"));
                        // Walling off a mid boss room cuts the grid in two: the only way between segments is through it.
                        var start = grid.Rooms[g.EntranceId];
                        var end = grid.Rooms[g.BossId];
                        Assert.False(Reaches(grid, start.CenterCx, start.CenterCz, end.CenterCx, end.CenterCz, new[] { boss }), $"{ctx}: a corridor bypasses boss room {boss.RoomId}");
                    }
                    for (var seg = 0; seg < s; seg++)
                    {
                        var hubs = new[] { grid.Rooms[g.SegmentEntry(seg)], grid.Rooms[g.BossRooms[seg]] };
                        var roots = g.Successors(g.SegmentEntry(seg));
                        var (a, b) = (grid.Rooms[roots[0]], grid.Rooms[roots[1]]);
                        Assert.False(Reaches(grid, a.CenterCx, a.CenterCz, b.CenterCx, b.CenterCz, hubs), $"{ctx}: segment {seg}'s branches meet outside its fork and boss");
                    }
                    var from = grid.Rooms[g.EntranceId];
                    var to = grid.Rooms[g.BossId];
                    Assert.True(Reaches(grid, from.CenterCx, from.CenterCz, to.CenterCx, to.CenterCz), $"{ctx}: final boss unreachable");
                }
            }
        }
        foreach (var seed in Seeds)
        {
            var widths = new[] { 1, 2, 3 }.Select(s => WalkGridGen.Emit(Gen(seed, s), DungeonGenParams.Default with { Segments = s }).Width).ToList();
            Assert.True(widths[0] < widths[1] && widths[1] < widths[2], $"seed {seed}: grid widths {string.Join(",", widths)} do not grow with S");
        }
    }

    // The single-fork generator's whole output for a seed: nodes (pre-segment fields), edges, grid size, digest and rooms.
    private static uint Snapshot(long seed, DungeonGenParams p)
    {
        var g = RoomGraphGen.Generate(seed, p);
        var grid = WalkGridGen.Emit(g, p);
        var s = string.Join(";", g.Nodes.Select(n => string.Create(CultureInfo.InvariantCulture, $"{n.Id},{n.Layer},{n.Lane},{(int)n.Kind}")))
            + "#" + g.Signature()
            + "#" + string.Create(CultureInfo.InvariantCulture, $"{grid.Width}x{grid.Height}:{grid.Digest()}")
            + "#" + string.Join(";", grid.Rooms.Select(r => string.Create(CultureInfo.InvariantCulture, $"{r.RoomId},{r.MinCx},{r.MinCz},{r.MaxCx},{r.MaxCz}")));
        return Fnv1a.Append(Fnv1a.OffsetBasis, s);
    }

    private static bool Overlap(RoomRect a, RoomRect b) =>
        a.MinCx <= b.MaxCx && b.MinCx <= a.MaxCx && a.MinCz <= b.MaxCz && b.MinCz <= a.MaxCz;

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
