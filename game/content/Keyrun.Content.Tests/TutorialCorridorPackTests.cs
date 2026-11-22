using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// f-9607c9c0 ("the corridors are too narrow and where some of the trash should be placed"): verifies the authored
/// tutorial (hollow-vault) layout against the #635 corridor width and pack placement rules:
/// (1) every corridor box is at least three pack footprints wide;
/// (2) at least one trash pack stands in a corridor, with its centre and every member spawn inside the corridor bounds;
/// (3) no room pack stands within one footprint of its room's centre (boss room excluded);
/// (4) any box holding two or more packs keeps them at least one aggro radius apart.
/// </summary>
public sealed class TutorialCorridorPackTests
{
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
        sides.AddRange(reg.Layout.Packs.Select(p => Extent(p.Enemies)));
        foreach (var dungeon in reg.Dungeons.Values)
        {
            sides.AddRange(dungeon.Pulls.Select(p => Extent(p.Enemies)));
        }
        return sides.Max();
    }

    [Fact(DisplayName = "Tutorial corridor packs: every authored corridor is at least three pack footprints wide")]
    public void Authored_corridors_are_three_footprints_wide()
    {
        var reg = ContentLoader.Load();
        var need = 3 * Footprint(reg);
        var corridors = reg.Layout.Boxes.Where(b => b.Kind == LayoutBoxKind.Corridor).ToList();
        Assert.NotEmpty(corridors);
        var narrow = corridors
            .Where(b => Math.Min(b.MaxX - b.MinX, b.MaxZ - b.MinZ) < need)
            .Select(b => $"{b.Id} is {Math.Min(b.MaxX - b.MinX, b.MaxZ - b.MinZ)} mm wide (want {need} mm)")
            .ToList();
        Assert.True(narrow.Count == 0, $"hollow-vault: {narrow.Count} narrow corridor(s):\n" + string.Join("\n", narrow));
    }

    [Fact(DisplayName = "Tutorial corridor packs: at least one trash pack stands wholly inside a walkable corridor")]
    public void At_least_one_authored_pack_stands_wholly_inside_a_corridor()
    {
        var reg = ContentLoader.Load();
        var layout = reg.Layout;
        var corridors = layout.Boxes.Where(b => b.Kind == LayoutBoxKind.Corridor).ToDictionary(b => b.Id, StringComparer.Ordinal);
        var inCorridor = layout.Packs.Where(p => corridors.ContainsKey(p.Room)).ToList();
        Assert.True(inCorridor.Count > 0, $"hollow-vault: none of {layout.Packs.Count} packs stands in a corridor");

        foreach (var p in inCorridor)
        {
            var box = corridors[p.Room];
            Assert.InRange(p.Center.X, box.MinX, box.MaxX);
            Assert.InRange(p.Center.Z, box.MinZ, box.MaxZ);
            foreach (var m in p.Enemies)
            {
                Assert.InRange(p.Center.X + m.Spawn.X, box.MinX, box.MaxX);
                Assert.InRange(p.Center.Z + m.Spawn.Z, box.MinZ, box.MaxZ);
            }
        }
    }

    [Fact(DisplayName = "Tutorial corridor packs: no room pack stands within one footprint of its room centre")]
    public void No_authored_room_pack_stands_at_room_centre()
    {
        var reg = ContentLoader.Load();
        var footprint = Footprint(reg);
        var rooms = reg.Layout.Boxes
            .Where(b => b.Kind == LayoutBoxKind.Room && !LayoutDef.IsBossPull(b.Pull))
            .ToDictionary(b => b.Id, StringComparer.Ordinal);

        var bad = new List<string>();
        foreach (var p in reg.Layout.Packs.Where(p => rooms.ContainsKey(p.Room)))
        {
            var r = rooms[p.Room];
            var dx = (long)p.Center.X - (r.MinX + ((r.MaxX - r.MinX) / 2));
            var dz = (long)p.Center.Z - (r.MinZ + ((r.MaxZ - r.MinZ) / 2));
            if ((dx * dx) + (dz * dz) < (long)footprint * footprint)
            {
                bad.Add($"{p.Id} in {p.Room} stands {Math.Sqrt((dx * dx) + (dz * dz)):F0} mm from the room centre (footprint {footprint} mm)");
            }
        }
        Assert.True(bad.Count == 0, $"hollow-vault: {bad.Count} pack(s) at the room centre:\n" + string.Join("\n", bad));
    }

    [Fact(DisplayName = "Tutorial corridor packs: packs sharing a box keep at least one aggro radius apart")]
    public void Authored_packs_in_same_box_are_an_aggro_radius_apart()
    {
        var reg = ContentLoader.Load();
        var bad = new List<string>();
        foreach (var group in reg.Layout.Packs.GroupBy(p => p.Room, StringComparer.Ordinal))
        {
            var list = group.ToList();
            for (var i = 0; i < list.Count; i++)
            {
                for (var j = i + 1; j < list.Count; j++)
                {
                    var dx = (long)list[i].Center.X - list[j].Center.X;
                    var dz = (long)list[i].Center.Z - list[j].Center.Z;
                    var need = Math.Max(list[i].AggroMm, list[j].AggroMm);
                    if ((dx * dx) + (dz * dz) < (long)need * need)
                    {
                        bad.Add($"{list[i].Id} and {list[j].Id} in {group.Key} stand {Math.Sqrt((dx * dx) + (dz * dz)):F0} mm apart (aggro {need} mm)");
                    }
                }
            }
        }
        Assert.True(bad.Count == 0, $"hollow-vault: {bad.Count} pair(s) inside one aggro radius:\n" + string.Join("\n", bad));
    }
}
