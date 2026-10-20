using System.Collections.Generic;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// Lane DMG-NUMBERS (finding f-07a02dc5): the human's floating damage numbers ("80", "100", "180") and MISS text spawned
/// over the target's head, on the spot where its nameplate and its over-head cast bar label draw, so both read as one
/// tangle (frames 02 and 04 of the bytick-42d5 visual audit). <see cref="CombatTextRule.PlaceNumber"/> puts a number
/// beside the target's drawn plate, drifting sideways and down away from it, and keeps it clear of every plate, cast
/// bar and HUD block. Each scene below is laid out the way the view lays it out: cast bars first
/// (<see cref="HudLayout.CastBars"/>), then the plates separated from them (<see cref="HudLayout.Separate(IReadOnlyList{PixelRect}, int, int, IReadOnlyList{PixelRect})"/>,
/// as LabelView does), then the number. Over its whole life the number's box shares no pixel with the target's plate,
/// any other plate, or any cast bar block.
/// </summary>
public sealed class CombatTextPlacementTests
{
    private const int Width = 1280;
    private const int Height = 720;

    // A three-digit number at 18 px with a 4 px outline (CombatText: FontSize 18, OutlineSize 4), and "MISS".
    private const int NumberW = 38;
    private const int MissW = 54;
    private const int NumberH = 26;

    private static readonly float[] Ages = { 0f, 0.1f, 0.25f, 0.5f, 0.75f, 0.9f, 1f };

    [Fact]
    public void A_number_on_a_casting_boss_clears_its_plate_and_its_cast_bar()
    {
        // Frame 04: The Seed casts Seed Lash; its bar sits over the head, "> The Seed <" just under it.
        var scene = Scene.Lay(
            casters: new[] { new PixelPoint(632, 262) },
            plates: new[] { Plate(632, 312, 96) });
        AssertClearForLife(scene, target: 0, NumberW);
    }

    [Fact]
    public void A_number_on_a_pack_clears_the_neighbour_s_plate_too()
    {
        // Frame 02: "> Choir Acolyte <" (targeted), "Choir Cantor" stacked over it, a second "Choir Acolyte" under it.
        var scene = Scene.Lay(
            casters: new PixelPoint[0],
            plates: new[] { Plate(676, 300, 124), Plate(622, 276, 96), Plate(684, 326, 104) });
        AssertClearForLife(scene, target: 0, NumberW);
    }

    [Fact]
    public void A_number_on_a_caster_in_a_pack_clears_every_plate_and_bar()
    {
        var scene = Scene.Lay(
            casters: new[] { new PixelPoint(676, 250), new PixelPoint(560, 262) },
            plates: new[] { Plate(676, 300, 124), Plate(560, 306, 96), Plate(760, 304, 104) });
        AssertClearForLife(scene, target: 0, NumberW);
        AssertClearForLife(scene, target: 1, NumberW);
        AssertClearForLife(scene, target: 2, NumberW);
    }

    [Fact]
    public void MISS_text_follows_the_same_rule()
    {
        var scene = Scene.Lay(
            casters: new[] { new PixelPoint(632, 262) },
            plates: new[] { Plate(632, 312, 96) });
        AssertClearForLife(scene, target: 0, MissW);
    }

    [Fact]
    public void A_target_at_the_right_edge_puts_its_number_on_the_left_side()
    {
        var scene = Scene.Lay(
            casters: new[] { new PixelPoint(1220, 262) },
            plates: new[] { Plate(1220, 312, 96) });
        AssertClearForLife(scene, target: 0, NumberW);
        var placed = Place(scene, 0, NumberW, 0.5f);
        Assert.True(placed.Right <= scene.Plates[0].X, $"number {placed} should sit left of plate {scene.Plates[0]}");
    }

    [Fact]
    public void The_number_stays_beside_its_target_and_drifts_away_from_the_plate()
    {
        var scene = Scene.Lay(
            casters: new[] { new PixelPoint(632, 262) },
            plates: new[] { Plate(632, 312, 96) });
        var plate = scene.Plates[0];
        var start = Place(scene, 0, NumberW, 0f);
        var end = Place(scene, 0, NumberW, 1f);

        // Beside the plate: level with it and within a short reach of its side, not flung across the screen.
        Assert.True(start.X >= plate.Right, $"start {start} should be right of plate {plate}");
        Assert.True(start.X - plate.Right <= 24, $"start {start} is too far from plate {plate}");
        Assert.True(start.Bottom > plate.Y && start.Y < plate.Bottom + NumberH, $"start {start} is not level with plate {plate}");

        // Drifting away: further from the plate's side and lower at the end of its life than at the start.
        Assert.True(end.X > start.X, $"end {end} should drift right of start {start}");
        Assert.True(end.Y > start.Y, $"end {end} should drift down from start {start}");
    }

    [Fact]
    public void Numbers_already_placed_this_frame_are_kept_clear_of()
    {
        var scene = Scene.Lay(
            casters: new PixelPoint[0],
            plates: new[] { Plate(632, 312, 96) });
        var first = Place(scene, 0, NumberW, 0.2f);
        var avoid = new List<PixelRect>(scene.Avoid) { first };
        var second = CombatTextRule.PlaceNumber(scene.Plates[0], NumberW, NumberH, 0.2f, Width, Height, avoid);
        Assert.NotNull(second);
        Assert.False(second!.Value.Intersects(first), $"second {second} overlaps first {first}");
        Assert.False(second.Value.Intersects(scene.Plates[0]), $"second {second} overlaps the plate {scene.Plates[0]}");
    }

    [Fact]
    public void A_number_with_no_clear_spot_is_not_drawn()
    {
        // Every pixel is covered: the number is withheld rather than drawn over a plate or a bar.
        var plate = new PixelRect(600, 300, 80, 23);
        var wall = new[] { plate, new PixelRect(0, 0, Width, Height) };
        Assert.Null(CombatTextRule.PlaceNumber(plate, NumberW, NumberH, 0.5f, Width, Height, wall));
    }

    private static void AssertClearForLife(Scene scene, int target, int width)
    {
        foreach (var age in Ages)
        {
            var placed = Place(scene, target, width, age);
            Assert.True(placed.X >= 0 && placed.Y >= 0 && placed.Right <= Width && placed.Bottom <= Height, $"age {age}: {placed} leaves the viewport");
            for (var i = 0; i < scene.Plates.Count; i++)
            {
                Assert.False(placed.Intersects(scene.Plates[i]), $"age {age}: number {placed} overlaps plate {i} {scene.Plates[i]}");
            }
            foreach (var bar in scene.Bars)
            {
                Assert.False(placed.Intersects(bar), $"age {age}: number {placed} overlaps cast bar {bar}");
            }
        }
    }

    private static PixelRect Place(Scene scene, int target, int width, float age)
    {
        var placed = CombatTextRule.PlaceNumber(scene.Plates[target], width, NumberH, age, Width, Height, scene.Avoid);
        Assert.NotNull(placed);
        return placed!.Value;
    }

    // A plate box exactly as LabelView builds it before separation: the name plus 4 px padding (15 px font), centred on
    // the projected plate anchor with its bottom edge on it.
    private static PixelRect Plate(int anchorX, int anchorY, int nameWidth)
    {
        var w = nameWidth + 8;
        var h = 15 + 8;
        return new PixelRect(anchorX - (w / 2), anchorY - h, w, h);
    }

    private sealed record Scene(IReadOnlyList<PixelRect> Plates, IReadOnlyList<PixelRect> Bars, IReadOnlyList<PixelRect> Avoid)
    {
        public static Scene Lay(IReadOnlyList<PixelPoint> casters, IReadOnlyList<PixelRect> plates)
        {
            var band = HudLayout.TopBar(Width, Height).Band(Width);
            var bars = HudLayout.CastBars(casters, Width, Height, new[] { band });
            var obstacles = new List<PixelRect> { band };
            obstacles.AddRange(bars);
            var offsets = HudLayout.Separate(plates, Width, Height, obstacles);
            var drawn = new List<PixelRect>();
            for (var i = 0; i < plates.Count; i++)
            {
                drawn.Add(plates[i].Offset(offsets[i].Dx, offsets[i].Dy));
            }
            var avoid = new List<PixelRect>(obstacles);
            avoid.AddRange(drawn);
            return new Scene(drawn, bars, avoid);
        }
    }
}
