using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Sim.Dungeon;
using Xunit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// The Sunken Bellfoundry through the segment fill (STATE.md D65/D67): the pool is the dungeon's packPool priced by forces
/// values, the bosses are its segment list, every trash room of the three segments holds a pool pack, every route reaches the
/// forces bar, and <see cref="SeededLayout.Walk"/> carries the bots through segment 1's boss, segment 2's boss and the final
/// boss in that order, each boss standing in the boss room the walk enters.
/// </summary>
public sealed class SegmentedFillContentTests
{
    private const string Dungeon = "sunken-bellfoundry";

    private static readonly long[] Seeds = Enumerable.Range(1, 60).Select(i => (long)i).ToArray();

    [Fact(DisplayName = "Segmented fill content: the pool is the dungeon's packPool in order, priced by forces values; the bosses are its segment list")]
    public void Pool_and_bosses_come_from_content()
    {
        var reg = ContentLoader.Load();
        var def = reg.Dungeons[Dungeon];
        var pool = SeededRoster.Pool(reg, Dungeon);
        Assert.Equal(def.PackPool, pool.Select(p => p.Id));
        Assert.All(pool, p => Assert.Equal(reg.Roster[p.Id].Enemies.Sum(g => g.Count * reg.Forces.PointsOf(g.EnemyId)), p.Points));
        Assert.All(pool, p => Assert.True(p.Points > 0));
        Assert.Equal(new[] { "bellfoundry.ommet", "bellfoundry.brineMother", "bellfoundry.greatBell" }, SeededRoster.SegmentBosses(reg, Dungeon));
        Assert.Equal(def.Boss.EnemyId, SeededRoster.SegmentBosses(reg, Dungeon)[^1]);
    }

    [Fact(DisplayName = "Segmented fill content: three segments, every trash room holds a pool pack, every route reaches the bar and both branches together exceed it")]
    public void Every_route_reaches_the_bar_from_content()
    {
        var reg = ContentLoader.Load();
        var pool = reg.Dungeons[Dungeon].PackPool.ToHashSet(StringComparer.Ordinal);
        foreach (var seed in Seeds)
        {
            var d = SeededLayout.BuildSegmented(reg, Dungeon, seed);
            Assert.Equal(3, d.Graph.SegmentCount);
            var trash = d.Graph.Nodes.Where(n => n.Kind == RoomKind.Trash).Select(n => n.Id).OrderBy(i => i).ToList();
            Assert.Equal(trash, d.Fill.Packs.Select(p => p.RoomId).OrderBy(i => i).ToList());
            Assert.All(d.Fill.Packs, p => Assert.Contains(p.PackId, pool));
            for (var mask = 0; mask < 8; mask++)
            {
                var pick = new[] { mask & 1, (mask >> 1) & 1, (mask >> 2) & 1 };
                var points = d.Fill.RoutePoints(pick);
                Assert.True(points >= reg.ForcesFor(Dungeon).Required, $"seed {seed} route {string.Join("", pick)}: {points} of {reg.ForcesFor(Dungeon).Required}");
            }
            Assert.True(d.Fill.TotalPoints > d.Fill.MaxRoutePoints, $"seed {seed}: the untaken branches add nothing");
        }
    }

    [Fact(DisplayName = "Segmented fill content: the pool packs are big pulls, 5 to 8 mobs (the fill places them whole)")]
    public void Placed_packs_are_big_pulls()
    {
        var reg = ContentLoader.Load();
        foreach (var seed in Seeds)
        {
            var d = SeededLayout.BuildSegmented(reg, Dungeon, seed);
            Assert.All(d.Fill.Packs, p => Assert.InRange(reg.Roster[p.PackId].Enemies.Sum(g => g.Count), 5, 8));
        }
    }

    [Fact(DisplayName = "Segmented fill content: the walk enters segment 1's boss room, segment 2's, then the final boss's, each holding its segment's boss")]
    public void Walk_carries_the_party_through_the_three_bosses_in_order()
    {
        var reg = ContentLoader.Load();
        var bosses = SeededRoster.SegmentBosses(reg, Dungeon);
        foreach (var seed in Seeds)
        {
            var d = SeededLayout.BuildSegmented(reg, Dungeon, seed);
            Assert.Equal(d.Graph.BossRooms, d.Fill.Bosses.Select(b => b.RoomId));
            Assert.Equal(bosses, d.Fill.Bosses.Select(b => b.BossId));
            var entered = d.Walk.Chain.Where(id => d.Graph.Nodes[id].Kind == RoomKind.Boss).ToList();
            Assert.Equal(d.Graph.BossRooms, entered);
            Assert.Equal(d.Graph.BossId, d.Walk.Chain[^1]);
            // Between two bosses the walk takes the segment's chosen branch, and every trash room on it holds a pack.
            var chain = d.Walk.Chain.ToList();
            for (var s = 0; s < 3; s++)
            {
                var from = s == 0 ? 0 : chain.IndexOf(d.Graph.BossRooms[s - 1]);
                var to = chain.IndexOf(d.Graph.BossRooms[s]);
                var trash = chain.Skip(from + 1).Take(to - from - 1).ToList();
                Assert.NotEmpty(trash);
                Assert.All(trash, id => Assert.Equal(d.Branches[s], d.Graph.Nodes[id].Branch));
                Assert.All(trash, id => Assert.Contains(d.Fill.Packs, p => p.RoomId == id));
            }
            // The untaken branches hold packs too (optional forces beside the route).
            var taken = d.Walk.Chain.ToHashSet();
            Assert.Contains(d.Fill.Packs, p => !taken.Contains(p.RoomId));
        }
    }
}
