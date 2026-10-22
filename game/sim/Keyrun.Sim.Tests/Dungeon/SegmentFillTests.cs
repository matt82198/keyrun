using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Dungeon;
using Xunit;

namespace Keyrun.Sim.Tests.Dungeon;

/// <summary>
/// The segment-aware fill (STATE.md D65/D67, the Sunken Bellfoundry): over a multi-segment room graph every trash room of
/// every segment holds a pack from the dungeon's pool, keyed by (segment, branch); each segment's boss stands in that
/// segment's boss room; and the forces are priced so every route (one branch per segment) reaches the bar with the
/// untaken branches' packs optional (WoW Mythic+: the bar is reachable on either branch, both together exceed it).
/// Properties over many seeds and every segment count, never a pinned hash.
/// </summary>
public sealed class SegmentFillTests
{
    // The Sunken Bellfoundry's 11 pool packs, priced as game/content/data/roster is under forces/values (5 to 8 mobs each).
    internal static readonly IReadOnlyList<RosterPack> Pool = new[]
    {
        new RosterPack("slag-haulers", 240),
        new RosterPack("pour-line", 230),
        new RosterPack("tolling-shift", 175),
        new RosterPack("scald-kennel", 175),
        new RosterPack("furnace-stokers", 215),
        new RosterPack("bellows-shift", 215),
        new RosterPack("brine-chapel", 235),
        new RosterPack("drowned-choir", 205),
        new RosterPack("crucible-guard", 235),
        new RosterPack("ash-and-brine", 185),
        new RosterPack("foundry-floor", 230),
    };

    internal static readonly IReadOnlyList<string> Bosses = new[] { "bellfoundry.ommet", "bellfoundry.brineMother", "bellfoundry.greatBell" };

    private static readonly long[] Seeds = Enumerable.Range(1, 40).Select(i => (long)i).ToArray();

    private static RoomGraph Graph(long seed, int segments) =>
        RoomGraphGen.Generate(seed, DungeonGenParams.Default with { Segments = segments });

    private static IReadOnlyList<string> BossIds(int segments) => Bosses.Take(segments).ToList();

    // The bar scales with the segment count so a one- or two-segment graph (fewer rooms) can still reach it; three is 1000.
    private static int Bar(int segments) => segments == 3 ? 1000 : 330 * segments;

    private static IEnumerable<(long Seed, int Segments, RoomGraph Graph, SegmentFillResult Fill)> All()
    {
        foreach (var segments in new[] { 1, 2, 3 })
        {
            foreach (var seed in Seeds)
            {
                var g = Graph(seed, segments);
                yield return (seed, segments, g, SegmentFill.Fill(seed, g, BossIds(segments), Pool, Bar(segments)));
            }
        }
    }

    [Fact(DisplayName = "Segment fill: every trash room of every segment holds one pool pack, and no other room holds any")]
    public void Every_trash_room_has_a_pool_pack()
    {
        var ids = Pool.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var (seed, _, g, fill) in All())
        {
            var trash = g.Nodes.Where(n => n.Kind == RoomKind.Trash).Select(n => n.Id).OrderBy(i => i).ToList();
            Assert.Equal(trash, fill.Packs.Select(p => p.RoomId).OrderBy(i => i).ToList());
            Assert.All(fill.Packs, p => Assert.Contains(p.PackId, ids));
            Assert.All(fill.Packs, p => Assert.Equal(Pool.Single(r => r.Id == p.PackId).Points, p.Points));
            Assert.True(fill.Packs.Count > 0, $"seed {seed}");
        }
    }

    [Fact(DisplayName = "Segment fill: a pack carries its room's (segment, branch) key")]
    public void Packs_are_keyed_by_segment_and_branch()
    {
        foreach (var (seed, _, g, fill) in All())
        {
            foreach (var p in fill.Packs)
            {
                var room = g.Nodes[p.RoomId];
                Assert.True(room.Segment == p.Segment && room.Branch == p.Branch, $"seed {seed}: room {p.RoomId} is ({room.Segment},{room.Branch}), pack says ({p.Segment},{p.Branch})");
            }
        }
    }

    [Fact(DisplayName = "Segment fill: every segment has at least one filled trash room on each branch, and the branch summary lists its rooms in walking order")]
    public void Every_segment_branch_is_filled()
    {
        foreach (var (seed, segments, g, fill) in All())
        {
            Assert.Equal(segments * RoomGraphGen.BranchCount, fill.Branches.Count);
            for (var s = 0; s < segments; s++)
            {
                for (var b = 0; b < RoomGraphGen.BranchCount; b++)
                {
                    var branch = fill.Branches.Single(x => x.Segment == s && x.Branch == b);
                    var rooms = g.Nodes.Where(n => n.Kind == RoomKind.Trash && n.Segment == s && n.Branch == b).OrderBy(n => n.Layer).Select(n => n.Id).ToList();
                    Assert.Equal(rooms, branch.Rooms);
                    Assert.True(rooms.Count >= 1, $"seed {seed} segment {s} branch {b} has no trash room");
                    Assert.Equal(rooms.Count, fill.Packs.Count(p => p.Segment == s && p.Branch == b));
                    Assert.Equal(fill.Packs.Where(p => p.Segment == s && p.Branch == b).Sum(p => p.Points), branch.Points);
                }
            }
        }
    }

    [Fact(DisplayName = "Segment fill: exactly one boss per segment, in that segment's boss room, from the dungeon's segment list; the last is the final boss")]
    public void Each_segment_has_its_boss_in_its_boss_room()
    {
        foreach (var (seed, segments, g, fill) in All())
        {
            Assert.Equal(segments, fill.Bosses.Count);
            for (var s = 0; s < segments; s++)
            {
                var boss = Assert.Single(fill.Bosses, b => b.Segment == s);
                Assert.Equal(g.BossRooms[s], boss.RoomId);
                Assert.Equal(Bosses[s], boss.BossId);
                Assert.Equal(RoomKind.Boss, g.Nodes[boss.RoomId].Kind);
            }
            Assert.Equal(g.BossId, fill.Bosses[^1].RoomId);
            Assert.Equal(segments, fill.Bosses.Select(b => b.RoomId).Distinct().Count());
            Assert.True(fill.Bosses.Select(b => b.RoomId).SequenceEqual(fill.Bosses.Select(b => b.RoomId).OrderBy(i => i)), $"seed {seed}: bosses out of order");
        }
    }

    [Fact(DisplayName = "Segment fill: every route (one branch per segment) reaches the forces bar, and the untaken branches add optional packs on top")]
    public void Every_route_reaches_the_bar_with_optional_packs_beside_it()
    {
        foreach (var (seed, segments, _, fill) in All())
        {
            var min = int.MaxValue;
            for (var mask = 0; mask < 1 << segments; mask++)
            {
                var pick = Enumerable.Range(0, segments).Select(s => (mask >> s) & 1).ToList();
                var points = fill.RoutePoints(pick);
                min = Math.Min(min, points);
                // 1000 points = 100% (BIBLE s9.2) for the three-segment dungeon.
                Assert.True(points >= Bar(segments), $"seed {seed} route {string.Join("", pick)}: {points} of {Bar(segments)}");
            }
            Assert.Equal(min, fill.MinRoutePoints);
            Assert.True(fill.TotalPoints > fill.MinRoutePoints, $"seed {seed}: both branches together ({fill.TotalPoints}) must exceed one route ({fill.MinRoutePoints})");
            Assert.Equal(fill.Packs.Sum(p => p.Points), fill.TotalPoints);
        }
    }

    [Fact(DisplayName = "Segment fill: the bar the route must reach is a parameter; a thin pool fails closed instead of shipping an unreachable bar")]
    public void A_pool_too_thin_for_the_bar_throws()
    {
        var g = Graph(1, 3);
        Assert.Throws<InvalidOperationException>(() => SegmentFill.Fill(1, g, BossIds(3), new[] { new RosterPack("tiny", 10) }, 1000));
        Assert.Throws<InvalidOperationException>(() => SegmentFill.Fill(1, g, BossIds(3), Pool, forcesRequired: 100000));
        var easy = SegmentFill.Fill(1, g, BossIds(3), Pool, forcesRequired: 500);
        Assert.True(easy.MinRoutePoints >= 500);
    }

    [Fact(DisplayName = "Segment fill: a route short of a high bar is lifted by upgrading its lightest packs, never past what the pool can carry")]
    public void A_short_route_is_lifted_to_the_bar()
    {
        const int Bar = 1350;
        var lifted = 0;
        var filled = 0;
        foreach (var seed in Seeds)
        {
            var g = Graph(seed, 3);
            var plain = SegmentFill.Fill(seed, g, BossIds(3), Pool, 1);
            try
            {
                var fill = SegmentFill.Fill(seed, g, BossIds(3), Pool, Bar);
                filled++;
                Assert.True(fill.MinRoutePoints >= Bar, $"seed {seed}: {fill.MinRoutePoints} of {Bar}");
                Assert.All(fill.Packs, p => Assert.Equal(Pool.Single(r => r.Id == p.PackId).Points, p.Points));
                if (plain.MinRoutePoints < Bar)
                {
                    lifted++;
                    Assert.NotEqual(plain.Packs, fill.Packs);
                }
            }
            catch (InvalidOperationException)
            {
                Assert.True(plain.MaxRoutePoints < int.MaxValue);
            }
        }
        Assert.True(lifted >= 3, $"only {lifted} of {Seeds.Length} seeds needed a lift ({filled} filled)");
    }

    [Fact(DisplayName = "Segment fill: a segment never repeats a pack while the pool is bigger than its rooms")]
    public void A_segment_deals_distinct_packs()
    {
        foreach (var (seed, _, g, fill) in All())
        {
            foreach (var group in fill.Packs.GroupBy(p => p.Segment))
            {
                Assert.True(group.Count() <= Pool.Count);
                Assert.Equal(group.Count(), group.Select(p => p.PackId).Distinct(StringComparer.Ordinal).Count());
            }
            Assert.NotNull(g);
            Assert.True(seed > 0);
        }
    }

    [Fact(DisplayName = "Segment fill: pure over (seed, graph, bosses, pool); seeds deal different dungeons")]
    public void Deterministic_and_seeded()
    {
        var g = Graph(7, 3);
        var a = SegmentFill.Fill(7, g, BossIds(3), Pool, 1000);
        var b = SegmentFill.Fill(7, g, BossIds(3), Pool, 1000);
        Assert.Equal(a.Packs, b.Packs);
        Assert.Equal(a.Bosses, b.Bosses);
        var deals = Seeds.Select(s => string.Join(",", SegmentFill.Fill(s, g, BossIds(3), Pool, 1000).Packs.Select(p => p.PackId))).Distinct().Count();
        Assert.True(deals > 20, $"{deals} distinct deals over {Seeds.Length} seeds");
    }

    [Fact(DisplayName = "Segment fill: fails closed on a boss list that is not one per segment, a bad pool or a null input")]
    public void Bad_input_fails_closed()
    {
        var g = Graph(1, 3);
        Assert.Throws<ArgumentException>(() => SegmentFill.Fill(1, g, BossIds(2), Pool));
        Assert.Throws<ArgumentException>(() => SegmentFill.Fill(1, g, new[] { "a", "", "c" }, Pool));
        Assert.Throws<ArgumentException>(() => SegmentFill.Fill(1, g, BossIds(3), Array.Empty<RosterPack>()));
        Assert.Throws<ArgumentException>(() => SegmentFill.Fill(1, g, BossIds(3), new[] { new RosterPack("a", 100), new RosterPack("a", 100) }));
        Assert.Throws<ArgumentNullException>(() => SegmentFill.Fill(1, null!, BossIds(3), Pool));
        Assert.Throws<ArgumentException>(() => SegmentFill.Fill(1, g, BossIds(3), Pool, forcesRequired: 0));
    }
}
