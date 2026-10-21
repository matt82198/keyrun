namespace Keyrun.Content;

/// <summary>
/// Render-only colours (game/content/data/palette.json, logical path <see cref="ContentLoader.PaletteFile"/>; C#-only, read only
/// by the view through <see cref="PaletteLoader"/>, never by the sim). Holds the floor colours the grey box draws and the
/// telegraph fill, outline and flash for each telegraph shape. design/BIBLE.md section 3 ("Telegraph contrast and outline")
/// is pinned against these values by TelegraphPaletteTests.
/// </summary>
public sealed record PaletteDef
{
    /// <summary>The floor colours (grey box slabs).</summary>
    public required PaletteFloorsDef Floors { get; init; }

    /// <summary>The telegraph colours, one set per shape.</summary>
    public required PaletteTelegraphDef Telegraph { get; init; }
}

/// <summary>The two floor colours the grey box draws: a room floor and a corridor floor.</summary>
public sealed record PaletteFloorsDef
{
    /// <summary>The room floor colour (GreyBox RoomFloor).</summary>
    public required PaletteColor Room { get; init; }

    /// <summary>The corridor floor colour (GreyBox CorridorFloor).</summary>
    public required PaletteColor Corridor { get; init; }
}

/// <summary>The telegraph colours of each shape the view draws.</summary>
public sealed record PaletteTelegraphDef
{
    /// <summary>A circle: the full-turn fan (TelegraphFan.Span1024 of "circle").</summary>
    public required PaletteShapeDef Circle { get; init; }

    /// <summary>A cone: the arc fan.</summary>
    public required PaletteShapeDef Cone { get; init; }

    /// <summary>A line: the rectangle from the caster.</summary>
    public required PaletteShapeDef Line { get; init; }
}

/// <summary>One telegraph shape's colours: the growing fill, the outline and the hit flash.</summary>
public sealed record PaletteShapeDef
{
    /// <summary>The growing fill.</summary>
    public required PaletteColor Fill { get; init; }

    /// <summary>The outline drawn under the fill.</summary>
    public required PaletteColor Outline { get; init; }

    /// <summary>The colour the fill flashes to when the telegraph lands.</summary>
    public required PaletteColor Flash { get; init; }
}

/// <summary>
/// An RGBA colour in integers, so the content assembly stays free of floating point (ContentPurityTests): each colour
/// channel 0..255, alpha a percentage 0..100 (opaque when the row omits it). The view divides by 255 and 100 for Godot's Color.
/// </summary>
public sealed record PaletteColor
{
    /// <summary>Red, 0..255.</summary>
    public required int R { get; init; }

    /// <summary>Green, 0..255.</summary>
    public required int G { get; init; }

    /// <summary>Blue, 0..255.</summary>
    public required int B { get; init; }

    /// <summary>Alpha as a percentage, 0..100; 100 (opaque) when the row omits it.</summary>
    public int A { get; init; } = 100;
}
