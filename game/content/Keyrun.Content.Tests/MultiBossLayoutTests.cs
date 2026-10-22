using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// R-MULTIBOSS-1: a dungeon with N segments has N boss pulls (<see cref="LayoutDef.BossPullIds"/>: "boss-s1", ..., then
/// "boss"), and <see cref="LayoutValidator"/> holds a layout to all N: every boss pull in exactly one room, in fight order
/// along the route, each room checked as a boss room. The loaded Hollow Vault (one segment) keeps its single "boss".
/// </summary>
public sealed class MultiBossLayoutTests
{
    private const int ArenaShiftX = 30000;

    private static DungeonSegment Segment(string id, string bossId) =>
        new() { Id = id, Boss = new BossEncounter { EnemyId = bossId, Mechanics = new ValueList<string>(new[] { "m" }) } };

    /// <summary>
    /// The Hollow Vault with a second segment bolted on: its warden arena becomes the first boss's room ("boss-s1") and a
    /// corridor and a second arena (the same size, 30 m east) hold the final boss ("boss").
    /// </summary>
    private static ContentRegistry TwoBossVault(bool secondBossRoom = true)
    {
        var reg = ContentLoader.Load();
        var vault = reg.Dungeons["hollow-vault"];
        var arena = reg.Layout.Boxes.Single(b => b.Pull == LayoutDef.BossPullId);
        var boxes = reg.Layout.Boxes.Select(b => b == arena ? b with { Pull = "boss-s1" } : b).ToList();
        var route = reg.Layout.Route.ToList();
        if (secondBossRoom)
        {
            var corridor = new LayoutBox { Id = "corridor-5", Kind = LayoutBoxKind.Corridor, MinX = arena.MaxX - 1000, MaxX = arena.MaxX + 3000, MinZ = -1500, MaxZ = 1500 };
            var second = arena with
            {
                Id = "second-arena",
                Pull = LayoutDef.BossPullId,
                MinX = arena.MaxX + 2000,
                MaxX = arena.MaxX + 2000 + (arena.MaxX - arena.MinX),
                Trigger = new LayoutRect { MinX = arena.MaxX + 4000, MaxX = arena.MaxX + 2000 + (arena.MaxX - arena.MinX), MinZ = arena.MinZ, MaxZ = arena.MaxZ },
                Origin = new LayoutPoint { X = arena.MaxX + 2000 + 11500, Z = 0 },
                Start = new LayoutPoint { X = arena.MaxX + 2000 + 4500, Z = 0 },
            };
            boxes.Add(corridor);
            boxes.Add(second);
            route[^1] = new LayoutPoint { X = arena.MaxX - 500, Z = 0 };
            route.Add(new LayoutPoint { X = arena.MaxX + 2500, Z = 0 });
            route.Add(new LayoutPoint { X = second.Origin!.X, Z = 0 });
        }
        var warden = vault.Boss;
        var segments = new[] { Segment("s1", warden.EnemyId), Segment("s2", warden.EnemyId) };
        var twoSegments = vault with { Segments = new ValueList<DungeonSegment>(segments) };
        return reg with
        {
            Layout = reg.Layout with { Boxes = new ValueList<LayoutBox>(boxes), Route = new ValueList<LayoutPoint>(route) },
            Dungeons = new ValueMap<DungeonDef>(reg.Dungeons.Select(kv => kv.Key == "hollow-vault" ? new KeyValuePair<string, DungeonDef>(kv.Key, twoSegments) : kv)),
        };
    }

    [Fact(DisplayName = "Boss pull ids: one segment is just \"boss\"; N segments are boss-<segment> for each but the last, then \"boss\"")]
    public void Boss_pull_ids_follow_the_segments()
    {
        var reg = ContentLoader.Load();
        Assert.Equal(new[] { "boss" }, LayoutDef.BossPullIds(reg.Dungeons["hollow-vault"]));
        Assert.Equal(new[] { "boss-s1", "boss-s2", "boss" }, LayoutDef.BossPullIds(reg.Dungeons["sunken-bellfoundry"]));
        Assert.True(LayoutDef.IsBossPull("boss") && LayoutDef.IsBossPull("boss-s2"));
        Assert.False(LayoutDef.IsBossPull("choir") || LayoutDef.IsBossPull(null) || LayoutDef.IsBossPull("bossy"));
    }

    [Fact(DisplayName = "LayoutValidator: a two-segment dungeon whose layout holds both boss rooms passes")]
    public void Two_boss_rooms_pass()
    {
        LayoutValidator.Check(TwoBossVault());
    }

    [Fact(DisplayName = "LayoutValidator: a two-segment dungeon whose layout has only one boss room fails closed naming the missing boss pull")]
    public void A_missing_boss_room_is_rejected()
    {
        var ex = Assert.Throws<ContentLoadException>(() => LayoutValidator.Check(TwoBossVault(secondBossRoom: false)));
        Assert.Contains("pull \"boss\" has no room", ex.Message);
    }

    [Fact(DisplayName = "LayoutValidator: boss rooms out of fight order along the route are rejected")]
    public void Boss_rooms_out_of_order_are_rejected()
    {
        var reg = TwoBossVault();
        var swapped = reg.Layout.Boxes.Select(b => b.Pull switch { "boss-s1" => b with { Pull = "boss" }, "boss" => b with { Pull = "boss-s1" }, _ => b }).ToList();
        var ex = Assert.Throws<ContentLoadException>(() => LayoutValidator.Check(reg with { Layout = reg.Layout with { Boxes = new ValueList<LayoutBox>(swapped) } }));
        Assert.Contains("is not after pull", ex.Message);
    }

    [Fact(DisplayName = "LayoutValidator: the single-segment Hollow Vault still loads with exactly one boss pull")]
    public void The_hollow_vault_keeps_one_boss()
    {
        var reg = ContentLoader.Load();
        Assert.Single(reg.Layout.Boxes, b => LayoutDef.IsBossPull(b.Pull));
        LayoutValidator.Check(reg);
    }
}
