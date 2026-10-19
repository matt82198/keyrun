using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// ROOM-THEMING-1 (design/ART-PLAN.md lane 5; STATE.md D16-D18): game/content/data/theme.json dresses the entrance and the
/// Choir room from data, and <see cref="RoomTheme"/>'s pure placement rule keeps every themed prop out of the party's way:
/// inside its room box with its footprint clear of the walls, clear of the centre lane (<see cref="RoomTheme.LaneHalfMm"/>
/// either side of every route segment), at least <see cref="DungeonPlan.PropClearMm"/> from every spawn (the Cantor
/// included) and at least <see cref="RoomTheme.DoorClearMm"/> from every door opening, door frame and gate. The footprint
/// checks here are re-derived from the layout and the dungeon content, not from the rule's own helpers.
/// </summary>
public sealed class RoomThemeTests
{
    private const string ThemeFile = "theme.json";

    private static ContentLoadException LoadFails(MutableSource source) =>
        Assert.Throws<ContentLoadException>(() => ThemeLoader.Load(source.Build()));

    private static JsonArray PropsOf(JsonNode root, string room) =>
        root["rooms"]!.AsArray().Select(r => r!.AsObject()).First(r => (string)r["id"]! == room)["props"]!.AsArray();

    private static JsonObject PropRow(string piece, int x, int z, int turns = 0, int pct = 50) =>
        new() { ["piece"] = piece, ["x"] = x, ["z"] = z, ["turns"] = turns, ["pct"] = pct };

    private static RoomThemeDef Room(ThemeDef theme, string id) => theme.Rooms.Single(r => r.Id == id);

    // Every spawn the run can place: each pull room's origin, its enemies at origin + content offset, its --pull start,
    // and the party's start.
    private static List<(int X, int Z)> Spawns(ContentRegistry reg)
    {
        var layout = reg.Layout;
        var dungeon = reg.Dungeons[layout.Dungeon];
        var spawns = new List<(int X, int Z)> { (layout.Start.X, layout.Start.Z) };
        foreach (var room in layout.Boxes.Where(b => b.Pull is not null))
        {
            spawns.Add((room.Origin!.X, room.Origin.Z));
            spawns.Add((room.Start!.X, room.Start.Z));
            var pull = dungeon.Pulls.FirstOrDefault(p => p.Id == room.Pull);
            if (pull is not null)
            {
                spawns.AddRange(pull.Enemies.Select(e => (room.Origin.X + e.Spawn.X, room.Origin.Z + e.Spawn.Z)));
            }
        }
        return spawns;
    }

    private static double RectPoint(WallBox r, int x, int z)
    {
        var dx = Math.Max(0, Math.Max(r.MinX - x, x - r.MaxX));
        var dz = Math.Max(0, Math.Max(r.MinZ - z, z - r.MaxZ));
        return Math.Sqrt((double)dx * dx + (double)dz * dz);
    }

    [Fact]
    public void Theme_json_parses_to_rows_for_the_entrance_the_choir_room_the_pair_hall_and_the_seed_chamber()
    {
        var theme = ThemeLoader.Load();
        Assert.Equal(new[] { "entrance", "choir-room", "pair-hall", "seed-chamber" }, theme.Rooms.Select(r => r.Id).ToArray());
        Assert.Contains(ContentLoader.ThemeFile, ContentLoader.Files);
        var choir = Room(theme, "choir-room");
        Assert.Equal(6, choir.Props.Count(p => p.Piece == "table_long"));
        Assert.Equal(2, choir.Props.Count(p => p.Piece == "pillar_decorated"));
        Assert.Equal(2, choir.Props.Count(p => p.Piece == "table_small_decorated_A"));
        Assert.Contains(choir.Decor!, d => d.Piece == "banner_shield_red" && d.Side == WallSide.MaxX);
        var entrance = Room(theme, "entrance");
        Assert.DoesNotContain(entrance.Props, p => p.Piece == "chest_gold"); // saved for the Seed chamber
        Assert.Single(entrance.Decor!, d => d.Piece == "banner_patternA_blue" && d.Side == WallSide.MinX);
        Assert.Equal(2, entrance.Decor!.Count(d => d.Piece == "torch_mounted" && d.Side == WallSide.MaxX));
        // ROOM-THEMING-2: the Pair hall is a guarded gallery, the Seed chamber a vault.
        var pair = Room(theme, "pair-hall");
        Assert.True(pair.Props.Count(p => p.Piece == "pillar") >= 8, "the Pair hall gallery needs two rows of pillars");
        Assert.Contains(pair.Props, p => p.Piece == "table_medium_broken");
        Assert.Contains(pair.Props, p => p.Piece == "crates_stacked");
        Assert.Contains(pair.Props, p => p.Piece == "barrel_small_stack");
        Assert.Contains(pair.Decor!, d => d.Piece == "banner_shield_red" && d.Side == WallSide.MaxX);
        Assert.True(pair.Decor!.Count(d => d.Piece == "torch_mounted" && d.Side == WallSide.MinZ) >= 2);
        Assert.True(pair.Decor!.Count(d => d.Piece == "torch_mounted" && d.Side == WallSide.MaxZ) >= 2);
        var seed = Room(theme, "seed-chamber");
        Assert.Contains(seed.Props, p => p.Piece == "chest_gold");
        Assert.Equal(2, seed.Props.Count(p => p.Piece == "pillar_decorated"));
        Assert.True(seed.Props.Count(p => p.Piece == "candle_triple") >= 2);
        Assert.Contains(seed.Props, p => p.Piece == "barrel_large");
        Assert.Contains(seed.Props, p => p.Piece == "crates_stacked");
        Assert.Contains(seed.Decor!, d => d.Piece == "banner_patternC_red" && d.Side == WallSide.MaxX);
        Assert.DoesNotContain(theme.Rooms.Where(r => r.Id != "seed-chamber").SelectMany(r => r.Props), p => p.Piece == "chest_gold");
    }

    [Fact]
    public void Every_themed_prop_and_decor_piece_names_a_kit_stem_in_nominal()
    {
        var theme = ThemeLoader.Load();
        foreach (var room in theme.Rooms)
        {
            Assert.All(room.Props, p => Assert.True(DungeonPlan.Nominal.ContainsKey(p.Piece), room.Id + ": unknown stem " + p.Piece));
            Assert.All(room.Decor!, d => Assert.True(DungeonPlan.Nominal.ContainsKey(d.Piece), room.Id + ": unknown stem " + d.Piece));
        }
    }

    [Fact]
    public void Every_themed_prop_clears_the_lane_the_spawns_the_doors_and_the_walls()
    {
        var reg = ContentLoader.Load();
        var layout = reg.Layout;
        var theme = ThemeLoader.Load();
        var boxes = DungeonPlan.BoxesOf(layout);
        var shell = DungeonPlan.Build(layout);
        var doors = DungeonPlan.FrameBoxes(boxes).Concat(shell.Gates.Select(g => DungeonPlan.SolidOf(g.Piece))).ToList();
        doors.AddRange(shell.Openings.Select(o => o.Side is WallSide.MinX or WallSide.MaxX
            ? new WallBox(o.BoxId, o.Line, o.Line, o.Lo, o.Hi, 0, 0)
            : new WallBox(o.BoxId, o.Lo, o.Hi, o.Line, o.Line, 0, 0)));
        var spawns = Spawns(reg);
        var checkedProps = 0;
        Assert.Empty(RoomTheme.Violations(theme, ThemeSite.Of(reg)));
        foreach (var room in theme.Rooms)
        {
            var box = layout.Boxes.Single(b => b.Id == room.Id);
            foreach (var p in room.Props)
            {
                var f = DungeonPlan.SolidOf(DungeonPlan.PropPiece(room.Id, p.Piece, p.X, p.Z, p.Turns, p.Pct));
                var at = $"{room.Id} {p.Piece} ({p.X},{p.Z})";
                Assert.True(f.MinX >= box.MinX && f.MaxX <= box.MaxX && f.MinZ >= box.MinZ && f.MaxZ <= box.MaxZ, at + " crosses a wall");
                // The route runs along z = 0 on the real layout, so the centre lane is |z| < 1500 wherever x is on it.
                Assert.True(f.MinZ >= RoomTheme.LaneHalfMm || f.MaxZ <= -RoomTheme.LaneHalfMm, at + " is in the centre lane");
                foreach (var s in spawns)
                {
                    Assert.True(RectPoint(f, s.X, s.Z) >= DungeonPlan.PropClearMm, at + $" is within 2000 mm of spawn ({s.X},{s.Z})");
                }
                foreach (var d in doors)
                {
                    var dx = Math.Max(0, Math.Max(d.MinX - f.MaxX, f.MinX - d.MaxX));
                    var dz = Math.Max(0, Math.Max(d.MinZ - f.MaxZ, f.MinZ - d.MaxZ));
                    Assert.True(Math.Sqrt((double)dx * dx + (double)dz * dz) >= RoomTheme.DoorClearMm, at + " is within 1500 mm of a door or gate");
                }
                checkedProps++;
            }
        }
        Assert.True(checkedProps >= 16, "vacuity guard: only " + checkedProps + " themed props checked");
    }

    [Fact]
    public void The_cantor_keeps_a_clear_two_metre_ring()
    {
        var theme = ThemeLoader.Load();
        foreach (var p in Room(theme, "choir-room").Props)
        {
            var f = DungeonPlan.SolidOf(DungeonPlan.PropPiece("choir-room", p.Piece, p.X, p.Z, p.Turns, p.Pct));
            Assert.True(RectPoint(f, 21500, -1000) >= 2000, $"{p.Piece} ({p.X},{p.Z}) stands in the Cantor's ring");
        }
    }

    [Fact]
    public void A_prop_in_the_centre_lane_is_rejected()
    {
        var ex = LoadFails(new MutableSource().Edit(ThemeFile, n => PropsOf(n, "choir-room").Add(PropRow("barrel_large", 16000, 600))));
        Assert.Contains("centre lane", ex.Message);
    }

    [Fact]
    public void A_prop_1000_mm_from_the_cantor_spawn_is_rejected()
    {
        // (21500, -2450) is off the lane (barrel footprint z -2900..-2000) and its footprint is 1000 mm from the Cantor at (21500, -1000).
        var ex = LoadFails(new MutableSource().Edit(ThemeFile, n => PropsOf(n, "choir-room").Add(PropRow("barrel_large", 21500, -2450))));
        Assert.Contains("spawn (21500,-1000)", ex.Message);
    }

    [Fact]
    public void A_prop_in_the_pair_hall_centre_lane_is_rejected()
    {
        var ex = LoadFails(new MutableSource().Edit(ThemeFile, n => PropsOf(n, "pair-hall").Add(PropRow("barrel_large", 46000, 600))));
        Assert.Contains("centre lane", ex.Message);
    }

    [Fact]
    public void A_prop_1900_mm_from_the_pair_melee_spawn_is_rejected()
    {
        // pair.melee spawns at the Pair hall origin (38500, 0) + (5000, 0); the barrel's footprint z -2800..-1900 is off the
        // lane but 1900 mm from the spawn.
        var ex = LoadFails(new MutableSource().Edit(ThemeFile, n => PropsOf(n, "pair-hall").Add(PropRow("barrel_large", 43500, -2350))));
        Assert.Contains("spawn (43500,0)", ex.Message);
    }

    [Fact]
    public void A_prop_1900_mm_from_the_seed_elite_spawn_is_rejected()
    {
        // seed.elite spawns at the Seed chamber origin (63500, 0) + (10000, 0).
        var ex = LoadFails(new MutableSource().Edit(ThemeFile, n => PropsOf(n, "seed-chamber").Add(PropRow("barrel_large", 73000, 2350))));
        Assert.Contains("spawn (73500,0)", ex.Message);
    }

    [Fact]
    public void A_prop_beside_the_corridor_3_gate_is_rejected()
    {
        // The corridor-3 gate stands at x 55500 inside the Pair hall's far doorway.
        var ex = LoadFails(new MutableSource().Edit(ThemeFile, n => PropsOf(n, "pair-hall").Add(PropRow("barrel_large", 54000, -2600))));
        Assert.Contains("door", ex.Message);
    }

    [Fact]
    public void A_prop_beside_the_corridor_4_gate_is_rejected()
    {
        // The corridor-4 gate stands at x 75500 inside the Seed chamber's far doorway.
        var ex = LoadFails(new MutableSource().Edit(ThemeFile, n => PropsOf(n, "seed-chamber").Add(PropRow("barrel_large", 74000, -2900))));
        Assert.Contains("door", ex.Message);
    }

    [Fact]
    public void The_seed_chamber_chest_gold_sits_outside_the_lane_and_the_elite_ring_and_is_accepted()
    {
        var reg = ContentLoader.Load();
        var theme = ThemeLoader.Load();
        var site = ThemeSite.Of(reg);
        var box = DungeonPlan.BoxesOf(reg.Layout).Single(b => b.Id == "seed-chamber");
        var chests = Room(theme, "seed-chamber").Props.Where(p => p.Piece == "chest_gold").ToList();
        Assert.InRange(chests.Count, 1, 2);
        foreach (var c in chests)
        {
            var f = DungeonPlan.SolidOf(DungeonPlan.PropPiece("seed-chamber", c.Piece, c.X, c.Z, c.Turns, c.Pct));
            Assert.True(f.MinZ >= RoomTheme.LaneHalfMm || f.MaxZ <= -RoomTheme.LaneHalfMm, $"chest ({c.X},{c.Z}) is in the centre lane");
            Assert.True(RectPoint(f, 73500, 0) >= 2000, $"chest ({c.X},{c.Z}) stands in the seed elite's ring");
            Assert.True(c.X >= box.MaxX - 4000, $"chest ({c.X},{c.Z}) is not at the far end of the vault");
            Assert.Empty(RoomTheme.PropViolations(box, c, site));
        }
    }

    [Fact]
    public void An_unknown_stem_is_rejected()
    {
        var ex = LoadFails(new MutableSource().Edit(ThemeFile, n => PropsOf(n, "entrance").Add(PropRow("dais_large", -3000, -4000))));
        Assert.Contains("unknown kit piece \"dais_large\"", ex.Message);
    }

    [Fact]
    public void A_prop_crossing_a_wall_is_rejected()
    {
        var ex = LoadFails(new MutableSource().Edit(ThemeFile, n => PropsOf(n, "entrance").Add(PropRow("crates_stacked", -3000, 5800))));
        Assert.Contains("crosses its room's wall", ex.Message);
    }

    [Fact]
    public void A_prop_beside_the_corridor_2_gate_is_rejected()
    {
        var ex = LoadFails(new MutableSource().Edit(ThemeFile, n => PropsOf(n, "choir-room").Add(PropRow("barrel_large", 24800, -2600))));
        Assert.Contains("door", ex.Message);
    }

    [Fact]
    public void A_theme_row_for_a_corridor_or_an_unknown_room_is_rejected()
    {
        var ex = LoadFails(new MutableSource().Edit(ThemeFile, n => n["rooms"]!.AsArray().Add(
            new JsonObject { ["id"] = "corridor-1", ["props"] = new JsonArray(), ["lights"] = new JsonArray() })));
        Assert.Contains("not a layout room", ex.Message);
    }

    [Fact]
    public void Light_count_per_room_is_within_budget()
    {
        var theme = ThemeLoader.Load();
        Assert.Equal(2, Room(theme, "entrance").Lights.Count);
        Assert.Equal(3, Room(theme, "choir-room").Lights.Count); // the two torches plus the chancel candle light
        Assert.Equal(2, Room(theme, "pair-hall").Lights.Count); // the hall keeps its two torch lights
        Assert.Equal(3, Room(theme, "seed-chamber").Lights.Count); // two torch lights plus the warm light over the chest
        Assert.All(theme.Rooms, r => Assert.InRange(r.Lights.Count, 1, RoomTheme.MaxLightsPerRoom));
        var plan = DungeonPlan.Build(ContentLoader.Load().Layout, theme);
        Assert.InRange(plan.Lights.Count, 13, 15); // 13 before ROOM-THEMING-2, at most 2 new, 16 cap
    }

    [Fact]
    public void Prop_counts_are_within_the_room_budgets()
    {
        var theme = ThemeLoader.Load();
        int Count(string id) => Room(theme, id).Props.Count + Room(theme, id).Decor!.Count(d => d.Piece != "torch_mounted");
        Assert.InRange(Count("entrance"), 6, 12);
        Assert.InRange(Count("choir-room"), 10, 20);
        Assert.InRange(Count("pair-hall"), 10, 20);
        Assert.InRange(Count("seed-chamber"), 8, 16);
    }

    [Fact]
    public void Themed_rooms_are_dressed_from_the_data_and_the_rest_keep_their_code_dressing()
    {
        var layout = ContentLoader.Load().Layout;
        var theme = ThemeLoader.Load();
        var themed = DungeonPlan.Build(layout, theme);
        var bare = DungeonPlan.Build(layout);
        foreach (var room in theme.Rooms)
        {
            Assert.Equal(room.Props.Count, themed.Props.Count(p => p.BoxId == room.Id));
            Assert.Equal(room.Decor!.Count, themed.WallDecor.Count(p => p.BoxId == room.Id));
            Assert.Equal(room.Lights.Count, themed.Lights.Count(l => l.BoxId == room.Id));
            Assert.DoesNotContain(bare.Props, p => p.BoxId == room.Id); // no hard-coded props left in C# for themed rooms
        }
        foreach (var id in new[] { "warden-arena" })
        {
            Assert.Equal(bare.Props.Where(p => p.BoxId == id), themed.Props.Where(p => p.BoxId == id));
            Assert.Equal(bare.WallDecor.Where(p => p.BoxId == id), themed.WallDecor.Where(p => p.BoxId == id));
            Assert.Equal(bare.Lights.Where(l => l.BoxId == id), themed.Lights.Where(l => l.BoxId == id));
            Assert.Contains(themed.Props, p => p.BoxId == id);
        }
        var light = Assert.Single(themed.Lights, l => l.BoxId == "choir-room" && l.Color is not null);
        Assert.Equal("FFB060", light.Color);
        var vault = Assert.Single(themed.Lights, l => l.BoxId == "seed-chamber" && l.Color is not null);
        Assert.Equal("FFB060", vault.Color);
        Assert.True(vault.X >= 72000, "the vault light hangs over the chest at the far end");
    }

    [Fact]
    public void A_cracked_wall_entry_turns_that_wall_piece_cracked()
    {
        var layout = ContentLoader.Load().Layout;
        var themed = DungeonPlan.Build(layout, ThemeLoader.Load());
        var bare = DungeonPlan.Build(layout);
        Assert.DoesNotContain(bare.WallPieces, w => w.Run.BoxId == "entrance" && w.Piece.Piece == "wall_cracked");
        var cracked = Assert.Single(themed.WallPieces, w => w.Run.BoxId == "entrance" && w.Piece.Piece == "wall_cracked");
        Assert.Equal(WallSide.MaxZ, cracked.Run.Side);
        // Same geometry: the camera's occluder boxes do not move.
        Assert.Equal(bare.WallPieces.Select(w => DungeonPlan.SolidOf(w.Piece)), themed.WallPieces.Select(w => DungeonPlan.SolidOf(w.Piece)));
    }
}
