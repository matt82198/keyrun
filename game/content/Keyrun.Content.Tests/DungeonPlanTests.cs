using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// The dungeon shell's pure placement (design/ART-PLAN.md lane 2; <see cref="DungeonPlan"/>): the kit floor tiles cover
/// every layout box exactly once and never leave the boxes, the walls open exactly where two boxes overlap (the
/// doorways) and stand solid everywhere else, and props stay off the walked route and away from spawn points. The
/// checks are written against the layout boxes themselves (sampled points), not against the planner's own helpers.
/// </summary>
public sealed class DungeonPlanTests
{
    private const int Step = 250;

    private static LayoutDef Real => ContentLoader.Load().Layout;

    private static PlanBox Box(string id, bool room, int minX, int maxX, int minZ, int maxZ) =>
        new(id, room, minX, maxX, minZ, maxZ, false);

    private static bool Inside(PlanBox b, long x2, long z2) =>
        x2 > 2L * b.MinX && x2 < 2L * b.MaxX && z2 > 2L * b.MinZ && z2 < 2L * b.MaxZ;

    // Doubled coordinates of the centres of a Step grid inside the box (so centres need no fractions).
    private static IEnumerable<(long X2, long Z2)> Samples(PlanBox b)
    {
        for (long x = b.MinX; x < b.MaxX; x += Step)
        {
            for (long z = b.MinZ; z < b.MaxZ; z += Step)
            {
                yield return (2 * x + Step + 1, 2 * z + Step + 1); // +0.5 mm: never on an integer tile edge
            }
        }
    }

    private static void AssertTiling(IReadOnlyList<PlanBox> boxes, IReadOnlyList<FloorTile> tiles)
    {
        Assert.NotEmpty(tiles);
        foreach (var t in tiles)
        {
            Assert.True(t.MaxX > t.MinX && t.MaxZ > t.MinZ, $"empty tile {t}");
            Assert.True(boxes.Any(b => t.MinX >= b.MinX && t.MaxX <= b.MaxX && t.MinZ >= b.MinZ && t.MaxZ <= b.MaxZ),
                $"tile {t} lies outside every layout box");
            Assert.True(t.MaxX - t.MinX <= DungeonPlan.TileMm && t.MaxZ - t.MinZ <= DungeonPlan.TileMm, $"tile {t} is larger than one kit tile");
        }
        foreach (var b in boxes)
        {
            foreach (var (x2, z2) in Samples(b))
            {
                var covering = tiles.Count(t => x2 > 2L * t.MinX && x2 < 2L * t.MaxX && z2 > 2L * t.MinZ && z2 < 2L * t.MaxZ);
                Assert.True(covering == 1, $"box {b.Id}: point ({x2 / 2.0}, {z2 / 2.0}) is under {covering} tiles, expected exactly 1");
            }
        }
    }

    // A wall run covers a point on box b's edge iff no other box carries on through the edge there (just past it).
    private static bool ExpectOpen(IReadOnlyList<PlanBox> boxes, PlanBox b, WallSide side, long along2)
    {
        foreach (var o in boxes)
        {
            if (ReferenceEquals(o, b))
            {
                continue;
            }
            var past = side switch
            {
                WallSide.MaxX => Inside(o, 2L * b.MaxX + 1, along2),
                WallSide.MinX => Inside(o, 2L * b.MinX - 1, along2),
                WallSide.MaxZ => Inside(o, along2, 2L * b.MaxZ + 1),
                _ => Inside(o, along2, 2L * b.MinZ - 1),
            };
            if (past)
            {
                return true;
            }
        }
        return false;
    }

    private static void AssertWalls(IReadOnlyList<PlanBox> boxes, DungeonLayoutPlan plan)
    {
        var checkedPoints = 0;
        foreach (var b in boxes)
        {
            foreach (var side in new[] { WallSide.MinX, WallSide.MaxX, WallSide.MinZ, WallSide.MaxZ })
            {
                var alongX = side is WallSide.MinZ or WallSide.MaxZ;
                var lo = alongX ? b.MinX : b.MinZ;
                var hi = alongX ? b.MaxX : b.MaxZ;
                for (long a = lo; a < hi; a += Step)
                {
                    var a2 = 2 * a + Step + 1;
                    var open = ExpectOpen(boxes, b, side, a2);
                    var walled = plan.Walls.Any(w => w.BoxId == b.Id && w.Side == side && a2 > 2L * w.Lo && a2 < 2L * w.Hi);
                    var opening = plan.Openings.Any(w => w.BoxId == b.Id && w.Side == side && a2 > 2L * w.Lo && a2 < 2L * w.Hi);
                    Assert.True(open != walled, $"box {b.Id} {side} at {a2 / 2.0}: expected {(open ? "an opening" : "a wall")}, walled={walled}");
                    Assert.True(open == opening, $"box {b.Id} {side} at {a2 / 2.0}: expected opening={open}, planner opening={opening}");
                    checkedPoints++;
                }
            }
        }
        Assert.True(checkedPoints > 100, "vacuity guard: only " + checkedPoints + " edge points checked");
    }

    [Fact]
    public void Every_layout_box_is_fully_tiled_and_no_tile_lies_outside_any_box()
    {
        var boxes = DungeonPlan.BoxesOf(Real);
        Assert.Equal(9, boxes.Count);
        AssertTiling(boxes, DungeonPlan.Build(Real).Floor);
    }

    [Fact]
    public void Odd_sized_and_overlapping_boxes_are_tiled_exactly_once()
    {
        var boxes = new[]
        {
            Box("a", true, 0, 7000, -2500, 2500),
            Box("c", false, 6000, 13500, -1500, 1500),
            Box("b", true, 12500, 20000, -4000, 3250),
        };
        AssertTiling(boxes, DungeonPlan.Tiles(boxes));
    }

    [Fact]
    public void Walls_open_exactly_where_two_boxes_overlap_and_stand_solid_elsewhere()
    {
        var layout = Real;
        AssertWalls(DungeonPlan.BoxesOf(layout), DungeonPlan.Build(layout));
    }

    [Fact]
    public void The_entrance_doorway_is_the_corridor_width_and_the_corridor_end_is_open()
    {
        var plan = DungeonPlan.Build(Real);
        var door = Assert.Single(plan.Openings, o => o.BoxId == "entrance" && o.Side == WallSide.MaxX);
        Assert.Equal((-1500, 1500, 6000), (door.Lo, door.Hi, door.Line));
        Assert.Equal(new[] { (-6000, -1500), (1500, 6000) },
            plan.Walls.Where(w => w.BoxId == "entrance" && w.Side == WallSide.MaxX).Select(w => (w.Lo, w.Hi)).OrderBy(p => p.Lo).ToArray());
        Assert.DoesNotContain(plan.Walls, w => w.BoxId == "corridor-1" && w.Side is WallSide.MinX or WallSide.MaxX);
        Assert.Equal(new[] { (6000, 10000) },
            plan.Walls.Where(w => w.BoxId == "corridor-1" && w.Side == WallSide.MaxZ).Select(w => (w.Lo, w.Hi)).ToArray());
    }

    [Fact]
    public void Two_overlapping_boxes_get_one_opening_exactly_over_the_overlap()
    {
        var boxes = new[] { Box("a", true, 0, 10000, 0, 10000), Box("b", false, 9000, 20000, 4000, 6000) };
        var plan = DungeonPlan.Walled(boxes);
        AssertWalls(boxes, plan);
        var door = Assert.Single(plan.Openings, o => o.BoxId == "a");
        Assert.Equal((WallSide.MaxX, 10000, 4000, 6000), (door.Side, door.Line, door.Lo, door.Hi));
        Assert.Equal(new[] { (0, 4000), (6000, 10000) },
            plan.Walls.Where(w => w.BoxId == "a" && w.Side == WallSide.MaxX).Select(w => (w.Lo, w.Hi)).OrderBy(p => p.Lo).ToArray());
        Assert.Single(plan.Openings, o => o.BoxId == "b" && o.Side == WallSide.MinX && o.Lo == 4000 && o.Hi == 6000);
        Assert.Single(plan.DoorFrames.Select(f => f.BoxId).Distinct());
    }

    [Fact]
    public void Wall_pieces_cover_each_wall_run_with_kit_sized_pieces()
    {
        var plan = DungeonPlan.Build(Real);
        Assert.NotEmpty(plan.WallPieces);
        foreach (var run in plan.Walls)
        {
            var pieces = plan.WallPieces.Where(p => p.Run == run).ToList();
            Assert.NotEmpty(pieces);
            Assert.All(pieces, p => Assert.InRange(p.Length, 1, DungeonPlan.WallPieceMm));
            Assert.Equal(run.Hi - run.Lo + run.ExtendLo + run.ExtendHi, pieces.Sum(p => p.Length));
        }
    }

    [Fact]
    public void The_shipped_path_to_the_boss_builds_no_gate()
    {
        // PATH-TO-BOSS (STATE.md D44): no corridor of the shipped layout is gated, so the plan stands no gate piece
        // across any doorway (the view draws none).
        var layout = Real;
        var plan = DungeonPlan.Build(layout, ThemeLoader.Load());
        Assert.DoesNotContain(layout.Boxes, b => b.Gate is not null);
        Assert.Empty(plan.Gates);
    }

    [Fact]
    public void Props_stay_inside_their_room_off_the_route_and_away_from_spawn_points()
    {
        var reg = ContentLoader.Load();
        var layout = reg.Layout;
        var plan = DungeonPlan.Build(layout, ThemeLoader.Load());
        var dungeon = reg.Dungeons[layout.Dungeon];
        var spawns = new List<(int X, int Z)>();
        foreach (var room in layout.Boxes.Where(b => b.Pull is not null))
        {
            spawns.Add((room.Origin!.X, room.Origin.Z));
            var pull = dungeon.Pulls.FirstOrDefault(p => p.Id == room.Pull);
            if (pull is not null)
            {
                spawns.AddRange(pull.Enemies.Select(e => (room.Origin.X + e.Spawn.X, room.Origin.Z + e.Spawn.Z)));
            }
        }
        spawns.Add((layout.Start.X, layout.Start.Z));
        var route = new List<(int X, int Z)> { (layout.Start.X, layout.Start.Z) };
        route.AddRange(layout.Route.Select(p => (p.X, p.Z)));

        Assert.True(plan.Props.Count >= 5 * 4, "vacuity guard: " + plan.Props.Count + " props");
        foreach (var room in layout.Boxes.Where(b => b.Kind == LayoutBoxKind.Room))
        {
            Assert.True(plan.Props.Count(p => p.BoxId == room.Id) >= 4, $"room {room.Id} has fewer than 4 props");
        }
        foreach (var p in plan.Props)
        {
            var room = layout.Boxes.Single(b => b.Id == p.BoxId);
            Assert.InRange(p.X, room.MinX, room.MaxX);
            Assert.InRange(p.Z, room.MinZ, room.MaxZ);
            for (var i = 0; i + 1 < route.Count; i++)
            {
                var d = SegmentDistance(p.X, p.Z, route[i], route[i + 1]);
                Assert.True(d >= DungeonPlan.PropClearMm, $"prop {p.Piece} at ({p.X},{p.Z}) is {d:F0} mm from the route");
            }
            foreach (var s in spawns)
            {
                var d = Math.Sqrt(Math.Pow(p.X - s.X, 2) + Math.Pow(p.Z - s.Z, 2));
                Assert.True(d >= DungeonPlan.PropClearMm, $"prop {p.Piece} at ({p.X},{p.Z}) is {d:F0} mm from spawn ({s.X},{s.Z})");
            }
        }
    }

    [Fact]
    public void Every_room_is_lit_13_lights_since_the_warden_arena_went_from_four_coded_lamps_to_three_theme_lights_of_its_own_kind()
    {
        var layout = Real;
        var plan = DungeonPlan.Build(layout, ThemeLoader.Load());
        foreach (var room in layout.Boxes.Where(b => b.Kind == LayoutBoxKind.Room))
        {
            Assert.Contains(plan.Lights, l => l.BoxId == room.Id);
        }
        Assert.All(plan.Lights.Where(l => l.BoxId == "warden-arena"), l => Assert.Equal(LightKind.Warden, l.Kind));
        Assert.All(plan.Lights.Where(l => l.BoxId != "warden-arena"), l => Assert.Equal(LightKind.Torch, l.Kind));
        Assert.Equal(3, plan.Lights.Count(l => l.BoxId == "warden-arena"));
        // 12 before ROOM-THEMING-1, plus the Choir chancel and the Seed vault candle lights (theme.json), then the arena's four
        // coded Warden lamps became three theme.json lights with "kind": "warden" (ROOM-THEMING-3, the per-room cap of 3).
        Assert.Equal(13, plan.Lights.Count);
    }

    private static double SegmentDistance(int px, int pz, (int X, int Z) a, (int X, int Z) b)
    {
        double dx = b.X - a.X, dz = b.Z - a.Z;
        var len2 = dx * dx + dz * dz;
        var t = len2 == 0 ? 0 : Math.Clamp(((px - a.X) * dx + (pz - a.Z) * dz) / len2, 0, 1);
        var cx = a.X + t * dx;
        var cz = a.Z + t * dz;
        return Math.Sqrt((px - cx) * (px - cx) + (pz - cz) * (pz - cz));
    }
}
