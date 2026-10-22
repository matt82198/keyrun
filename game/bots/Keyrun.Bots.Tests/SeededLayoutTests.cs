using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Sim.Dungeon;
using Xunit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// The seeded dungeon as the run's layout (D45/D46, f-46043bc6; <see cref="SeededLayout"/>): the walked branch is the
/// route chain (entrance, its corridors and rooms, the boss room), the other branch is drawn beside it with its packs,
/// the branch doors are one-way, and the content's own LayoutValidator accepts the walked chain for every seed.
/// </summary>
public sealed class SeededLayoutTests
{
    private static readonly long[] Seeds = Enumerable.Range(1, 60).Select(i => (long)i).ToArray();

    private static IEnumerable<(long Seed, SeededDungeon D)> All()
    {
        var reg = ContentLoader.Load();
        foreach (var seed in Seeds)
        {
            yield return (seed, SeededLayout.Build(reg, seed));
        }
    }

    [Fact(DisplayName = "Seeded layout: the route walks the entrance, the bots' branch and the boss room; the other branch is drawn beside it")]
    public void Route_is_the_walked_branch_and_the_side_is_the_other()
    {
        foreach (var (seed, d) in All())
        {
            var layout = d.Registry.Layout;
            var walked = d.Fill.Branches[d.Branch].Rooms;
            var other = d.Fill.Branches[1 - d.Branch].Rooms;
            var routeRooms = layout.Boxes.Where(b => b.Kind == LayoutBoxKind.Room).Select(b => b.Id).ToList();
            var expected = new[] { d.Graph.EntranceId }.Concat(walked).Append(d.Graph.BossId).Select(SeededLayout.RoomBoxId).ToList();
            Assert.Equal(expected, routeRooms);
            Assert.Equal(other.Select(SeededLayout.RoomBoxId), layout.SideBoxes.Where(b => b.Kind == LayoutBoxKind.Room).Select(b => b.Id));
            Assert.Equal(layout.Boxes.Count, layout.Route.Count);
            // The side branch's corridors reach from the entrance to the boss room: one more corridor run than rooms.
            Assert.Contains(layout.SideBoxes, b => b.Kind == LayoutBoxKind.Corridor && b.MinX < layout.Boxes[0].MaxX);
            Assert.Contains(layout.SideBoxes, b => b.Kind == LayoutBoxKind.Corridor && b.MaxX > layout.Boxes[^1].MinX);
            Assert.Empty(layout.Boxes.Select(b => b.Id).Intersect(layout.SideBoxes.Select(b => b.Id), StringComparer.Ordinal));
            _ = seed;
        }
    }

    [Fact(DisplayName = "Seeded layout: two exits leave the entrance, each branch begins with a one-way door")]
    public void Two_one_way_exits_leave_the_entrance()
    {
        foreach (var (seed, d) in All())
        {
            var layout = d.Registry.Layout;
            var entrance = layout.Boxes[0];
            var exits = layout.Boxes.Concat(layout.SideBoxes)
                .Where(b => b.Kind == LayoutBoxKind.Corridor && b.MinX < entrance.MaxX && b.MaxX > entrance.MaxX)
                .ToList();
            Assert.True(exits.Count == 2, $"seed {seed}: {exits.Count} exits from the entrance");
            Assert.All(exits, b => Assert.True(b.OneWay, $"seed {seed}: exit {b.Id} is not one-way"));
            Assert.Equal(2, layout.Boxes.Concat(layout.SideBoxes).Count(b => b.OneWay));
            Assert.Equal(1, layout.Boxes.Count(b => b.OneWay));
        }
    }

    // The walked chain alone as a registry: the route boxes, their packs, and the walked branch's two teaching pulls.
    private static ContentRegistry WalkedOnly(SeededDungeon d, LayoutDef? layout = null)
    {
        layout ??= d.Registry.Layout;
        var sideRooms = layout.SideBoxes.Select(b => b.Id).ToHashSet(StringComparer.Ordinal);
        var id = d.Registry.Forces.Route.DungeonId;
        var dungeon = d.Registry.Dungeons[id];
        var walked = dungeon with { Pulls = new ValueList<PullDef>(dungeon.Pulls.Take(2)) };
        return d.Registry with
        {
            Layout = layout with { Packs = new ValueList<LayoutPack>(layout.Packs.Where(p => !sideRooms.Contains(p.Room))), SideBoxes = new() },
            Dungeons = new ValueMap<DungeonDef>(d.Registry.Dungeons.Select(kv => kv.Key == id ? new KeyValuePair<string, DungeonDef>(kv.Key, walked) : kv)),
        };
    }

    [Fact(DisplayName = "Seeded layout: the run's pulls are both branches' teaching pulls, walked branch first, each room tagged with its branch")]
    public void Pulls_cover_both_branches()
    {
        foreach (var (seed, d) in All())
        {
            var dungeon = d.Registry.Dungeons[d.Registry.Forces.Route.DungeonId];
            var second = d.Fill.Branches[d.Branch].SecondPull;
            var otherSecond = d.Fill.Branches[1 - d.Branch].SecondPull;
            Assert.Equal(new[] { "choir", second, "choir" + SeededLayout.MirrorSuffix, otherSecond }, dungeon.Pulls.Select(p => p.Id));
            var layout = d.Registry.Layout;
            Assert.Equal(new[] { "choir", second, LayoutDef.BossPullId }, layout.Boxes.Where(b => b.Pull is not null).Select(b => b.Pull));
            Assert.Equal(new[] { "choir" + SeededLayout.MirrorSuffix, otherSecond }, layout.SideBoxes.Where(b => b.Pull is not null).Select(b => b.Pull));
            // Only the optional packs stay packs: every teaching placement is a pull.
            Assert.Equal(d.Fill.Packs.Count(p => p.PullIndex < 0), layout.Packs.Count);
            var tag = d.Branch.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Assert.All(layout.Boxes.Where(b => b.Kind == LayoutBoxKind.Room).Skip(1).SkipLast(1), b => Assert.Equal(tag, b.Branch));
            Assert.All(layout.SideBoxes.Where(b => b.Kind == LayoutBoxKind.Room), b => Assert.NotEqual(tag, b.Branch));
            Assert.Null(layout.Boxes[0].Branch);
            Assert.Null(layout.Boxes[^1].Branch);
            // PlaceDungeon carries each room's branch onto its sim pull; the boss stays untagged.
            var sim = new Keyrun.Sim.Encounter.DungeonDef("d", dungeon.Pulls.Select(p => new Keyrun.Sim.Encounter.PullDef(p.Id, System.Array.Empty<Keyrun.Sim.Encounter.PullSpawn>()))
                .Append(new Keyrun.Sim.Encounter.PullDef(LayoutDef.BossPullId, System.Array.Empty<Keyrun.Sim.Encounter.PullSpawn>())).ToList());
            var placed = NavLayouts.PlaceDungeon(layout, sim);
            Assert.Equal(new[] { tag, tag, OtherTag(d.Branch), OtherTag(d.Branch), null }, placed.Pulls.Select(p => p.Branch));
            _ = seed;
        }

        static string OtherTag(int b) => (1 - b).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    [Fact(DisplayName = "Seeded layout: the walked chain passes the content's LayoutValidator, the other branch is walkable beside it")]
    public void Walked_chain_is_a_valid_layout()
    {
        foreach (var (seed, d) in All())
        {
            var layout = d.Registry.Layout;
            LayoutValidator.Check(WalkedOnly(d));
            var nav = NavLayouts.ToSim(layout);
            var sideIds = layout.SideBoxes.Select(b => b.Id).ToHashSet(StringComparer.Ordinal);
            Assert.DoesNotContain(nav.Boxes, b => sideIds.Contains(b.Id));
            Assert.Equal(layout.SideBoxes.Select(b => b.Id), nav.Side.Select(b => b.Id));
            // Every side room's centre is walkable and the other branch's pulls have room triggers.
            Assert.All(layout.SideBoxes.Where(b => b.Kind == LayoutBoxKind.Room), b => Assert.True(nav.InAnyBox(new Keyrun.Sim.Core.Vec3(b.MinX + ((b.MaxX - b.MinX) / 2), 0, b.MinZ + ((b.MaxZ - b.MinZ) / 2)))));
            Assert.All(layout.SideBoxes.Where(b => b.Pull is not null), b => Assert.NotNull(nav.RoomFor(b.Pull!)));
            _ = seed;
        }
    }

    [Fact(DisplayName = "Seeded layout: the other branch as the chain (walk override) is the same boxes and also a valid chain")]
    public void Other_branch_chain_is_the_same_geometry()
    {
        var reg = ContentLoader.Load();
        foreach (var seed in Seeds.Take(30))
        {
            var bots = SeededLayout.Build(reg, seed);
            var other = SeededLayout.Build(reg, seed, walk: 1 - bots.Branch);
            Assert.Equal(1 - bots.Branch, other.Branch);
            string Key(LayoutBox b) => $"{b.Id}:{b.MinX},{b.MaxX},{b.MinZ},{b.MaxZ}";
            Assert.Equal(
                bots.Registry.Layout.Boxes.Concat(bots.Registry.Layout.SideBoxes).Select(Key).OrderBy(k => k, StringComparer.Ordinal),
                other.Registry.Layout.Boxes.Concat(other.Registry.Layout.SideBoxes).Select(Key).OrderBy(k => k, StringComparer.Ordinal));
            LayoutValidator.Check(WalkedOnly(other));
        }
    }

    [Fact(DisplayName = "Seeded layout: a broken box chain fails the validator (the check is not vacuous)")]
    public void A_broken_chain_fails_the_validator()
    {
        var d = SeededLayout.Build(ContentLoader.Load(), 1);
        var valid = WalkedOnly(d);
        LayoutValidator.Check(valid);
        var walked = valid.Layout;
        // Drop the first corridor box: the entrance no longer overlaps the next box at a doorway.
        var broken = walked with
        {
            Boxes = new ValueList<LayoutBox>(walked.Boxes.Where((_, i) => i != 1)),
            Route = new ValueList<LayoutPoint>(walked.Route.Where((_, i) => i != 1)),
        };
        Assert.Throws<ContentLoadException>(() => LayoutValidator.Check(valid with { Layout = broken }));
    }

    [Fact(DisplayName = "Seeded layout: the shell the view builds opens two doorways in the entrance's east wall and draws two one-way arrows")]
    public void The_view_shell_shows_two_exits_at_the_entrance()
    {
        foreach (var (seed, d) in All())
        {
            var layout = d.Registry.Layout;
            var entrance = layout.Boxes[0].Id;
            var plan = DungeonPlan.Build(BranchDoors.ShellBoxes(layout), BranchDoors.ShellPoints(layout));
            var exits = plan.Openings.Where(o => o.BoxId == entrance && o.Side == WallSide.MaxX).ToList();
            Assert.True(exits.Count == 2, $"seed {seed}: {exits.Count} doorways in the entrance's east wall");
            Assert.True(exits[0].Hi <= exits[1].Lo || exits[1].Hi <= exits[0].Lo, $"seed {seed}: the two exits overlap");
            // Without the side branch the shell is the old single corridor: one exit.
            var walkedOnly = DungeonPlan.Build(DungeonPlan.BoxesOf(layout), layout.Route.Select(p => (p.X, p.Z)).ToList());
            Assert.Single(walkedOnly.Openings, o => o.BoxId == entrance && o.Side == WallSide.MaxX);
            // Every side box is floored.
            Assert.All(layout.SideBoxes, b => Assert.Contains(plan.Floor, t => t.BoxId == b.Id));
            Assert.Equal(2, BranchDoors.Arrows(layout).Count);
        }
    }

    [Fact(DisplayName = "Seeded layout: the bots' branch is a seeded draw and both branches are walked across seeds")]
    public void Bot_branch_varies_by_seed()
    {
        var branches = Seeds.Select(SeededLayout.BotBranch).ToList();
        Assert.Contains(0, branches);
        Assert.Contains(1, branches);
        Assert.All(branches, b => Assert.InRange(b, 0, RoomGraphGen.BranchCount - 1));
        Assert.Equal(branches, Seeds.Select(SeededLayout.BotBranch));
    }
}
