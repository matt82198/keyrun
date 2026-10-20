using System.Collections.Generic;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// STACKED-PLATES hover (Matt 2026-10-20, ledger f-e933a2b7, "hover to interrupt like WoW"): a name plate is a hover
/// target. The live enemy whose drawn plate block is under the cursor is the mouseover unit, ahead of any body under
/// it (the plate draws over the 3D view), so the Q kick from MOUSEOVER goes at that enemy. A friendly or dead plate
/// is not a target and the pick falls through to the bodies; there is no mouseover during mouselook.
/// </summary>
public sealed class PlateHoverTests
{
    private static readonly PixelRect PlateBox = new(600, 200, 150, 51);
    private static readonly PixelRect BodyBox = new(600, 190, 60, 120);

    private static HoverBody Body(int id, PixelRect rect, long depth = 10) => new(id, rect, depth, Hostile: true, Alive: true);

    private static HoverPlate Plate(int id, PixelRect rect, bool hostile = true, bool alive = true) => new(id, rect, hostile, alive);

    [Fact]
    public void Hovering_an_enemy_plate_makes_that_enemy_the_mouseover_unit()
    {
        var plates = new List<HoverPlate> { Plate(103, PlateBox) };
        Assert.Equal(103, MouseoverRule.Unit(new PixelPoint(700, 240), rightHeld: false, new List<HoverBody>(), plates));
        Assert.Null(MouseoverRule.Unit(new PixelPoint(599, 240), rightHeld: false, new List<HoverBody>(), plates));
    }

    [Fact]
    public void A_plate_under_the_cursor_wins_over_a_nearer_body_under_it()
    {
        var bodies = new List<HoverBody> { Body(101, BodyBox, depth: 1) };
        var plates = new List<HoverPlate> { Plate(103, PlateBox) };
        Assert.Equal(103, MouseoverRule.Unit(new PixelPoint(620, 230), rightHeld: false, bodies, plates));
        // Off the plate, the body is still hovered.
        Assert.Equal(101, MouseoverRule.Unit(new PixelPoint(620, 300), rightHeld: false, bodies, plates));
    }

    [Fact]
    public void Friendly_and_dead_plates_fall_through_to_the_body()
    {
        var bodies = new List<HoverBody> { Body(101, BodyBox) };
        var plates = new List<HoverPlate> { Plate(2, PlateBox, hostile: false), Plate(104, PlateBox, alive: false) };
        Assert.Equal(101, MouseoverRule.Unit(new PixelPoint(620, 230), rightHeld: false, bodies, plates));
        Assert.Null(MouseoverRule.Unit(new PixelPoint(700, 230), rightHeld: false, bodies, plates));
    }

    [Fact]
    public void No_plate_mouseover_while_right_mouse_is_held()
    {
        var plates = new List<HoverPlate> { Plate(103, PlateBox) };
        Assert.Null(MouseoverRule.Unit(new PixelPoint(700, 240), rightHeld: true, new List<HoverBody>(), plates));
    }

    [Fact]
    public void Stacked_plates_map_each_row_to_its_own_enemy()
    {
        var placed = PlateStack.Layout(
            new[]
            {
                new PlateSpec(101, new PixelPoint(640, 300), 120, 10, Casting: true),
                new PlateSpec(102, new PixelPoint(640, 300), 120, 20, Casting: true),
                new PlateSpec(103, new PixelPoint(640, 300), 120, 30, Casting: false),
            },
            1280, 720, new List<PixelRect>());
        var plates = new List<HoverPlate>();
        foreach (var p in placed)
        {
            plates.Add(Plate(p.Id, p.Block));
        }
        foreach (var p in placed)
        {
            // The centre of each stacked block (its name, and its cast bar when it casts) hovers that enemy alone.
            var centre = new PixelPoint(p.Block.X + (p.Block.Width / 2), p.Block.Y + (p.Block.Height / 2));
            Assert.Equal(p.Id, MouseoverRule.Unit(centre, rightHeld: false, new List<HoverBody>(), plates));
            if (p.CastBar is { } bar)
            {
                Assert.Equal(p.Id, MouseoverRule.Unit(new PixelPoint(bar.X + 1, bar.Y + 1), rightHeld: false, new List<HoverBody>(), plates));
            }
        }
    }

    [Fact]
    public void Where_plates_still_overlap_the_one_drawn_last_wins()
    {
        var plates = new List<HoverPlate> { Plate(101, PlateBox), Plate(102, PlateBox) };
        Assert.Equal(102, MouseoverRule.Unit(new PixelPoint(700, 240), rightHeld: false, new List<HoverBody>(), plates));
    }
}
