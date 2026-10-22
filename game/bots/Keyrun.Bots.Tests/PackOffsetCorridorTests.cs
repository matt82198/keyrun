using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Xunit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// f-1232d0aa and f-9607c9c0 (Matt 2026-10-22: "the mobs are all center of the room, they should be offset so the player
/// can decide what to pull" and "the corridors are too narrow and where some of the trash should be placed"). Over both
/// seeded dungeons (hollow-vault via <see cref="SeededLayout.Build"/>, sunken-bellfoundry via <see cref="SeededLayout.ArmSegmented"/>)
/// at seeds 1 to 3 (the layout does not depend on the key; the run proofs are key 2), the built layout must hold four properties:
/// (1) no pack stands within one pack footprint of its room's centre; (2) a room holding two or more packs keeps them at least
/// one aggro radius apart; (3) every corridor box is at least three footprints wide; (4) at least one trash pack stands in a
/// corridor. Definitions, stated and not tuned: a pack's footprint is the larger side of the bounding box of its member
/// offsets, the footprint used is the largest over every pack and pull the content ships (an enemy cluster has to fit three
/// times over in a corridor: three seats and a pack); a pack's position is its centre, a teaching pull's the mean of its
/// spawns; the boss room is excluded.
/// </summary>
public sealed class PackOffsetCorridorTests
{
    private static readonly string[] Dungeons = { "hollow-vault", "sunken-bellfoundry" };

    private static readonly long[] Seeds = { 1, 2, 3 };

    /// <summary>Both dungeons crossed with seeds 1 to 3.</summary>
    public static IEnumerable<object[]> Cases() =>
        Dungeons.SelectMany(d => Seeds.Select(s => new object[] { d, s }));

    private sealed record Placed(string Id, string Room, int X, int Z, int AggroMm);

    private static int Extent(IEnumerable<PullEnemy> enemies)
    {
        var list = enemies.ToList();
        var dx = list.Max(e => e.Spawn.X) - list.Min(e => e.Spawn.X);
        var dz = list.Max(e => e.Spawn.Z) - list.Min(e => e.Spawn.Z);
        return Math.Max(dx, dz);
    }

    private static int Footprint(ContentRegistry reg)
    {
        var sides = new List<int>();
        sides.AddRange(reg.Roster.Values.Select(r => Extent(r.Enemies)));
        sides.AddRange(reg.Layout.Packs.Select(p => Extent(p.Enemies)));
        foreach (var dungeon in reg.Dungeons.Values)
        {
            sides.AddRange(dungeon.Pulls.Select(p => Extent(p.Enemies)));
        }
        return sides.Max();
    }

    private static LayoutDef Build(ContentRegistry reg, string dungeon, long seed, out ContentRegistry armed)
    {
        armed = dungeon == "hollow-vault" ? SeededLayout.Build(reg, seed).Registry : SeededLayout.ArmSegmented(reg, dungeon, seed);
        return armed.Layout;
    }

    // Every trash pack of the layout (walked and side) plus every walked teaching pull as a pack at its spawn mean.
    private static List<Placed> Packs(ContentRegistry armed)
    {
        var layout = armed.Layout;
        var placed = layout.Packs.Select(p => new Placed(p.Id, p.Room, p.Center.X, p.Center.Z, p.AggroMm)).ToList();
        var dungeon = armed.Dungeons[layout.Dungeon];
        foreach (var room in layout.Boxes.Where(b => b.Kind == LayoutBoxKind.Room && b.Pull is not null && !LayoutDef.IsBossPull(b.Pull)))
        {
            var pull = dungeon.Pulls.First(p => p.Id == room.Pull);
            var n = pull.Enemies.Sum(e => e.Count);
            var x = room.Origin!.X + (pull.Enemies.Sum(e => e.Count * e.Spawn.X) / n);
            var z = room.Origin!.Z + (pull.Enemies.Sum(e => e.Count * e.Spawn.Z) / n);
            placed.Add(new Placed(pull.Id, room.Id, x, z, room.AggroMm ?? 4000));
        }
        return placed;
    }

    private static IEnumerable<LayoutBox> AllBoxes(LayoutDef layout) => layout.Boxes.Concat(layout.SideBoxes);

    [Theory(DisplayName = "Pack offset: no pack stands within one pack footprint of its room's centre")]
    [MemberData(nameof(Cases))]
    public void No_pack_stands_at_the_room_centre(string dungeon, long seed)
    {
        var reg = ContentLoader.Load();
        var layout = Build(reg, dungeon, seed, out var armed);
        var footprint = Footprint(reg);
        var rooms = AllBoxes(layout).Where(b => b.Kind == LayoutBoxKind.Room).ToDictionary(b => b.Id, StringComparer.Ordinal);
        var bad = new List<string>();
        foreach (var p in Packs(armed).Where(p => rooms.ContainsKey(p.Room) && !LayoutDef.IsBossPull(rooms[p.Room].Pull)))
        {
            var r = rooms[p.Room];
            var dx = (long)p.X - (r.MinX + ((r.MaxX - r.MinX) / 2));
            var dz = (long)p.Z - (r.MinZ + ((r.MaxZ - r.MinZ) / 2));
            if ((dx * dx) + (dz * dz) < (long)footprint * footprint)
            {
                bad.Add($"{p.Id} in {p.Room} stands {Math.Sqrt((dx * dx) + (dz * dz)):F0} mm from the room centre (footprint {footprint} mm)");
            }
        }
        Assert.True(bad.Count == 0, $"{dungeon} seed {seed}: {bad.Count} pack(s) at the room centre:\n" + string.Join("\n", bad));
    }

    [Theory(DisplayName = "Pack offset: a room holding two or more packs keeps them at least one aggro radius apart")]
    [MemberData(nameof(Cases))]
    public void Packs_in_one_room_are_an_aggro_radius_apart(string dungeon, long seed)
    {
        var reg = ContentLoader.Load();
        Build(reg, dungeon, seed, out var armed);
        var bad = new List<string>();
        foreach (var group in Packs(armed).GroupBy(p => p.Room, StringComparer.Ordinal))
        {
            var list = group.ToList();
            for (var i = 0; i < list.Count; i++)
            {
                for (var j = i + 1; j < list.Count; j++)
                {
                    var dx = (long)list[i].X - list[j].X;
                    var dz = (long)list[i].Z - list[j].Z;
                    var need = Math.Max(list[i].AggroMm, list[j].AggroMm);
                    if ((dx * dx) + (dz * dz) < (long)need * need)
                    {
                        bad.Add($"{list[i].Id} and {list[j].Id} in {group.Key} stand {Math.Sqrt((dx * dx) + (dz * dz)):F0} mm apart (aggro {need} mm)");
                    }
                }
            }
        }
        Assert.True(bad.Count == 0, $"{dungeon} seed {seed}: {bad.Count} pair(s) inside one aggro radius:\n" + string.Join("\n", bad));
    }

    [Theory(DisplayName = "Pack offset: every corridor on the route and beside it is at least three footprints wide")]
    [MemberData(nameof(Cases))]
    public void Corridors_are_three_footprints_wide(string dungeon, long seed)
    {
        var reg = ContentLoader.Load();
        var layout = Build(reg, dungeon, seed, out _);
        var need = 3 * Footprint(reg);
        var corridors = AllBoxes(layout).Where(b => b.Kind == LayoutBoxKind.Corridor).ToList();
        Assert.NotEmpty(corridors);
        var narrow = corridors
            .Where(b => Math.Min(b.MaxX - b.MinX, b.MaxZ - b.MinZ) < need)
            .Select(b => $"{b.Id} is {Math.Min(b.MaxX - b.MinX, b.MaxZ - b.MinZ)} mm wide (want {need} mm)")
            .ToList();
        Assert.True(narrow.Count == 0, $"{dungeon} seed {seed}: {narrow.Count} narrow corridor(s):\n" + string.Join("\n", narrow));
    }

    [Theory(DisplayName = "Pack offset: at least one trash pack spawns inside a corridor")]
    [MemberData(nameof(Cases))]
    public void A_trash_pack_stands_in_a_corridor(string dungeon, long seed)
    {
        var reg = ContentLoader.Load();
        var layout = Build(reg, dungeon, seed, out _);
        var corridorIds = AllBoxes(layout).Where(b => b.Kind == LayoutBoxKind.Corridor).Select(b => b.Id).ToHashSet(StringComparer.Ordinal);
        var inCorridor = layout.Packs.Where(p => corridorIds.Contains(p.Room)).ToList();
        Assert.True(inCorridor.Count > 0, $"{dungeon} seed {seed}: none of {layout.Packs.Count} packs stands in a corridor");
        // The pack stands wholly inside its corridor box, and the walked route has one (a pack the party walks past).
        foreach (var p in inCorridor)
        {
            var box = AllBoxes(layout).First(b => b.Id == p.Room);
            foreach (var m in p.Enemies)
            {
                Assert.InRange(p.Center.X + m.Spawn.X, box.MinX, box.MaxX);
                Assert.InRange(p.Center.Z + m.Spawn.Z, box.MinZ, box.MaxZ);
            }
        }
        var routeCorridors = layout.Boxes.Where(b => b.Kind == LayoutBoxKind.Corridor).Select(b => b.Id).ToHashSet(StringComparer.Ordinal);
        Assert.Contains(inCorridor, p => routeCorridors.Contains(p.Room));
    }
}
