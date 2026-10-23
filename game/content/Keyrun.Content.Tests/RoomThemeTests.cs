using System;
using System.Collections.Generic;
using System.IO;
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

    private static JsonArray LightsOf(JsonNode root, string room) =>
        root["rooms"]!.AsArray().Select(r => r!.AsObject()).First(r => (string)r["id"]! == room)["lights"]!.AsArray();

    // ROOM-THEMING-3: the Hollow Warden pulls at the arena origin (93500, 0) and game/src/Host/HostRunSetup.cs flanks it
    // with the two Sundered Twins at +/- TwinOffsetMm 4000 along x, so (97500, 0) and (89500, 0).
    private static readonly (int X, int Z)[] Twins = { (97500, 0), (89500, 0) };

    // The arena's C# dressing before ROOM-THEMING-3 (DungeonPlan.Dress, removed): 6 torch_lit braziers, 4 pillars and the
    // chair; 4 banner_shield_red; 4 LightKind.Warden lamps.
    private const int CodedArenaProps = 11;
    private const int CodedArenaBanners = 4;
    private const int CodedArenaLights = 4;

    private static PlanBox Arena(ContentRegistry reg) => DungeonPlan.BoxesOf(reg.Layout).Single(b => b.Id == "warden-arena");

    private static ThemeProp Barrel(int x, int z) => new() { Piece = "barrel_large", X = x, Z = z, Turns = 0, Pct = 50 };

    // Every spawn the run can place: each pull room's origin, its enemies at origin + content offset, its --pull start,
    // the boss room's two Twins, and the party's start.
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
            if (room.Pull == LayoutDef.BossPullId)
            {
                spawns.AddRange(Twins);
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
    public void Theme_json_parses_to_rows_for_all_five_rooms_from_the_entrance_to_the_warden_arena()
    {
        var theme = ThemeLoader.Load();
        Assert.Equal(new[] { "entrance", "choir-room", "pair-hall", "seed-chamber", "warden-arena" }, theme.Rooms.Select(r => r.Id).ToArray());
        // ROOM-THEMING-3: the arena is a throne room.
        var arena = Room(theme, "warden-arena");
        Assert.Equal(4, arena.Props.Count(p => p.Piece == "pillar_decorated"));
        Assert.True(arena.Props.Count(p => p.Piece == "candle_triple") >= 4, "a candle at each pillar foot");
        Assert.Single(arena.Props, p => p.Piece == "chair" && p.X == 108200 && p.Z == 0); // the empty throne
        Assert.Equal(4, arena.Decor!.Count(d => d.Piece == "banner_shield_red" && d.Side == WallSide.MaxX));
        Assert.Contains(arena.Decor!, d => d.Piece == "banner_thin_red" && d.Side == WallSide.MaxX);
        Assert.True(arena.Decor!.Count(d => d.Piece == "torch_mounted" && d.Side == WallSide.MinZ) >= 2);
        Assert.True(arena.Decor!.Count(d => d.Piece == "torch_mounted" && d.Side == WallSide.MaxZ) >= 2);
        Assert.NotEmpty(arena.Cracked!);
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
        Assert.Contains(pair.Decor!, d => d.Piece == "banner_shield_red" && d.Side == WallSide.MaxZ);
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
        Assert.All(layout.Route, p => Assert.Equal(0, p.Z));
        var laneEndX = Math.Max(layout.Start.X, layout.Route.Max(p => p.X)) + RoomTheme.LaneHalfMm;
        Assert.Equal(97500, laneEndX);
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
                // The route runs along z = 0 on the real layout, so the centre lane is |z| < 1500 wherever x is on it; it ends
                // 1500 mm past the last route point (96000, 0) in the arena, so the throne beyond it is clear.
                Assert.True(f.MinZ >= RoomTheme.LaneHalfMm || f.MaxZ <= -RoomTheme.LaneHalfMm || f.MinX >= laneEndX, at + " is in the centre lane");
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
        Assert.Equal(3, Room(theme, "warden-arena").Lights.Count); // the cap: four coded Warden lamps became three
        Assert.All(theme.Rooms, r => Assert.InRange(r.Lights.Count, 1, RoomTheme.MaxLightsPerRoom));
        var plan = DungeonPlan.Build(ContentLoader.Load().Layout, theme);
        Assert.Equal(13, plan.Lights.Count); // 14 before ROOM-THEMING-3, the arena one fewer
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
        Assert.InRange(Room(theme, "warden-arena").Props.Count, 10, 18);
        Assert.InRange(Count("warden-arena"), 14, 26);
    }

    [Fact]
    public void Every_room_is_dressed_from_the_data_and_no_room_keeps_code_dressing()
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
        }
        // ROOM-THEMING-3: every layout room has a theme row and C# places no prop in any room.
        Assert.Equal(layout.Boxes.Where(b => b.Kind == LayoutBoxKind.Room).Select(b => b.Id).OrderBy(s => s, StringComparer.Ordinal),
            theme.Rooms.Select(r => r.Id).OrderBy(s => s, StringComparer.Ordinal));
        Assert.Empty(bare.Props);
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

    // ---- ROOM-THEMING-3: the Warden arena as a throne room (design/ART-PLAN.md lane 7) ------------------------------

    [Fact]
    public void The_theme_site_counts_the_boss_and_both_sundered_twins_as_spawns()
    {
        var site = ThemeSite.Of(ContentLoader.Load());
        Assert.Contains((93500, 0), site.Spawns);
        foreach (var t in Twins)
        {
            Assert.Contains(t, site.Spawns);
        }
    }

    [Fact]
    public void The_twin_sites_use_the_host_run_setup_offset()
    {
        // The content layer cannot reference the Godot host, so ThemeSite mirrors HostRunSetup.TwinOffsetMm; this pins the mirror.
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "src", "HostCore", "HostRunSetup.cs")))
        {
            d = d.Parent;
        }
        Assert.NotNull(d);
        var source = File.ReadAllText(Path.Combine(d!.FullName, "src", "HostCore", "HostRunSetup.cs"));
        Assert.Contains($"public const int TwinOffsetMm = {ThemeSite.TwinOffsetMm};", source, StringComparison.Ordinal);
        Assert.Equal(new[] { 93500 + ThemeSite.TwinOffsetMm, 93500 - ThemeSite.TwinOffsetMm }, Twins.Select(t => t.X).ToArray());
    }

    [Fact]
    public void A_prop_1900_mm_from_either_twin_is_rejected()
    {
        // Each barrel footprint (900 mm square) is off the lane and 1900 mm from one Twin, at least 3100 mm from the boss.
        var reg = ContentLoader.Load();
        var site = ThemeSite.Of(reg);
        Assert.Contains(RoomTheme.PropViolations(Arena(reg), Barrel(97500, -2350), site), e => e.Contains("spawn (97500,0)", StringComparison.Ordinal));
        Assert.Contains(RoomTheme.PropViolations(Arena(reg), Barrel(89500, 2350), site), e => e.Contains("spawn (89500,0)", StringComparison.Ordinal));
    }

    [Fact]
    public void A_prop_beside_a_twin_in_the_arena_row_fails_the_load()
    {
        var ex = LoadFails(new MutableSource().Edit(ThemeFile, n => PropsOf(n, "warden-arena").Add(PropRow("barrel_large", 97500, -2350))));
        Assert.Contains("spawn (97500,0)", ex.Message);
    }

    [Fact]
    public void A_prop_in_the_arena_centre_lane_is_rejected()
    {
        var ex = LoadFails(new MutableSource().Edit(ThemeFile, n => PropsOf(n, "warden-arena").Add(PropRow("barrel_large", 91500, -600))));
        Assert.Contains("centre lane", ex.Message);
    }

    [Fact]
    public void A_prop_beside_the_corridor_4_door_into_the_arena_is_rejected()
    {
        // Corridor 4 (gate "seed") opens the arena's minX wall at x 82000, z -1500..1500; the barrel's footprint
        // x 82550..83450 z -3050..-2150 is about 850 mm from the opening and its frame pillar.
        var ex = LoadFails(new MutableSource().Edit(ThemeFile, n => PropsOf(n, "warden-arena").Add(PropRow("barrel_large", 83000, -2600))));
        Assert.Contains("door", ex.Message);
    }

    [Fact]
    public void The_arena_pillar_ring_stands_clear_of_the_boss_the_twins_and_the_lane_and_is_accepted()
    {
        var reg = ContentLoader.Load();
        var site = ThemeSite.Of(reg);
        var ring = Room(ThemeLoader.Load(), "warden-arena").Props.Where(p => p.Piece == "pillar_decorated").ToList();
        Assert.Equal(4, ring.Count);
        Assert.Equal(2, ring.Count(p => p.X > 93500));
        Assert.Equal(2, ring.Count(p => p.Z > 0));
        foreach (var p in ring)
        {
            var at = $"pillar ({p.X},{p.Z})";
            var f = DungeonPlan.SolidOf(DungeonPlan.PropPiece("warden-arena", p.Piece, p.X, p.Z, p.Turns, p.Pct));
            Assert.Empty(RoomTheme.PropViolations(Arena(reg), p, site));
            Assert.InRange(Math.Sqrt(Math.Pow(p.X - 93500, 2) + Math.Pow(p.Z, 2)), 5000, 7000); // a ring about the boss ground
            Assert.True(RectPoint(f, 93500, 0) >= 3000, at + " stands inside the boss's 3000 mm slam range");
            foreach (var t in Twins)
            {
                Assert.True(Math.Sqrt(Math.Pow(p.X - t.X, 2) + Math.Pow(p.Z - t.Z, 2)) >= 4250, at + $" is under 4250 mm from the Twin at ({t.X},{t.Z})");
                Assert.True(RectPoint(f, t.X, t.Z) >= DungeonPlan.PropClearMm, at + $" crowds the Twin at ({t.X},{t.Z})");
            }
            Assert.True(f.MinZ >= RoomTheme.LaneHalfMm || f.MaxZ <= -RoomTheme.LaneHalfMm, at + " is in the centre lane");
        }
    }

    [Fact]
    public void The_arena_carries_three_lights_and_a_fourth_is_rejected()
    {
        Assert.Equal(3, Room(ThemeLoader.Load(), "warden-arena").Lights.Count);
        var ex = LoadFails(new MutableSource().Edit(ThemeFile, n => LightsOf(n, "warden-arena").Add(
            new JsonObject { ["x"] = 96000, ["z"] = 0, ["yMm"] = 6000, ["rangeMm"] = 16000, ["kind"] = "warden" })));
        Assert.Contains("warden-arena has 4 lights (1..3)", ex.Message);
    }

    [Fact]
    public void The_arena_lights_keep_the_warden_kind_and_every_other_light_is_torchlight()
    {
        var plan = DungeonPlan.Build(ContentLoader.Load().Layout, ThemeLoader.Load());
        var arena = plan.Lights.Where(l => l.BoxId == "warden-arena").ToList();
        Assert.Equal(3, arena.Count);
        Assert.All(arena, l => Assert.Equal(LightKind.Warden, l.Kind));
        Assert.All(arena, l => Assert.Null(l.Color)); // the view's Warden colour and energy, not a torch tint
        Assert.All(plan.Lights.Where(l => l.BoxId != "warden-arena"), l => Assert.Equal(LightKind.Torch, l.Kind));
    }

    [Fact]
    public void An_unknown_light_kind_is_rejected()
    {
        var ex = LoadFails(new MutableSource().Edit(ThemeFile, n => LightsOf(n, "entrance")[0]!["kind"] = "lantern"));
        Assert.Contains("lantern", ex.Message);
    }

    [Fact]
    public void The_themed_arena_matches_the_coded_arena_counts_with_one_light_fewer()
    {
        var plan = DungeonPlan.Build(ContentLoader.Load().Layout, ThemeLoader.Load());
        Assert.InRange(plan.Props.Count(p => p.BoxId == "warden-arena"), CodedArenaProps, 18);
        Assert.True(plan.WallDecor.Count(p => p.BoxId == "warden-arena" && p.Piece.StartsWith("banner_", StringComparison.Ordinal)) >= CodedArenaBanners);
        Assert.Equal(CodedArenaLights - 1, plan.Lights.Count(l => l.BoxId == "warden-arena"));
        Assert.Contains(plan.WallPieces, w => w.Run.BoxId == "warden-arena" && w.Piece.Piece == "wall_cracked");
        Assert.DoesNotContain(plan.Props, p => p.BoxId == "warden-arena" && p.Piece == "chest_gold");
    }
}
