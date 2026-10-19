using System;
using System.Collections.Generic;
using System.Linq;

namespace Keyrun.Content;

/// <summary>
/// Per-room dressing data (game/content/data/theme.json, logical path <c>theme.json</c>; C#-only like layout.json and read
/// only by the view through <see cref="DungeonPlan.Build(LayoutDef, ThemeDef)"/>; ROOM-THEMING-1, design/ART-PLAN.md lane
/// 5). A room listed here is dressed entirely from its row (props, wall decor, lights, cracked wall pieces); a room not
/// listed keeps DungeonPlan's code dressing. Checked fail-closed at load by <see cref="RoomTheme.Violations"/>.
/// </summary>
public sealed record ThemeDef
{
    /// <summary>One row per themed room, each id a layout room, no repeats.</summary>
    public required ValueList<RoomThemeDef> Rooms { get; init; }
}

/// <summary>One themed room's dressing, mm, in layout (world) coordinates.</summary>
public sealed record RoomThemeDef
{
    /// <summary>The layout room this row dresses, e.g. "choir-room".</summary>
    public required string Id { get; init; }

    /// <summary>Floor props (kit pieces standing on the floor).</summary>
    public required ValueList<ThemeProp> Props { get; init; }

    /// <summary>Wall decor: <c>torch_mounted</c> sconces and <c>banner_*</c> hangings on the room's own walls; null = none.</summary>
    public ValueList<ThemeDecor>? Decor { get; init; }

    /// <summary>The room's point lights (warm torchlight unless a colour is given).</summary>
    public required ValueList<ThemeLight> Lights { get; init; }

    /// <summary>Wall pieces to show as <c>wall_cracked</c> (same geometry, so camera occluders do not move); null = none.</summary>
    public ValueList<ThemeWallPoint>? Cracked { get; init; }
}

/// <summary>A floor prop: the kit piece <see cref="Piece"/> at (<see cref="X"/>, <see cref="Z"/>), turned and scaled.</summary>
public sealed record ThemeProp
{
    /// <summary>Kit file stem, a key of <see cref="DungeonPlan.Nominal"/>.</summary>
    public required string Piece { get; init; }

    /// <summary>Origin x, mm.</summary>
    public required int X { get; init; }

    /// <summary>Origin z, mm.</summary>
    public required int Z { get; init; }

    /// <summary>Quarter turns about +y, 0..3 (one turn maps local +z to world +x).</summary>
    public required int Turns { get; init; }

    /// <summary>Scale, percent of the piece's nominal size.</summary>
    public required int Pct { get; init; }
}

/// <summary>A wall-mounted piece on the inner face of one of the room's walls.</summary>
public sealed record ThemeDecor
{
    /// <summary><c>torch_mounted</c> or a <c>banner_*</c> stem.</summary>
    public required string Piece { get; init; }

    /// <summary>Which wall of the room.</summary>
    public required WallSide Side { get; init; }

    /// <summary>Position along the wall (x for minZ/maxZ walls, z for minX/maxX walls), mm.</summary>
    public required int Along { get; init; }
}

/// <summary>A room light, mm.</summary>
public sealed record ThemeLight
{
    /// <summary>X, mm.</summary>
    public required int X { get; init; }

    /// <summary>Z, mm.</summary>
    public required int Z { get; init; }

    /// <summary>Height, mm.</summary>
    public required int YMm { get; init; }

    /// <summary>Omni range, mm.</summary>
    public required int RangeMm { get; init; }

    /// <summary>Colour as six hex digits RRGGBB, or null for the view's torch colour.</summary>
    public string? Color { get; init; }
}

/// <summary>A point on one of the room's walls.</summary>
public sealed record ThemeWallPoint
{
    /// <summary>Which wall.</summary>
    public required WallSide Side { get; init; }

    /// <summary>Position along the wall, mm.</summary>
    public required int Along { get; init; }
}

/// <summary>
/// What the placement rule checks a theme against (mm): the layout boxes, the walked route (the party start, then every
/// route point), every spawn point and every door (wall openings as zero-width segments, door-frame pillars and gates).
/// </summary>
/// <param name="Boxes">Layout boxes in route order.</param>
/// <param name="Lane">The party start followed by the layout's route points.</param>
/// <param name="Spawns">Pull origins, enemy spawns (origin plus content offset), pull-room starts and the party start.</param>
/// <param name="Doors">Openings, door frames and gates.</param>
/// <param name="Walls">Solid wall runs (decor must hang on one).</param>
/// <param name="Openings">Doorway gaps (decor keeps <see cref="RoomTheme.DecorDoorClearMm"/> from them).</param>
public sealed record ThemeSite(
    IReadOnlyList<PlanBox> Boxes,
    IReadOnlyList<(int X, int Z)> Lane,
    IReadOnlyList<(int X, int Z)> Spawns,
    IReadOnlyList<WallBox> Doors,
    IReadOnlyList<WallRun> Walls,
    IReadOnlyList<WallOpening> Openings)
{
    /// <summary>The site for loaded content.</summary>
    /// <param name="registry">Content (layout plus the dungeon's pulls).</param>
    /// <returns>The site.</returns>
    public static ThemeSite Of(ContentRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        var layout = registry.Layout;
        var boxes = DungeonPlan.BoxesOf(layout);
        var lane = new List<(int X, int Z)> { (layout.Start.X, layout.Start.Z) };
        lane.AddRange(layout.Route.Select(p => (p.X, p.Z)));
        var spawns = new List<(int X, int Z)> { (layout.Start.X, layout.Start.Z) };
        registry.Dungeons.TryGetValue(layout.Dungeon, out var dungeon);
        foreach (var room in layout.Boxes.Where(b => b.Pull is not null))
        {
            if (room.Origin is { } o)
            {
                spawns.Add((o.X, o.Z));
                var pull = dungeon?.Pulls.FirstOrDefault(p => p.Id == room.Pull);
                if (pull is not null)
                {
                    spawns.AddRange(pull.Enemies.Select(e => (o.X + e.Spawn.X, o.Z + e.Spawn.Z)));
                }
            }
            if (room.Start is { } s)
            {
                spawns.Add((s.X, s.Z));
            }
        }
        var shell = DungeonPlan.Build(layout);
        var doors = new List<WallBox>();
        doors.AddRange(shell.Openings.Select(Segment));
        doors.AddRange(shell.DoorFrames.Select(DungeonPlan.SolidOf));
        doors.AddRange(shell.Gates.Select(g => DungeonPlan.SolidOf(g.Piece)));
        return new ThemeSite(boxes, lane, spawns, doors, shell.Walls, shell.Openings);
    }

    private static WallBox Segment(WallOpening o) => o.Side is WallSide.MinX or WallSide.MaxX
        ? new WallBox(o.BoxId, o.Line, o.Line, o.Lo, o.Hi, 0, 0)
        : new WallBox(o.BoxId, o.Lo, o.Hi, o.Line, o.Line, 0, 0);
}

/// <summary>
/// The pure placement rule for themed rooms (ROOM-THEMING-1; integers only). A floor prop's footprint is its scaled kit
/// size about its origin (<see cref="DungeonPlan.SolidOf"/>). Every prop must: name a kit stem in
/// <see cref="DungeonPlan.Nominal"/>; lie inside its room box (so it crosses no wall, which stands outside the box edge);
/// stay out of the centre lane, the band <see cref="LaneHalfMm"/> either side of every route segment (start, then route
/// points); keep <see cref="DungeonPlan.PropClearMm"/> from every spawn (no exemption, the Cantor included); keep
/// <see cref="DoorClearMm"/> from every door opening, door frame and gate. Decor must hang on a solid run of its room's
/// own wall, <see cref="DecorDoorClearMm"/> clear of any opening on that wall. Lights: 1 to <see cref="MaxLightsPerRoom"/>
/// per room, inside the room, colour null or RRGGBB. Props never block the party: the sim clamps movement only against
/// layout boxes, so this rule is the guarantee that the walked lane, spawns and doors stay visibly clear.
/// </summary>
public static class RoomTheme
{
    /// <summary>Half-width of the centre lane kept clear either side of every route segment, mm.</summary>
    public const int LaneHalfMm = 1500;

    /// <summary>Minimum distance from a prop's footprint to any door opening, door frame or gate, mm.</summary>
    public const int DoorClearMm = 1500;

    /// <summary>Minimum distance along its wall from a decor piece's edge to any opening on that wall, mm.</summary>
    public const int DecorDoorClearMm = 1000;

    /// <summary>Most lights one themed room may carry.</summary>
    public const int MaxLightsPerRoom = 3;

    /// <summary>Every rule violation in <paramref name="theme"/> against <paramref name="site"/>; empty = valid.</summary>
    /// <param name="theme">The parsed theme.</param>
    /// <param name="site">Layout, route, spawns and doors.</param>
    /// <returns>One message per violation, naming the room and the row.</returns>
    public static IReadOnlyList<string> Violations(ThemeDef theme, ThemeSite site)
    {
        ArgumentNullException.ThrowIfNull(theme);
        ArgumentNullException.ThrowIfNull(site);
        var errors = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var room in theme.Rooms)
        {
            var box = site.Boxes.FirstOrDefault(b => b.Id == room.Id);
            if (box is null || !box.IsRoom)
            {
                errors.Add($"room \"{room.Id}\" is not a layout room");
                continue;
            }
            if (!seen.Add(room.Id))
            {
                errors.Add($"room \"{room.Id}\" is themed twice");
            }
            for (var i = 0; i < room.Props.Count; i++)
            {
                errors.AddRange(PropViolations(box, room.Props[i], site).Select(e => $"{room.Id}.props[{i}] {e}"));
            }
            var decor = room.Decor ?? new ValueList<ThemeDecor>();
            for (var i = 0; i < decor.Count; i++)
            {
                errors.AddRange(DecorViolations(box, decor[i], site).Select(e => $"{room.Id}.decor[{i}] {e}"));
            }
            var cracked = room.Cracked ?? new ValueList<ThemeWallPoint>();
            for (var i = 0; i < cracked.Count; i++)
            {
                if (!OnSolidWall(box, cracked[i].Side, cracked[i].Along, cracked[i].Along, 0, site))
                {
                    errors.Add($"{room.Id}.cracked[{i}] {cracked[i].Side} at {cracked[i].Along} is not on a solid wall of the room");
                }
            }
            if (room.Lights.Count < 1 || room.Lights.Count > MaxLightsPerRoom)
            {
                errors.Add($"{room.Id} has {room.Lights.Count} lights (1..{MaxLightsPerRoom})");
            }
            for (var i = 0; i < room.Lights.Count; i++)
            {
                var l = room.Lights[i];
                if (l.X < box.MinX || l.X > box.MaxX || l.Z < box.MinZ || l.Z > box.MaxZ)
                {
                    errors.Add($"{room.Id}.lights[{i}] ({l.X},{l.Z}) is outside the room");
                }
                if (l.YMm < 500 || l.YMm > 8000 || l.RangeMm < 1000 || l.RangeMm > 16000)
                {
                    errors.Add($"{room.Id}.lights[{i}] height {l.YMm} or range {l.RangeMm} out of bounds (500..8000, 1000..16000)");
                }
                if (l.Color is not null && (l.Color.Length != 6 || !l.Color.All(Uri.IsHexDigit)))
                {
                    errors.Add($"{room.Id}.lights[{i}] colour \"{l.Color}\" is not RRGGBB");
                }
            }
        }
        return errors;
    }

    /// <summary>Every violation of one floor prop in <paramref name="room"/>.</summary>
    /// <param name="room">The prop's room box.</param>
    /// <param name="p">The prop row.</param>
    /// <param name="site">Layout, route, spawns and doors.</param>
    /// <returns>Violations; empty = the prop is placed legally.</returns>
    public static IReadOnlyList<string> PropViolations(PlanBox room, ThemeProp p, ThemeSite site)
    {
        ArgumentNullException.ThrowIfNull(room);
        ArgumentNullException.ThrowIfNull(p);
        ArgumentNullException.ThrowIfNull(site);
        var at = $"{p.Piece} ({p.X},{p.Z})";
        if (!DungeonPlan.Nominal.ContainsKey(p.Piece))
        {
            return new[] { $"unknown kit piece \"{p.Piece}\"" };
        }
        if (p.Turns is < 0 or > 3 || p.Pct is < 10 or > 300)
        {
            return new[] { $"{at} turns {p.Turns} or pct {p.Pct} out of bounds (0..3, 10..300)" };
        }
        var errors = new List<string>();
        var f = DungeonPlan.SolidOf(DungeonPlan.PropPiece(room.Id, p.Piece, p.X, p.Z, p.Turns, p.Pct));
        if (f.MinX < room.MinX || f.MaxX > room.MaxX || f.MinZ < room.MinZ || f.MaxZ > room.MaxZ)
        {
            errors.Add($"{at} footprint x {f.MinX}..{f.MaxX} z {f.MinZ}..{f.MaxZ} crosses its room's wall");
        }
        for (var i = 0; i + 1 < site.Lane.Count; i++)
        {
            var (a, b) = (site.Lane[i], site.Lane[i + 1]);
            var lane = new WallBox(room.Id, Math.Min(a.X, b.X) - LaneHalfMm, Math.Max(a.X, b.X) + LaneHalfMm,
                Math.Min(a.Z, b.Z) - LaneHalfMm, Math.Max(a.Z, b.Z) + LaneHalfMm, 0, 0);
            if (f.MinX < lane.MaxX && lane.MinX < f.MaxX && f.MinZ < lane.MaxZ && lane.MinZ < f.MaxZ)
            {
                errors.Add($"{at} is in the centre lane of route ({a.X},{a.Z})-({b.X},{b.Z})");
                break;
            }
        }
        foreach (var s in site.Spawns.Distinct())
        {
            if (Dist2(f, s.X, s.X, s.Z, s.Z) < (long)DungeonPlan.PropClearMm * DungeonPlan.PropClearMm)
            {
                errors.Add($"{at} is within {DungeonPlan.PropClearMm} mm of spawn ({s.X},{s.Z})");
            }
        }
        foreach (var d in site.Doors)
        {
            if (Dist2(f, d.MinX, d.MaxX, d.MinZ, d.MaxZ) < (long)DoorClearMm * DoorClearMm)
            {
                errors.Add($"{at} is within {DoorClearMm} mm of a door or gate at x {d.MinX}..{d.MaxX} z {d.MinZ}..{d.MaxZ}");
                break;
            }
        }
        return errors;
    }

    /// <summary>The span along its wall a decor piece covers (its scaled width), mm.</summary>
    /// <param name="piece">Decor stem.</param>
    /// <returns>Half the width.</returns>
    public static int DecorHalfWidth(string piece) => DungeonPlan.Nominal[piece].X * DungeonPlan.KitScale / 100 / 2;

    private static IEnumerable<string> DecorViolations(PlanBox room, ThemeDecor d, ThemeSite site)
    {
        if (!DungeonPlan.Nominal.ContainsKey(d.Piece))
        {
            yield return $"unknown kit piece \"{d.Piece}\"";
            yield break;
        }
        if (d.Piece != "torch_mounted" && !d.Piece.StartsWith("banner_", StringComparison.Ordinal))
        {
            yield return $"{d.Piece} is not wall decor (torch_mounted or banner_*)";
            yield break;
        }
        var half = DecorHalfWidth(d.Piece);
        if (!OnSolidWall(room, d.Side, d.Along - half, d.Along + half, DecorDoorClearMm, site))
        {
            yield return $"{d.Piece} {d.Side} at {d.Along} is not on a solid wall {DecorDoorClearMm} mm clear of every opening";
        }
    }

    // [lo, hi] along the room's side lies inside one solid wall run and at least clear mm from every opening on that side.
    private static bool OnSolidWall(PlanBox room, WallSide side, int lo, int hi, int clear, ThemeSite site)
    {
        var onRun = site.Walls.Any(w => w.BoxId == room.Id && w.Side == side && lo >= w.Lo && hi <= w.Hi);
        var nearOpening = site.Openings.Any(o => o.BoxId == room.Id && o.Side == side && lo < o.Hi + clear && hi > o.Lo - clear);
        return onRun && !nearOpening;
    }

    private static long Dist2(WallBox f, int minX, int maxX, int minZ, int maxZ)
    {
        long dx = Math.Max(0, Math.Max(minX - f.MaxX, f.MinX - maxX));
        long dz = Math.Max(0, Math.Max(minZ - f.MaxZ, f.MinZ - maxZ));
        return dx * dx + dz * dz;
    }
}
