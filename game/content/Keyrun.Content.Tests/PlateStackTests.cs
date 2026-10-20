using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// STACKED-PLATES (Matt 2026-10-20, ledger f-e933a2b7: "enemy nameplates stack vertically with interruptible cast bars,
/// hover to interrupt like WoW"; BIBLE s3, cast bars are the enemy's language): <see cref="PlateStack"/> lays out one
/// block per plate (the name row, and under it the enemy's cast bar while it casts), stacked vertically so no two
/// blocks share a pixel, in a stable order (nearest the camera first, lowest id on a tie, whatever order the frame
/// listed them in). A kickable cast is marked by shape and colour, never colour alone.
/// </summary>
public sealed class PlateStackTests
{
    private const int W = 1280;
    private const int H = 720;

    private static PlateSpec Spec(int id, int x, int y, long depth, bool casting = false, int nameWidth = 120) =>
        new(id, new PixelPoint(x, y), nameWidth, depth, casting);

    private static void AssertDisjoint(IReadOnlyList<StackedPlate> plates)
    {
        for (var i = 0; i < plates.Count; i++)
        {
            for (var j = i + 1; j < plates.Count; j++)
            {
                Assert.False(plates[i].Block.Intersects(plates[j].Block),
                    $"plate {plates[i].Id} {plates[i].Block} overlaps plate {plates[j].Id} {plates[j].Block}");
            }
        }
    }

    [Fact]
    public void Two_plates_on_one_spot_stack_vertically_without_overlap()
    {
        var placed = PlateStack.Layout(new[] { Spec(101, 640, 300, 10), Spec(102, 640, 300, 20) }, W, H, new List<PixelRect>());
        Assert.Equal(2, placed.Count);
        AssertDisjoint(placed);
        // Vertical only: both stay centred on the shared anchor's x.
        foreach (var p in placed)
        {
            Assert.Equal(640, p.Block.X + (p.Block.Width / 2));
        }
        Assert.NotEqual(placed[0].Block.Y, placed[1].Block.Y);
    }

    [Fact]
    public void Five_casting_and_idle_plates_in_a_pile_never_overlap()
    {
        var specs = new[]
        {
            Spec(101, 600, 320, 50, casting: true),
            Spec(102, 610, 318, 40),
            Spec(103, 590, 322, 30, casting: true),
            Spec(104, 605, 316, 20, casting: true),
            Spec(105, 600, 320, 10),
        };
        var placed = PlateStack.Layout(specs, W, H, new List<PixelRect>());
        Assert.Equal(5, placed.Count);
        AssertDisjoint(placed);
        foreach (var p in placed)
        {
            Assert.True(p.Block.X >= 0 && p.Block.Right <= W && p.Block.Y >= 0 && p.Block.Bottom <= H, $"{p.Id} {p.Block} off screen");
        }
    }

    [Fact]
    public void Plates_keep_clear_of_hud_obstacles()
    {
        var bar = new PixelRect(0, 0, W, 120);
        var placed = PlateStack.Layout(new[] { Spec(101, 640, 100, 10, casting: true) }, W, H, new[] { bar });
        Assert.False(placed[0].Block.Intersects(bar));
    }

    [Fact]
    public void Order_is_nearest_first_then_lowest_id_whatever_the_input_order()
    {
        var a = Spec(103, 640, 300, 10);
        var b = Spec(101, 640, 300, 20);
        var c = Spec(102, 640, 300, 20);
        var d = Spec(104, 640, 300, 5, casting: true);
        var one = PlateStack.Layout(new[] { a, b, c, d }, W, H, new List<PixelRect>());
        var two = PlateStack.Layout(new[] { c, d, b, a }, W, H, new List<PixelRect>());
        Assert.Equal(new[] { 104, 103, 101, 102 }, one.Select(p => p.Id).ToArray());
        Assert.Equal(one, two);
    }

    [Fact]
    public void The_nearest_plate_keeps_its_spot_and_its_name_sits_on_the_anchor()
    {
        var placed = PlateStack.Layout(new[] { Spec(101, 640, 300, 20), Spec(102, 640, 300, 10, casting: true) }, W, H, new List<PixelRect>());
        var nearest = placed[0];
        Assert.Equal(102, nearest.Id);
        Assert.Equal(300, nearest.Name.Bottom);
        Assert.Equal(nearest.Block.Y, nearest.Name.Y);
    }

    [Fact]
    public void A_casting_plate_carries_its_cast_bar_and_mark_under_the_name_inside_its_block()
    {
        var p = PlateStack.Layout(new[] { Spec(101, 640, 300, 10, casting: true) }, W, H, new List<PixelRect>())[0];
        Assert.NotNull(p.CastBar);
        Assert.NotNull(p.Mark);
        var bar = p.CastBar!.Value;
        var mark = p.Mark!.Value;
        Assert.True(bar.Y >= p.Name.Bottom, "the cast bar hangs under the name");
        Assert.True(Contains(p.Block, bar) && Contains(p.Block, mark) && Contains(p.Block, p.Name));
        Assert.False(mark.Intersects(bar), "the mark sits beside the bar, not on it");
        Assert.True(mark.Right <= bar.X, "the mark is left of the bar");
        Assert.Equal(p.Block.Bottom, bar.Bottom);
    }

    [Fact]
    public void An_idle_plate_is_just_its_name()
    {
        var p = PlateStack.Layout(new[] { Spec(101, 640, 300, 10) }, W, H, new List<PixelRect>())[0];
        Assert.Null(p.CastBar);
        Assert.Null(p.Mark);
        Assert.Equal(p.Name, p.Block);
    }

    [Fact]
    public void A_kickable_cast_is_marked_by_shape_not_colour_alone()
    {
        Assert.Equal(CastMark.Kick, PlateStack.MarkOf(interruptible: true));
        Assert.Equal(CastMark.Locked, PlateStack.MarkOf(interruptible: false));
        var box = new PixelRect(100, 200, 12, 12);
        var kick = PlateStack.MarkShape(CastMark.Kick, box);
        var locked = PlateStack.MarkShape(CastMark.Locked, box);
        // Kick: a diamond through the box's edge midpoints. Locked: the box's square corners.
        Assert.Equal(new[] { new PixelPoint(106, 200), new PixelPoint(112, 206), new PixelPoint(106, 212), new PixelPoint(100, 206) }, kick);
        Assert.Equal(new[] { new PixelPoint(100, 200), new PixelPoint(112, 200), new PixelPoint(112, 212), new PixelPoint(100, 212) }, locked);
        Assert.Empty(kick.Intersect(locked));
    }

    [Fact]
    public void A_casting_enemy_is_never_folded_into_a_same_name_crowd()
    {
        Assert.Equal("Paired Warrior", PlateStack.FoldKey("Paired Warrior", 101, casting: false));
        Assert.Equal(PlateStack.FoldKey("Paired Warrior", 102, casting: false), PlateStack.FoldKey("Paired Warrior", 103, casting: false));
        Assert.NotEqual(PlateStack.FoldKey("Paired Warrior", 102, casting: true), PlateStack.FoldKey("Paired Warrior", 103, casting: false));
        Assert.NotEqual(PlateStack.FoldKey("Paired Warrior", 102, casting: true), PlateStack.FoldKey("Paired Warrior", 103, casting: true));
    }

    private static bool Contains(PixelRect outer, PixelRect inner) =>
        inner.X >= outer.X && inner.Y >= outer.Y && inner.Right <= outer.Right && inner.Bottom <= outer.Bottom;
}
