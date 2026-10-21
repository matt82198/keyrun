using System;
using System.Collections.Generic;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// The telegraph contrast rule pinned against the render palette (design/BIBLE.md section 3, "Telegraph contrast and
/// outline": every floor telegraph has a solid, high-contrast outline, its fill is checked against the room's floor so it
/// never sinks into the background, and each shape keeps its own edge). The palette is read through
/// <see cref="PaletteLoader"/>, the same path the view uses. The WCAG 2.x relative luminance and the alpha compositing are
/// written out in this file, not taken from production code, so the rule is checked independently of the loader.
/// Both floors are checked: RoomFloor and CorridorFloor (game/src/View/GreyBox.cs).
/// </summary>
public sealed class TelegraphPaletteTests
{
    private const double MinContrast = 3.0;

    // Floors are opaque; a fill or outline is composited over the floor it sits on.
    private static double Linear(double c) => c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);

    private static double Luminance((double R, double G, double B) rgb) =>
        0.2126 * Linear(rgb.R) + 0.7152 * Linear(rgb.G) + 0.0722 * Linear(rgb.B);

    private static double Ratio((double R, double G, double B) a, (double R, double G, double B) b)
    {
        var la = Luminance(a);
        var lb = Luminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    // The palette stores 0..255 channels and a 0..100 alpha percentage; the maths here works on 0..1.
    private static (double R, double G, double B) Floor(PaletteColor c) => (c.R / 255.0, c.G / 255.0, c.B / 255.0);

    private static (double R, double G, double B) Over(PaletteColor src, (double R, double G, double B) under)
    {
        var a = src.A / 100.0;
        return (src.R / 255.0 * a + under.R * (1 - a), src.G / 255.0 * a + under.G * (1 - a), src.B / 255.0 * a + under.B * (1 - a));
    }

    private static PaletteShapeDef ShapeOf(PaletteDef palette, string shape) => shape switch
    {
        "circle" => palette.Telegraph.Circle,
        "cone" => palette.Telegraph.Cone,
        "line" => palette.Telegraph.Line,
        _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, "not a telegraph shape"),
    };

    private static readonly string[] Shapes = { "circle", "cone", "line" };

    [Fact]
    public void Every_shape_fill_clears_3_to_1_over_both_floors()
    {
        var palette = PaletteLoader.Load();
        var floors = new Dictionary<string, PaletteColor>
        {
            ["room"] = palette.Floors.Room,
            ["corridor"] = palette.Floors.Corridor,
        };
        var failures = new List<string>();
        foreach (var shape in Shapes)
        {
            foreach (var (floorName, floor) in floors)
            {
                var under = Floor(floor);
                var ratio = Ratio(Over(ShapeOf(palette, shape).Fill, under), under);
                if (ratio < MinContrast)
                {
                    failures.Add($"{shape} fill over {floorName} floor is {ratio:F4}:1 (need {MinContrast}:1)");
                }
            }
        }
        Assert.Empty(failures);
    }

    [Fact]
    public void Every_shape_outline_is_distinct_from_its_fill_and_clears_3_to_1_against_it()
    {
        var palette = PaletteLoader.Load();
        var under = Floor(palette.Floors.Room);
        var failures = new List<string>();
        foreach (var shape in Shapes)
        {
            var s = ShapeOf(palette, shape);
            var fill = Over(s.Fill, under);
            var outline = Over(s.Outline, under);
            var ratio = Ratio(outline, fill);
            if (outline == fill)
            {
                failures.Add($"{shape} outline is the same colour as its fill");
            }
            if (ratio < MinContrast)
            {
                failures.Add($"{shape} outline over fill is {ratio:F4}:1 (need {MinContrast}:1)");
            }
        }
        Assert.Empty(failures);
    }

    [Fact]
    public void No_two_shapes_share_both_fill_and_outline()
    {
        var palette = PaletteLoader.Load();
        var collisions = new List<string>();
        for (var i = 0; i < Shapes.Length; i++)
        {
            for (var j = i + 1; j < Shapes.Length; j++)
            {
                var a = ShapeOf(palette, Shapes[i]);
                var b = ShapeOf(palette, Shapes[j]);
                if (a.Fill == b.Fill && a.Outline == b.Outline)
                {
                    collisions.Add($"{Shapes[i]} and {Shapes[j]} share fill and outline");
                }
            }
        }
        Assert.Empty(collisions);
    }
}
