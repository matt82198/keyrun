using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// D65/D66/D67, the second dungeon (Matt 2026-10-22: "at least 1 other dungeon with multiple bosses", "any dungeon past
/// tutorial should have 3 bosses" "min", "huge pulls are fun"): the Sunken Bellfoundry's definition, its roster and its
/// forces, against the generator's Segments=3 shape (feat/dungeon-segments: each segment forks into two branches of 2 or
/// 3 pack rooms, 5 to 7 pack rooms per segment), and the loader's fail-closed rules for the new dungeon shape.
/// </summary>
public sealed class SunkenBellfoundryContentTests
{
    private const string Id = "sunken-bellfoundry";
    private const string File = "dungeons/sunken-bellfoundry.json";

    /// <summary>The generator's shape: three segments (one boss each), and the shorter branch of a fork has 2 pack rooms.</summary>
    private const int Segments = 3;

    private const int MinRoomsPerBranch = 2;

    private static readonly ContentRegistry Reg = ContentLoader.Load();

    /// <summary>The five trash kinds new to the game (enemies/bellfoundry.json).</summary>
    private static readonly string[] NewTrash =
    {
        "bellfoundry.slagRam", "bellfoundry.tollingBell", "bellfoundry.furnaceThrall", "bellfoundry.scaldHound", "bellfoundry.brineMender",
    };

    private static DungeonDef Dungeon => Reg.Dungeons[Id];

    private static IReadOnlyList<RosterPack> Pool => Dungeon.PackPool.Select(p => Reg.Roster[p]).ToList();

    private static int Points(RosterPack pack) => pack.Enemies.Sum(g => g.Count * Reg.Forces.PointsOf(g.EnemyId));

    private static ContentLoadException LoadFails(MutableSource source) =>
        Assert.Throws<ContentLoadException>(() => ContentLoader.Load(source.Build()));

    [Fact(DisplayName = "D65/D66: the Sunken Bellfoundry is seeded, three segments, one boss each, the last its final boss, par 24000")]
    public void The_dungeon_has_three_segments_with_a_boss_each()
    {
        var d = Dungeon;
        Assert.Equal("The Sunken Bellfoundry", d.Name);
        Assert.False(d.Tutorial);
        Assert.Empty(d.Pulls);
        Assert.Equal(24000, d.ParTicks);
        Assert.Equal(new[] { "s1", "s2", "s3" }, d.Segments.Select(s => s.Id));
        Assert.Equal(new[] { "bellfoundry.ommet", "bellfoundry.brineMother", "bellfoundry.greatBell" }, d.Segments.Select(s => s.Boss.EnemyId));
        Assert.Equal(d.Segments[^1].Boss, d.Boss);
        Assert.All(d.Segments, s => Assert.Equal(EnemyRole.Boss, Reg.Enemies[s.Boss.EnemyId].Role));
        Assert.All(d.Segments, s => Assert.InRange(s.Boss.Mechanics.Count, 2, 3));
    }

    [Fact(DisplayName = "D67: the Hollow Vault has the same shape: the tutorial, one segment, its one boss, its layout packs as its pool")]
    public void The_hollow_vault_is_the_tutorial_with_the_same_shape()
    {
        var hv = Reg.Dungeons["hollow-vault"];
        Assert.True(hv.Tutorial);
        Assert.Equal(14400, hv.ParTicks);
        var segment = Assert.Single(hv.Segments);
        Assert.Equal(hv.Boss, segment.Boss);
        Assert.Equal(Reg.Layout.Packs.Select(p => p.Id).OrderBy(p => p, StringComparer.Ordinal), hv.PackPool.OrderBy(p => p, StringComparer.Ordinal));
    }

    [Fact(DisplayName = "D66: at least ten roster packs, every one teaching something, mixing the five new kinds")]
    public void The_pool_has_ten_teaching_packs_that_field_every_new_kind()
    {
        Assert.True(Pool.Count >= 10, $"pool has {Pool.Count} packs");
        Assert.All(Pool, p => Assert.Equal(Id, p.Dungeon));
        Assert.All(Pool, p => Assert.False(string.IsNullOrWhiteSpace(p.Teaches), p.Id + " teaches nothing"));
        Assert.Equal(Reg.Roster.Values.Where(p => p.Dungeon == Id).Select(p => p.Id).OrderBy(i => i, StringComparer.Ordinal),
            Dungeon.PackPool.OrderBy(i => i, StringComparer.Ordinal));
        foreach (var kind in NewTrash)
        {
            var packs = Pool.Count(p => p.Enemies.Any(g => g.EnemyId == kind));
            Assert.True(packs >= 2, $"{kind} is in {packs} packs, want at least 2");
        }
        // Reuse two or three existing kinds where it fits (the brief): the Hollow Vault's casters and Brute come along.
        var reused = Pool.SelectMany(p => p.Enemies).Select(g => g.EnemyId).Where(e => !e.StartsWith("bellfoundry.", StringComparison.Ordinal)).Distinct().Count();
        Assert.InRange(reused, 2, 5);
    }

    [Fact(DisplayName = "D66 forces: the shortest route of three segments (two pack rooms a branch) on the smallest packs still passes 100%")]
    public void Every_route_over_three_segments_reaches_the_forces_bar()
    {
        var points = Pool.Select(Points).ToList();
        Assert.All(points, p => Assert.True(p > 0));
        var shortest = Segments * MinRoomsPerBranch;
        var worst = points.Min() * shortest;
        Assert.True(worst > Reg.Forces.Required,
            $"{shortest} rooms of the smallest pack ({points.Min()}) give {worst}, not above the {Reg.Forces.Required} bar");
        // And no pack is a whole segment on its own: big pulls are chosen by chaining, not handed out by one pack.
        Assert.True(points.Max() * Segments < Reg.Forces.Required, $"the biggest pack ({points.Max()}) clears a third of the bar alone");
    }

    [Fact(DisplayName = "D66 huge pulls: at least three pack pairs are built to be chain-pulled, both ways, with a note")]
    public void At_least_three_chain_pairs_with_a_note()
    {
        var pairs = Pool
            .SelectMany(p => (p.ChainWith ?? new ValueList<string>()).Select(o => string.CompareOrdinal(p.Id, o) < 0 ? (p.Id, o) : (o, p.Id)))
            .Distinct()
            .ToList();
        Assert.True(pairs.Count >= 3, $"{pairs.Count} chain pairs");
        foreach (var (a, b) in pairs)
        {
            Assert.Contains(b, Reg.Roster[a].ChainWith!);
            Assert.Contains(a, Reg.Roster[b].ChainWith!);
            Assert.True(!string.IsNullOrWhiteSpace(Reg.Roster[a].ChainNote) || !string.IsNullOrWhiteSpace(Reg.Roster[b].ChainNote), $"{a}+{b} has no chain note");
        }
    }

    [Fact(DisplayName = "A seeded dungeon with fewer than three segments fails the load")]
    public void Two_segments_past_the_tutorial_fail()
    {
        var ex = LoadFails(new MutableSource().Edit(File, n => n["segments"]!.AsArray().RemoveAt(0)));
        Assert.Contains("needs at least 3", ex.Message);
    }

    [Fact(DisplayName = "A dungeon whose final boss is not its last segment's boss fails the load")]
    public void Final_boss_must_close_the_last_segment()
    {
        var ex = LoadFails(new MutableSource().Edit(File, n => n["boss"]!["enemyId"] = "bellfoundry.ommet"));
        Assert.Contains("is not its last segment's boss", ex.Message);
    }

    [Fact(DisplayName = "A segment boss that is not a boss fails the load")]
    public void Segment_boss_must_be_a_boss()
    {
        var ex = LoadFails(new MutableSource().Edit(File, n => n["segments"]![0]!["boss"]!["enemyId"] = "bellfoundry.slagRam"));
        Assert.Contains("not boss", ex.Message);
    }

    [Fact(DisplayName = "A pack pool id that names no roster pack of the dungeon fails the load")]
    public void Unknown_pool_pack_fails()
    {
        var ex = LoadFails(new MutableSource().Edit(File, n => n["packPool"]!.AsArray().Add("no-such-pack")));
        Assert.Contains("no-such-pack", ex.Message);
    }

    [Fact(DisplayName = "A chain partner that does not chain back fails the load")]
    public void One_way_chain_fails()
    {
        var ex = LoadFails(new MutableSource().Edit("roster/pour-line.json", n => n.AsObject().Remove("chainWith")));
        Assert.Contains("does not chain back", ex.Message);
    }

    [Fact(DisplayName = "A roster pack that teaches nothing fails the load")]
    public void Pack_without_a_lesson_fails()
    {
        var ex = LoadFails(new MutableSource().Edit("roster/pour-line.json", n => n["teaches"] = " "));
        Assert.Contains("teaches nothing", ex.Message);
    }

    [Fact(DisplayName = "A roster pack member with no forces value fails the load (R-PACKS-7)")]
    public void Roster_member_without_a_value_fails()
    {
        var ex = LoadFails(new MutableSource().Remove("forces/values/bellfoundry.slagRam.json"));
        Assert.Contains("has no forces value", ex.Message);
    }

    [Fact(DisplayName = "SCRIPT-GATE: a when with both conditions, or an unknown effect, fails the load")]
    public void Script_gate_is_checked()
    {
        const string enemies = "enemies/bellfoundry.json";
        static JsonNode Thrall(JsonNode n) => n.AsArray().First(e => (string?)e!["id"] == "bellfoundry.furnaceThrall")!;
        var both = LoadFails(new MutableSource().Edit(enemies, n => Thrall(n)["script"]![2]!["when"]!["healthBelowBp"] = 3000));
        Assert.Contains("exactly one of hasEffect and healthBelowBp", both.Message);
        var unknown = LoadFails(new MutableSource().Edit(enemies, n => Thrall(n)["script"]![2]!["when"]!["hasEffect"] = "noSuchEffect"));
        Assert.Contains("unknown effect \"noSuchEffect\"", unknown.Message);
    }
}
