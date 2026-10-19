using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace Keyrun.Content;

/// <summary>A layout box as the dungeon shell sees it (mm, XZ).</summary>
/// <param name="Id">Box id (layout.json), e.g. "choir-room".</param>
/// <param name="IsRoom">Room (true) or corridor (false).</param>
/// <param name="MinX">Lowest x.</param>
/// <param name="MaxX">Highest x.</param>
/// <param name="MinZ">Lowest z.</param>
/// <param name="MaxZ">Highest z.</param>
/// <param name="Gated">True when the box opens on a pull clear (a gate stands across its entry doorway).</param>
public sealed record PlanBox(string Id, bool IsRoom, int MinX, int MaxX, int MinZ, int MaxZ, bool Gated);

/// <summary>Which edge of a box a wall run or opening lies on (theme.json spells them minX, maxX, minZ, maxZ).</summary>
public enum WallSide
{
    /// <summary>The x = MinX edge (runs along z).</summary>
    [EnumMember(Value = "minX")] MinX,

    /// <summary>The x = MaxX edge (runs along z).</summary>
    [EnumMember(Value = "maxX")] MaxX,

    /// <summary>The z = MinZ edge (runs along x).</summary>
    [EnumMember(Value = "minZ")] MinZ,

    /// <summary>The z = MaxZ edge (runs along x).</summary>
    [EnumMember(Value = "maxZ")] MaxZ,
}

/// <summary>The flavour of a room light; the view maps it to a colour and energy.</summary>
public enum LightKind
{
    /// <summary>Warm torchlight (every room but the boss arena).</summary>
    Torch,

    /// <summary>The Warden arena's colder, stronger vault light, so the boss room reads differently.</summary>
    Warden,
}

/// <summary>
/// One kit piece instance (mm). The view loads <see cref="Piece"/> (the kit's file stem), scales it so its nominal
/// size (<see cref="DungeonPlan.Nominal"/>) becomes <see cref="SizeX"/> x <see cref="SizeY"/> x <see cref="SizeZ"/> on
/// its own axes, turns it <see cref="QuarterTurns"/> x 90 degrees about +y (one turn maps local +z to world +x), and puts
/// its origin at (<see cref="X"/>, <see cref="Y"/>, <see cref="Z"/>).
/// </summary>
/// <param name="BoxId">The box it dresses.</param>
/// <param name="Piece">Kit file stem, e.g. "wall" for wall.gltf.glb.</param>
/// <param name="X">Origin x, mm.</param>
/// <param name="Y">Origin y, mm (floor level is 0).</param>
/// <param name="Z">Origin z, mm.</param>
/// <param name="QuarterTurns">Turns about +y, 0..3.</param>
/// <param name="SizeX">Target size along the piece's local x, mm.</param>
/// <param name="SizeY">Target height, mm.</param>
/// <param name="SizeZ">Target size along the piece's local z, mm.</param>
public sealed record KitPiece(string BoxId, string Piece, int X, int Y, int Z, int QuarterTurns, int SizeX, int SizeY, int SizeZ);

/// <summary>One floor tile: an XZ rectangle inside one box, dressed with one kit floor piece.</summary>
/// <param name="BoxId">The box that owns this patch of floor.</param>
/// <param name="Piece">Kit floor piece stem.</param>
/// <param name="MinX">Lowest x.</param>
/// <param name="MaxX">Highest x.</param>
/// <param name="MinZ">Lowest z.</param>
/// <param name="MaxZ">Highest z.</param>
public sealed record FloorTile(string BoxId, string Piece, int MinX, int MaxX, int MinZ, int MaxZ);

/// <summary>
/// A solid stretch of a box edge (mm). The edge is the line <see cref="Line"/> (x for MinX/MaxX, z for MinZ/MaxZ) and
/// the stretch runs from <see cref="Lo"/> to <see cref="Hi"/> along the other axis. A run along x that ends at a box
/// corner is lengthened by the wall thickness there (<see cref="ExtendLo"/>, <see cref="ExtendHi"/>) so the outer corner
/// is closed.
/// </summary>
public sealed record WallRun(string BoxId, WallSide Side, int Line, int Lo, int Hi, int ExtendLo, int ExtendHi);

/// <summary>A gap in a box edge where another box carries on through it (a doorway), mm.</summary>
public sealed record WallOpening(string BoxId, WallSide Side, int Line, int Lo, int Hi);

/// <summary>One kit wall piece of a run; the run's pieces sum to its length.</summary>
public sealed record WallPiece(WallRun Run, int Length, KitPiece Piece);

/// <summary>
/// The solid volume one wall piece (or door-frame pillar) fills, axis-aligned, mm (CAMERA-FEEL, design/ART-PLAN.md lane
/// 10): the follow camera keeps its boom out of these and cuts away any that still stand between it and a body.
/// </summary>
/// <param name="BoxId">The layout box whose edge the piece walls.</param>
/// <param name="MinX">Lowest x.</param>
/// <param name="MaxX">Highest x.</param>
/// <param name="MinZ">Lowest z.</param>
/// <param name="MaxZ">Highest z.</param>
/// <param name="MinY">Bottom, mm (floor level is 0).</param>
/// <param name="MaxY">Top, mm.</param>
public sealed record WallBox(string BoxId, int MinX, int MaxX, int MinZ, int MaxZ, int MinY, int MaxY);

/// <summary>The gate across a gated box's entry doorway; the view hides it while the box is open.</summary>
public sealed record GatePiece(string BoxId, KitPiece Piece);

/// <summary>A point light, mm.</summary>
/// <param name="BoxId">The box it lights.</param>
/// <param name="Kind">Torch or Warden (the view's default colour and energy).</param>
/// <param name="X">X, mm.</param>
/// <param name="Y">Height, mm.</param>
/// <param name="Z">Z, mm.</param>
/// <param name="RangeMm">Omni range, mm.</param>
/// <param name="Color">A theme.json colour (RRGGBB) overriding the kind's colour, or null.</param>
public sealed record LightSpot(string BoxId, LightKind Kind, int X, int Y, int Z, int RangeMm, string? Color = null);

/// <summary>The whole dungeon shell for one layout: everything the view instantiates, in mm.</summary>
/// <param name="Floor">Floor tiles: every box covered exactly once.</param>
/// <param name="Walls">Solid wall runs.</param>
/// <param name="Openings">Doorway gaps.</param>
/// <param name="WallPieces">Kit wall pieces, run by run.</param>
/// <param name="DoorFrames">Pillars framing every doorway cut into a wall.</param>
/// <param name="Gates">One gate per gated box.</param>
/// <param name="Props">Room props, off the walked route.</param>
/// <param name="WallDecor">Wall-mounted torches and banners (on the wall faces, so outside the walkable boxes).</param>
/// <param name="Lights">Point lights.</param>
public sealed record DungeonLayoutPlan(
    IReadOnlyList<FloorTile> Floor,
    IReadOnlyList<WallRun> Walls,
    IReadOnlyList<WallOpening> Openings,
    IReadOnlyList<WallPiece> WallPieces,
    IReadOnlyList<KitPiece> DoorFrames,
    IReadOnlyList<GatePiece> Gates,
    IReadOnlyList<KitPiece> Props,
    IReadOnlyList<KitPiece> WallDecor,
    IReadOnlyList<LightSpot> Lights);

/// <summary>A kit piece's nominal size and lowest point at scale 1 (mm), measured from the KayKit glTF files.</summary>
public sealed record PieceSize(int X, int Y, int Z, int MinY);

/// <summary>
/// Pure placement for the dungeon shell (design/ART-PLAN.md section 8 lane 2; STATE.md D16-D18): which KayKit Dungeon
/// Remastered piece goes where over game/content/data/layout.json. No Godot types and integers only (the purity rule),
/// so every rule is unit-tested (Keyrun.Content.Tests/DungeonPlanTests.cs); game/src/View/DungeonBuilder.cs only
/// instantiates the result. Units: mm here, metres in Godot (1 unit = 1 m, the view divides by 1000).
/// The kit is authored on a 4 m grid (4 m wall pieces, 4 m floor tiles); it is placed at half scale (<see cref="KitScale"/>,
/// 2 m pieces) and walls are squashed to <see cref="WallHeightMm"/> (1.6 m, the GreyBox wall height) so rooms read as
/// open-topped vaults and a wall between the follow camera (about 4.5 m up, 7.5 m back) and a body hides no more than
/// the grey box did (ART-PLAN section 2 readability rule; 3 m and 2 m walls hid bots at doorways in the first frames).
/// Wall fade or cut-away for taller walls is lane 10's (CameraRig).
/// </summary>
public static class DungeonPlan
{
    /// <summary>Kit scale, percent of the authored size.</summary>
    public const int KitScale = 50;

    /// <summary>Largest floor tile edge, mm (a 4 m kit tile at half scale).</summary>
    public const int TileMm = 2000;

    /// <summary>Largest wall piece length, mm.</summary>
    public const int WallPieceMm = 2000;

    /// <summary>Wall height, mm.</summary>
    public const int WallHeightMm = 1600;

    /// <summary>Wall thickness, mm; walls stand outside their box edge so the inner face is the walkable edge.</summary>
    public const int WallThickMm = 500;

    /// <summary>Door-frame pillar footprint, mm.</summary>
    public const int PillarMm = 800;

    /// <summary>Door-frame pillar height, mm (a little over the wall, so the doorway reads).</summary>
    public const int PillarHeightMm = 1900;

    /// <summary>Minimum distance from a prop to the walked route and to every spawn point, mm.</summary>
    public const int PropClearMm = 2000;

    /// <summary>Floor tiles sit with their top this far below y 0, so floor telegraphs (lifted 30 mm) draw above them.</summary>
    public const int FloorTopMm = -5;

    /// <summary>Sconce mount height, mm.</summary>
    public const int SconceHeightMm = 1100;

    /// <summary>Nominal sizes of every piece the plan places (KayKit Dungeon Remastered 1.0, scale 1, mm).</summary>
    public static readonly IReadOnlyDictionary<string, PieceSize> Nominal = new Dictionary<string, PieceSize>(StringComparer.Ordinal)
    {
        ["floor_tile_large"] = new(4000, 150, 4000, -100),
        ["floor_tile_small"] = new(2000, 150, 2000, -100),
        ["floor_tile_small_broken_A"] = new(2000, 150, 2000, -100),
        ["floor_tile_small_weeds_A"] = new(2000, 300, 2000, -100),
        ["floor_dirt_large"] = new(4000, 210, 4000, -100),
        ["wall"] = new(4000, 4000, 1000, 0),
        ["wall_cracked"] = new(4000, 4000, 1260, 0),
        ["wall_arched"] = new(4000, 4000, 1000, 0),
        ["wall_gated"] = new(4000, 4000, 1000, 0),
        ["pillar"] = new(1500, 4000, 1500, 0),
        ["pillar_decorated"] = new(2230, 4000, 1710, 0),
        ["torch_mounted"] = new(550, 1060, 620, -380),
        ["torch_lit"] = new(550, 1130, 550, -400),
        ["candle_triple"] = new(500, 870, 390, 0),
        ["table_long"] = new(2000, 1000, 4000, 0),
        ["table_small_decorated_A"] = new(1040, 1620, 1210, 0),
        ["table_medium_broken"] = new(2280, 970, 2430, 0),
        ["banner_patternA_blue"] = new(1500, 3200, 310, 530),
        ["banner_patternC_red"] = new(1500, 3200, 310, 530),
        ["banner_shield_red"] = new(2230, 3200, 370, 530),
        ["banner_thin_red"] = new(1100, 3170, 360, 560),
        ["barrel_large"] = new(1800, 2000, 1800, 0),
        ["barrel_small_stack"] = new(1850, 1770, 1000, 0),
        ["crates_stacked"] = new(2090, 2140, 2250, 0),
        ["rubble_large"] = new(8130, 3500, 3180, 0),
        ["chest_gold"] = new(1700, 1300, 1450, 0),
        ["chair"] = new(750, 1230, 750, 0),
    };

    /// <summary>The kit file for a piece stem (most pieces are "stem.gltf.glb", a few are "stem.glb").</summary>
    /// <param name="piece">Piece stem.</param>
    /// <returns>The file name inside game/assets/kaykit-dungeon-remastered/.</returns>
    public static string FileOf(string piece) => piece == "chest_gold" ? "chest_gold.glb" : piece + ".gltf.glb";

    /// <summary>The layout's boxes as the planner reads them.</summary>
    /// <param name="layout">The loaded layout.</param>
    /// <returns>Boxes in route order.</returns>
    public static IReadOnlyList<PlanBox> BoxesOf(LayoutDef layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        return layout.Boxes.Select(b => new PlanBox(b.Id, b.Kind == LayoutBoxKind.Room, b.MinX, b.MaxX, b.MinZ, b.MaxZ, b.Gate is not null)).ToList();
    }

    /// <summary>The full shell for a loaded layout.</summary>
    /// <param name="layout">The layout (game/content/data/layout.json).</param>
    /// <param name="theme">Per-room dressing (game/content/data/theme.json, <see cref="ThemeLoader"/>); rooms it lists are
    /// dressed from it alone, the rest from code. Null = no themed rooms (those rooms get only their torches and lights).</param>
    /// <returns>The plan.</returns>
    public static DungeonLayoutPlan Build(LayoutDef layout, ThemeDef? theme = null)
    {
        ArgumentNullException.ThrowIfNull(layout);
        return Build(BoxesOf(layout), layout.Route.Select(p => (p.X, p.Z)).ToList(), theme);
    }

    /// <summary>The full shell for boxes plus the route (one point per box: the doorway into the next box).</summary>
    /// <param name="boxes">Boxes in route order.</param>
    /// <param name="route">Route points, one per box.</param>
    /// <param name="theme">Per-room dressing, or null (see <see cref="Build(LayoutDef, ThemeDef)"/>).</param>
    /// <returns>The plan.</returns>
    public static DungeonLayoutPlan Build(IReadOnlyList<PlanBox> boxes, IReadOnlyList<(int X, int Z)> route, ThemeDef? theme = null)
    {
        ArgumentNullException.ThrowIfNull(boxes);
        ArgumentNullException.ThrowIfNull(route);
        if (route.Count != boxes.Count)
        {
            throw new ArgumentException($"the route needs one point per box ({boxes.Count}), got {route.Count}", nameof(route));
        }
        var walled = Walled(boxes);
        var gates = new List<GatePiece>();
        for (var k = 1; k < boxes.Count; k++)
        {
            var b = boxes[k];
            if (!b.Gated)
            {
                continue;
            }
            var width = b.MaxZ - b.MinZ;
            gates.Add(new GatePiece(b.Id, new KitPiece(b.Id, "wall_gated", route[k - 1].X, 0, Mid(b.MinZ, b.MaxZ), 1, width, WallHeightMm, WallThickMm)));
        }
        var props = new List<KitPiece>();
        var sconces = new List<KitPiece>();
        var lights = new List<LightSpot>();
        foreach (var b in boxes)
        {
            var row = theme?.Rooms.FirstOrDefault(r => r.Id == b.Id);
            if (row is not null && b.IsRoom)
            {
                DressThemed(b, row, props, sconces, lights);
            }
            else
            {
                Dress(b, props, sconces, lights);
            }
        }
        var wallPieces = theme is null ? walled.WallPieces : Crack(walled.WallPieces, theme);
        return walled with { WallPieces = wallPieces, Gates = gates, Props = props, WallDecor = sconces, Lights = lights };
    }

    /// <summary>
    /// A floor prop as a kit piece: <paramref name="piece"/> scaled to <paramref name="pct"/> percent of its nominal size,
    /// its lowest point on the floor, turned <paramref name="turns"/> quarter turns, origin at (x, z).
    /// </summary>
    /// <param name="boxId">The room it dresses.</param>
    /// <param name="piece">Kit stem (a key of <see cref="Nominal"/>).</param>
    /// <param name="x">Origin x, mm.</param>
    /// <param name="z">Origin z, mm.</param>
    /// <param name="turns">Quarter turns about +y.</param>
    /// <param name="pct">Scale, percent of nominal.</param>
    /// <returns>The placed piece.</returns>
    public static KitPiece PropPiece(string boxId, string piece, int x, int z, int turns, int pct)
    {
        var n = Nominal[piece];
        var sy = n.Y * pct / 100;
        return new KitPiece(boxId, piece, x, GroundY(piece, sy), z, turns, n.X * pct / 100, sy, n.Z * pct / 100);
    }

    /// <summary>
    /// Floor tiles for the boxes: every box is covered exactly once (rooms own the doorway overlaps, then earlier boxes
    /// win), each owned patch is split into near-equal tiles no larger than <see cref="TileMm"/>, and no tile leaves its box.
    /// </summary>
    /// <param name="boxes">Boxes in route order.</param>
    /// <returns>The tiles.</returns>
    public static IReadOnlyList<FloorTile> Tiles(IReadOnlyList<PlanBox> boxes)
    {
        ArgumentNullException.ThrowIfNull(boxes);
        var order = boxes.Where(b => b.IsRoom).Concat(boxes.Where(b => !b.IsRoom)).ToList();
        var tiles = new List<FloorTile>();
        for (var r = 0; r < order.Count; r++)
        {
            var b = order[r];
            var higher = order.Take(r).Where(h => Overlaps(h, b)).ToList();
            var xs = Cuts(b.MinX, b.MaxX, higher.SelectMany(h => new[] { h.MinX, h.MaxX }));
            var zs = Cuts(b.MinZ, b.MaxZ, higher.SelectMany(h => new[] { h.MinZ, h.MaxZ }));
            for (var i = 0; i + 1 < xs.Count; i++)
            {
                for (var j = 0; j + 1 < zs.Count; j++)
                {
                    long cx2 = (long)xs[i] + xs[i + 1];
                    long cz2 = (long)zs[j] + zs[j + 1];
                    if (higher.Any(h => cx2 > 2L * h.MinX && cx2 < 2L * h.MaxX && cz2 > 2L * h.MinZ && cz2 < 2L * h.MaxZ))
                    {
                        continue;
                    }
                    foreach (var (x0, x1) in Split(xs[i], xs[i + 1], TileMm))
                    {
                        foreach (var (z0, z1) in Split(zs[j], zs[j + 1], TileMm))
                        {
                            tiles.Add(new FloorTile(b.Id, FloorPiece(b, x0, z0), x0, x1, z0, z1));
                        }
                    }
                }
            }
        }
        return tiles;
    }

    /// <summary>
    /// The floor and walls for the boxes (no gates, props or lights): on every box edge, a wall runs wherever no other
    /// box carries on through the edge, and an opening is left exactly where one does; each doorway cut into a wall gets
    /// a pillar at each side.
    /// </summary>
    /// <param name="boxes">Boxes in route order.</param>
    /// <returns>A plan with Floor, Walls, Openings, WallPieces and DoorFrames filled.</returns>
    public static DungeonLayoutPlan Walled(IReadOnlyList<PlanBox> boxes)
    {
        ArgumentNullException.ThrowIfNull(boxes);
        var walls = new List<WallRun>();
        var openings = new List<WallOpening>();
        var frames = new List<KitPiece>();
        foreach (var b in boxes)
        {
            var others = boxes.Where(o => !ReferenceEquals(o, b)).ToList();
            foreach (var side in new[] { WallSide.MinX, WallSide.MaxX, WallSide.MinZ, WallSide.MaxZ })
            {
                var (line, lo, hi) = Edge(b, side);
                var gaps = others.Where(o => CarriesOn(o, b, side)).Select(o => AlongX(side) ? (o.MinX, o.MaxX) : (o.MinZ, o.MaxZ)).ToList();
                var solid = Subtract(lo, hi, gaps).ToList();
                var open = Complement(lo, hi, solid).ToList();
                foreach (var (s0, s1) in solid)
                {
                    var extLo = AlongX(side) && s0 == lo ? WallThickMm : 0;
                    var extHi = AlongX(side) && s1 == hi ? WallThickMm : 0;
                    walls.Add(new WallRun(b.Id, side, line, s0, s1, extLo, extHi));
                }
                foreach (var (o0, o1) in open)
                {
                    openings.Add(new WallOpening(b.Id, side, line, o0, o1));
                    if (!b.IsRoom || (o0 == lo && o1 == hi))
                    {
                        continue; // doorways are framed on the room wall they cut; corridor ends and side gaps are not
                    }
                    if (o0 > lo)
                    {
                        frames.Add(Pillar(b, side, line, o0 - PillarMm / 2));
                    }
                    if (o1 < hi)
                    {
                        frames.Add(Pillar(b, side, line, o1 + PillarMm / 2));
                    }
                }
            }
        }
        var pieces = new List<WallPiece>();
        foreach (var run in walls)
        {
            var box = boxes.First(b => b.Id == run.BoxId);
            var n = 0;
            foreach (var (a0, a1) in Split(run.Lo - run.ExtendLo, run.Hi + run.ExtendHi, WallPieceMm))
            {
                var centre = Mid(a0, a1);
                var off = Outward(run.Side) * WallThickMm / 2;
                var piece = WallPieceFor(box, n++);
                var kit = AlongX(run.Side)
                    ? new KitPiece(run.BoxId, piece, centre, 0, run.Line + off, 0, a1 - a0, WallHeightMm, WallThickMm)
                    : new KitPiece(run.BoxId, piece, run.Line + off, 0, centre, 1, a1 - a0, WallHeightMm, WallThickMm);
                pieces.Add(new WallPiece(run, a1 - a0, kit));
            }
        }
        return new DungeonLayoutPlan(Tiles(boxes), walls, openings, pieces, frames,
            Array.Empty<GatePiece>(), Array.Empty<KitPiece>(), Array.Empty<KitPiece>(), Array.Empty<LightSpot>());
    }

    /// <summary>
    /// The solid box of every wall piece of <see cref="Walled"/>, in <see cref="DungeonLayoutPlan.WallPieces"/> order (so
    /// box i is wall piece i): the piece's footprint (length along its run, <see cref="WallThickMm"/> outward from the box
    /// edge) from the floor to <see cref="WallHeightMm"/>. Lane 10's camera reads these (<see cref="CameraBoom"/>).
    /// </summary>
    /// <param name="boxes">Boxes in route order.</param>
    /// <returns>One box per wall piece.</returns>
    public static IReadOnlyList<WallBox> WallBoxes(IReadOnlyList<PlanBox> boxes)
    {
        ArgumentNullException.ThrowIfNull(boxes);
        return Walled(boxes).WallPieces.Select(w => SolidOf(w.Piece)).ToList();
    }

    /// <summary>The solid box of every door-frame pillar of <see cref="Walled"/>, in <see cref="DungeonLayoutPlan.DoorFrames"/> order.</summary>
    /// <param name="boxes">Boxes in route order.</param>
    /// <returns>One box per pillar.</returns>
    public static IReadOnlyList<WallBox> FrameBoxes(IReadOnlyList<PlanBox> boxes)
    {
        ArgumentNullException.ThrowIfNull(boxes);
        return Walled(boxes).DoorFrames.Select(SolidOf).ToList();
    }

    /// <summary>The axis-aligned box a placed piece fills: its target size about its origin, turned by its quarter turns.</summary>
    /// <param name="p">A wall piece or pillar (origin at the footprint centre, on the floor).</param>
    /// <returns>Its box, mm.</returns>
    public static WallBox SolidOf(KitPiece p)
    {
        ArgumentNullException.ThrowIfNull(p);
        var (sx, sz) = p.QuarterTurns % 2 == 0 ? (p.SizeX, p.SizeZ) : (p.SizeZ, p.SizeX);
        return new WallBox(p.BoxId, p.X - sx / 2, p.X + sx - sx / 2, p.Z - sz / 2, p.Z + sz - sz / 2, p.Y, p.Y + p.SizeY);
    }

    /// <summary>The y (mm) that puts a scaled piece's lowest point on <paramref name="floorMm"/>.</summary>
    /// <param name="piece">Piece stem.</param>
    /// <param name="sizeY">Target height, mm.</param>
    /// <param name="floorMm">Where its lowest point should sit.</param>
    /// <returns>Origin y, mm.</returns>
    public static int GroundY(string piece, int sizeY, int floorMm = 0)
    {
        var n = Nominal[piece];
        return floorMm - (int)((long)n.MinY * sizeY / n.Y);
    }

    /// <summary>The y (mm) that puts a scaled floor tile's top just under y 0 (<see cref="FloorTopMm"/>).</summary>
    /// <param name="piece">Floor piece stem.</param>
    /// <returns>Origin y, mm.</returns>
    public static int FloorY(string piece)
    {
        // Every kit floor piece's base slab spans y -100..50 at scale 1 (dirt, weeds and broken tiles add bumps above
        // it), so the slab top is 50 mm over the origin; the plan places floors at the kit scale.
        var n = Nominal[piece];
        var slabTop = n.MinY + 150;
        return FloorTopMm - slabTop * KitScale / 100;
    }

    // ---- floor and walls -------------------------------------------------------------------------------------------

    private static bool Overlaps(PlanBox a, PlanBox b) => a.MinX < b.MaxX && b.MinX < a.MaxX && a.MinZ < b.MaxZ && b.MinZ < a.MaxZ;

    private static List<int> Cuts(int lo, int hi, IEnumerable<int> cuts) =>
        new[] { lo, hi }.Concat(cuts.Where(c => c > lo && c < hi)).Distinct().OrderBy(c => c).ToList();

    /// <summary>[lo, hi] split into the fewest near-equal pieces no longer than <paramref name="max"/>.</summary>
    private static IEnumerable<(int Lo, int Hi)> Split(int lo, int hi, int max)
    {
        var len = hi - lo;
        if (len <= 0)
        {
            yield break;
        }
        var n = (len + max - 1) / max;
        var at = lo;
        for (var i = 0; i < n; i++)
        {
            var size = len / n + (i < len % n ? 1 : 0);
            yield return (at, at + size);
            at += size;
        }
    }

    private static bool AlongX(WallSide side) => side is WallSide.MinZ or WallSide.MaxZ;

    private static int Outward(WallSide side) => side is WallSide.MaxX or WallSide.MaxZ ? 1 : -1;

    private static (int Line, int Lo, int Hi) Edge(PlanBox b, WallSide side) => side switch
    {
        WallSide.MinX => (b.MinX, b.MinZ, b.MaxZ),
        WallSide.MaxX => (b.MaxX, b.MinZ, b.MaxZ),
        WallSide.MinZ => (b.MinZ, b.MinX, b.MaxX),
        _ => (b.MaxZ, b.MinX, b.MaxX),
    };

    // Another box carries on through b's edge when it holds points just past the edge (it reaches across the line).
    private static bool CarriesOn(PlanBox o, PlanBox b, WallSide side) => side switch
    {
        WallSide.MaxX => o.MinX <= b.MaxX && o.MaxX > b.MaxX,
        WallSide.MinX => o.MinX < b.MinX && o.MaxX >= b.MinX,
        WallSide.MaxZ => o.MinZ <= b.MaxZ && o.MaxZ > b.MaxZ,
        _ => o.MinZ < b.MinZ && o.MaxZ >= b.MinZ,
    };

    // [lo, hi] minus the union of the gaps, as ordered non-empty intervals (GreyBox.cs's rule).
    private static IEnumerable<(int Lo, int Hi)> Subtract(int lo, int hi, IEnumerable<(int Lo, int Hi)> gaps)
    {
        var cursor = lo;
        foreach (var (gLo, gHi) in gaps.OrderBy(g => g.Lo))
        {
            if (gHi <= cursor)
            {
                continue;
            }
            if (gLo > cursor)
            {
                var end = Math.Min(gLo, hi);
                if (end > cursor)
                {
                    yield return (cursor, end);
                }
            }
            cursor = Math.Max(cursor, gHi);
            if (cursor >= hi)
            {
                yield break;
            }
        }
        if (cursor < hi)
        {
            yield return (cursor, hi);
        }
    }

    private static IEnumerable<(int Lo, int Hi)> Complement(int lo, int hi, IReadOnlyList<(int Lo, int Hi)> solid)
    {
        var cursor = lo;
        foreach (var (s0, s1) in solid)
        {
            if (s0 > cursor)
            {
                yield return (cursor, s0);
            }
            cursor = s1;
        }
        if (cursor < hi)
        {
            yield return (cursor, hi);
        }
    }

    private static KitPiece Pillar(PlanBox b, WallSide side, int line, int along)
    {
        var off = Outward(side) * WallThickMm / 2;
        return AlongX(side)
            ? new KitPiece(b.Id, "pillar", along, 0, line + off, 0, PillarMm, PillarHeightMm, PillarMm)
            : new KitPiece(b.Id, "pillar", line + off, 0, along, 0, PillarMm, PillarHeightMm, PillarMm);
    }

    private static int Mid(int a, int b) => (int)(((long)a + b) / 2);

    // A small deterministic hash so tile and wall variants scatter the same way every run.
    private static int Hash(int a, int b) => (int)((((uint)a * 73856093u) ^ ((uint)b * 19349663u)) % 1000u);

    private static string FloorPiece(PlanBox b, int x, int z)
    {
        var h = Hash(x, z);
        return b.Id switch
        {
            "seed-chamber" => h < 450 ? "floor_dirt_large" : h < 700 ? "floor_tile_small_broken_A" : h < 850 ? "floor_tile_small_weeds_A" : "floor_tile_large",
            "choir-room" => h < 250 ? "floor_tile_small_broken_A" : "floor_tile_large",
            "warden-arena" => "floor_tile_large",
            _ when !b.IsRoom => "floor_tile_small",
            _ => h < 120 ? "floor_tile_small_broken_A" : "floor_tile_large",
        };
    }

    private static string WallPieceFor(PlanBox b, int n)
    {
        var h = Hash(b.MinX + n * 3001, b.MaxZ - n * 7);
        return b.Id switch
        {
            "choir-room" => n % 2 == 1 ? "wall_arched" : "wall",
            "seed-chamber" => h < 500 ? "wall_cracked" : "wall",
            "warden-arena" => h < 200 ? "wall_cracked" : "wall",
            _ => h < 150 ? "wall_cracked" : "wall",
        };
    }

    // ---- props, sconces and lights ---------------------------------------------------------------------------------

    private static KitPiece Prop(PlanBox b, string piece, int x, int z, int turns, int pct) => PropPiece(b.Id, piece, x, z, turns, pct);

    // A theme.json room: every prop, wall piece of decor and light comes from its row (ROOM-THEMING-1).
    private static void DressThemed(PlanBox b, RoomThemeDef row, List<KitPiece> props, List<KitPiece> sconces, List<LightSpot> lights)
    {
        foreach (var p in row.Props)
        {
            props.Add(Prop(b, p.Piece, p.X, p.Z, p.Turns, p.Pct));
        }
        foreach (var d in row.Decor ?? new ValueList<ThemeDecor>())
        {
            sconces.Add(d.Piece == "torch_mounted" ? Sconce(b, d.Side, d.Along) : Hanging(b, d.Piece, d.Side, d.Along));
        }
        foreach (var l in row.Lights)
        {
            lights.Add(new LightSpot(b.Id, LightKind.Torch, l.X, l.YMm, l.Z, l.RangeMm, l.Color));
        }
    }

    // The wall pieces theme.json marks cracked show as wall_cracked; the run, length and box (so the camera's occluder
    // boxes) are unchanged.
    private static IReadOnlyList<WallPiece> Crack(IReadOnlyList<WallPiece> pieces, ThemeDef theme)
    {
        var points = theme.Rooms.SelectMany(r => (r.Cracked ?? new ValueList<ThemeWallPoint>()).Select(c => (r.Id, c.Side, c.Along))).ToList();
        if (points.Count == 0)
        {
            return pieces;
        }
        return pieces.Select(w =>
        {
            var centre = AlongX(w.Run.Side) ? w.Piece.X : w.Piece.Z;
            var hit = points.Any(c => c.Id == w.Run.BoxId && c.Side == w.Run.Side
                && 2L * c.Along >= 2L * centre - w.Length && 2L * c.Along < 2L * centre + w.Length);
            return hit ? w with { Piece = w.Piece with { Piece = "wall_cracked" } } : w;
        }).ToList();
    }

    // A wall hanging on the inner face of one of b's walls (banners hang off a 1 m kit wall centred on its origin).
    private static KitPiece Hanging(PlanBox b, string piece, WallSide side, int along)
    {
        var n = Nominal[piece];
        var off = Outward(side) * WallThickMm / 2;
        var (line, _, _) = Edge(b, side);
        var turns = Facing(side);
        var (x, z) = AlongX(side) ? (along, line + off) : (line + off, along);
        return new KitPiece(b.Id, piece, x, 0, z, turns, n.X * KitScale / 100, n.Y * WallHeightMm / 4000, n.Z * KitScale / 100);
    }

    // Turns that point a piece's local +z into the room from the given wall.
    private static int Facing(WallSide side) => side switch
    {
        WallSide.MinZ => 0,
        WallSide.MinX => 1,
        WallSide.MaxZ => 2,
        _ => 3,
    };

    private static KitPiece Sconce(PlanBox b, WallSide side, int along)
    {
        var n = Nominal["torch_mounted"];
        var (line, _, _) = Edge(b, side);
        var (x, z) = AlongX(side) ? (along, line) : (line, along);
        var sy = n.Y * KitScale / 100;
        return new KitPiece(b.Id, "torch_mounted", x, SconceHeightMm, z, Facing(side), n.X * KitScale / 100, sy, n.Z * KitScale / 100);
    }

    private static void Dress(PlanBox b, List<KitPiece> props, List<KitPiece> sconces, List<LightSpot> lights)
    {
        var w = b.MaxX - b.MinX;
        var hz = (b.MaxZ - b.MinZ) / 2;
        var cz = Mid(b.MinZ, b.MaxZ);
        var x1 = b.MinX + w / 4;
        var x2 = b.MaxX - w / 4;
        if (!b.IsRoom)
        {
            var mid = Mid(b.MinX, b.MaxX);
            sconces.Add(Sconce(b, WallSide.MinZ, mid));
            sconces.Add(Sconce(b, WallSide.MaxZ, mid));
            return;
        }

        // Torches on both long walls at the quarter points; the warm lights hang by them, alternating sides.
        foreach (var x in new[] { x1, x2 })
        {
            sconces.Add(Sconce(b, WallSide.MinZ, x));
            sconces.Add(Sconce(b, WallSide.MaxZ, x));
        }
        var range = Math.Min(16000, 2 * hz + 3000);
        if (b.Id != "warden-arena")
        {
            lights.Add(new LightSpot(b.Id, LightKind.Torch, x1, 2200, b.MinZ + 1500, range));
            lights.Add(new LightSpot(b.Id, LightKind.Torch, x2, 2200, b.MaxZ - 1500, range));
        }

        // The entrance, the Choir room, the Pair hall and the Seed chamber are dressed from game/content/data/theme.json
        // (DressThemed; ROOM-THEMING-1 and -2); lane 7 moves the room below into it.
        switch (b.Id)
        {
            case "warden-arena":
                // The vault: braziers on the rim, columns at the inner corners, the Warden's throne and banners at the far wall.
                foreach (var x in new[] { b.MinX + 3000, Mid(b.MinX, b.MaxX), b.MaxX - 3000 })
                {
                    props.Add(Prop(b, "torch_lit", x, b.MinZ + 1500, 0, 150));
                    props.Add(Prop(b, "torch_lit", x, b.MaxZ - 1500, 0, 150));
                }
                props.Add(Prop(b, "pillar", b.MinX + 5000, b.MinZ + 4000, 0, 60));
                props.Add(Prop(b, "pillar", b.MinX + 5000, b.MaxZ - 4000, 0, 60));
                props.Add(Prop(b, "pillar", b.MaxX - 5000, b.MinZ + 4000, 0, 60));
                props.Add(Prop(b, "pillar", b.MaxX - 5000, b.MaxZ - 4000, 0, 60));
                props.Add(Prop(b, "chair", b.MaxX - 1800, cz, 3, 170));
                sconces.Add(Hanging(b, "banner_shield_red", WallSide.MaxX, cz - 4000));
                sconces.Add(Hanging(b, "banner_shield_red", WallSide.MaxX, cz + 4000));
                sconces.Add(Hanging(b, "banner_shield_red", WallSide.MaxX, cz - 9000));
                sconces.Add(Hanging(b, "banner_shield_red", WallSide.MaxX, cz + 9000));
                // Cold vault light: four high lamps over the floor quarters, stronger than the torches.
                foreach (var x in new[] { x1, x2 })
                {
                    lights.Add(new LightSpot(b.Id, LightKind.Warden, x, 6000, cz - hz / 2, 16000));
                    lights.Add(new LightSpot(b.Id, LightKind.Warden, x, 6000, cz + hz / 2, 16000));
                }
                break;
        }
    }
}
