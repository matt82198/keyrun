using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Sim.Dungeon;
using Xunit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// The shipped content through the branch fill (D45/D46, f-46043bc6): the teaching pulls and the optional packs come
/// from content (dungeon pulls, layout packs, forces values; R-PACKS-7 forces ship with the packs that place them) and
/// the placement rule from the sim (<see cref="BranchFill"/>). Per seed and per 2/3-room branch shape: the dungeon
/// carries more than 100% forces, each branch reaches the bar alone and carries at least one teaching pull, and no room
/// holds two packs of one pull index.
/// </summary>
public sealed class SeededFillContentTests
{
    private static readonly long[] Seeds = Enumerable.Range(1, 100).Select(i => (long)i).ToArray();

    // The seeded generator's graph (RoomGraphGen: one fork after the entrance, two branches of 2 or 3 rooms).
    private static IEnumerable<(long Seed, BranchFillResult Fill)> Fills(ContentRegistry reg)
    {
        var teaching = SeededRoster.Teaching(reg);
        var optional = SeededRoster.Optional(reg);
        var p = SeededRoster.Params(reg);
        foreach (var seed in Seeds)
        {
            yield return (seed, BranchFill.Fill(seed, RoomGraphGen.Generate(seed, DungeonGenParams.Default), teaching, optional, p));
        }
    }

    [Fact(DisplayName = "Seeded fill content: the roster is the dungeon's teaching pulls and the layout's optional packs, priced by forces values")]
    public void Roster_comes_from_content()
    {
        var reg = ContentLoader.Load();
        var teaching = SeededRoster.Teaching(reg);
        Assert.Equal(new[] { ("choir", 0, 160), ("pair", 1, 260), ("seed", 2, 180) }, teaching.Select(t => (t.Id, t.PullIndex, t.Points)).ToArray());
        var optional = SeededRoster.Optional(reg);
        Assert.Equal(
            new[] { ("cantor-vigil", 400), ("hall-sentries", 240), ("warrior-remnant", 392) },
            optional.Select(o => (o.Id, o.Points)).OrderBy(o => o.Id, StringComparer.Ordinal).ToArray());
        Assert.Equal(reg.Forces.Required, SeededRoster.Params(reg).ForcesRequired);
        // The branch target is the bar plus the biggest optional pack (400, the Cantor Vigil; R-REMNANT-2 took the Remnant from 420 to 392), so any one optional pack is skippable.
        Assert.Equal(1400, SeededRoster.Params(reg).TargetPoints);
    }

    [Fact(DisplayName = "Seeded fill content: per seed every branch still reaches the bar with any one optional pack skipped")]
    public void Any_one_optional_pack_is_skippable()
    {
        var reg = ContentLoader.Load();
        foreach (var (seed, fill) in Fills(reg))
        {
            foreach (var br in fill.Branches)
            {
                var own = fill.Packs.Where(p => br.Rooms.Contains(p.RoomId)).ToList();
                Assert.All(own.Where(p => p.PullIndex < 0), p => Assert.True(
                    br.Points - p.Points >= reg.Forces.Required,
                    $"seed {seed}: branch {br.Index} falls to {br.Points - p.Points} without {p.InstanceId}"));
            }
        }
    }

    [Fact(DisplayName = "Seeded fill content: per seed the dungeon carries more than 100 percent and each branch reaches the bar alone")]
    public void Forces_sum_over_100_per_seed()
    {
        var reg = ContentLoader.Load();
        var required = reg.Forces.Required;
        foreach (var (seed, fill) in Fills(reg))
        {
            Assert.True(fill.TotalPoints > required, $"seed {seed}: dungeon carries {fill.TotalPoints} of {required}");
            Assert.All(fill.Branches, br => Assert.True(br.Points >= required, $"seed {seed}: branch {br.Index} carries {br.Points} of {required}"));
        }
    }

    [Fact(DisplayName = "Seeded fill content: each branch carries at least one teaching pull")]
    public void Each_branch_carries_a_teaching_pull()
    {
        var reg = ContentLoader.Load();
        foreach (var (seed, fill) in Fills(reg))
        {
            Assert.All(fill.Branches, br => Assert.True(
                fill.Packs.Count(p => br.Rooms.Contains(p.RoomId) && p.PullIndex >= 0) >= 1,
                $"seed {seed}: branch {br.Index} teaches nothing"));
        }
    }

    [Fact(DisplayName = "Seeded fill content: every room has at most one pack per pull index")]
    public void At_most_one_pack_per_pull_index_per_room()
    {
        var reg = ContentLoader.Load();
        foreach (var (seed, fill) in Fills(reg))
        {
            foreach (var room in fill.Packs.GroupBy(p => p.RoomId))
            {
                var teachingHere = room.Where(p => p.PullIndex >= 0).ToList();
                Assert.True(teachingHere.Count <= 1, $"seed {seed}: room {room.Key} holds pulls {string.Join(",", teachingHere.Select(p => p.PackId))}");
                Assert.Equal(room.Count(), room.Select(p => p.Slot).Distinct().Count());
            }
        }
    }
}
