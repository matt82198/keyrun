using System.Collections.Generic;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// Lane PLATE-RECTS (finding f-9d4a31a5): CombatText laid its own copy of the name plates out (the pre-separation box,
/// separated again) because LabelView was held when DMG-NUMBERS was written. That copy never folds a same-name crowd
/// ("Paired Warrior x5", NAMEPLATE-DECLUTTER), so where five stacked plates draw as one wide plate the copy holds five
/// narrow boxes in a column, and every plate the crowd's separation moves sits somewhere the copy does not think it is.
/// LabelView now publishes the plates it draws (<see cref="DrawnPlate"/>: the rect after folding and separation, and the
/// actors folded into it) and CombatText reads them through <see cref="DrawnPlates"/>. Each scene below is the list
/// LabelView publishes; the number is placed with <see cref="CombatTextRule.PlaceNumber"/> beside the plate
/// <see cref="DrawnPlates.PlateOf"/> returns, avoiding <see cref="DrawnPlates.Rects"/>, and over its whole life it
/// shares no pixel with any drawn plate.
/// </summary>
public sealed class CombatTextPlateRectsTests
{
    private const int Width = 1280;
    private const int Height = 720;
    private const int NumberW = 38;
    private const int NumberH = 26;

    private static readonly float[] Ages = { 0f, 0.25f, 0.5f, 0.75f, 1f };

    [Fact]
    public void A_number_on_a_folded_crowd_member_goes_beside_the_crowd_s_one_plate()
    {
        // Five Paired Warriors on one spot draw one plate, "> Paired Warrior x5 <", at the target's (actor 103's) spot;
        // a Choir Cantor beside them was pushed up by separation. Actor 101 is folded into the crowd: it has no plate of
        // its own, so its number goes beside the crowd's plate.
        var crowd = new DrawnPlate(new PixelRect(560, 300, 176, 23), new[] { 103, 101, 102, 104, 105 });
        var cantor = new DrawnPlate(new PixelRect(600, 274, 104, 23), new[] { 110 });
        var plates = new[] { crowd, cantor };

        Assert.Equal(crowd.Rect, DrawnPlates.PlateOf(plates, 101));
        Assert.Equal(crowd.Rect, DrawnPlates.PlateOf(plates, 103));
        Assert.Equal(cantor.Rect, DrawnPlates.PlateOf(plates, 110));
        AssertClearForLife(plates, 101);
        AssertClearForLife(plates, 103);
        AssertClearForLife(plates, 110);
    }

    [Fact]
    public void A_number_follows_a_plate_where_separation_moved_it()
    {
        // Three plates whose bodies project onto one spot: the drawn rects are the separated ones, a column above and
        // below the nearest. The number for each goes beside its plate where it draws, never beside the shared spot.
        var raw = new[] { new PixelRect(600, 300, 110, 23), new PixelRect(600, 300, 96, 23), new PixelRect(600, 300, 124, 23) };
        var offsets = HudLayout.Separate(raw, Width, Height);
        var plates = new DrawnPlate[raw.Length];
        for (var i = 0; i < raw.Length; i++)
        {
            plates[i] = new DrawnPlate(raw[i].Offset(offsets[i].Dx, offsets[i].Dy), new[] { 200 + i });
        }
        Assert.NotEqual(raw[1], plates[1].Rect);

        for (var i = 0; i < plates.Length; i++)
        {
            Assert.Equal(plates[i].Rect, DrawnPlates.PlateOf(plates, 200 + i));
            AssertClearForLife(plates, 200 + i);
        }
    }

    [Fact]
    public void An_actor_with_no_drawn_plate_has_none()
    {
        var plates = new[] { new DrawnPlate(new PixelRect(560, 300, 176, 23), new[] { 103, 101 }) };

        Assert.Null(DrawnPlates.PlateOf(plates, 999));
        Assert.Null(DrawnPlates.PlateOf(new DrawnPlate[0], 101));
    }

    [Fact]
    public void Rects_lists_every_drawn_plate_once_and_Hits_reports_a_box_on_one()
    {
        var a = new DrawnPlate(new PixelRect(560, 300, 176, 23), new[] { 103, 101 });
        var b = new DrawnPlate(new PixelRect(600, 274, 104, 23), new[] { 110 });
        var plates = new[] { a, b };

        Assert.Equal(new[] { a.Rect, b.Rect }, DrawnPlates.Rects(plates));
        Assert.True(DrawnPlates.Hits(new PixelRect(700, 310, 38, 26), plates));
        Assert.True(DrawnPlates.Hits(new PixelRect(610, 280, 10, 10), plates));
        Assert.False(DrawnPlates.Hits(new PixelRect(800, 400, 38, 26), plates));
    }

    private static void AssertClearForLife(IReadOnlyList<DrawnPlate> plates, int target)
    {
        var beside = DrawnPlates.PlateOf(plates, target);
        Assert.NotNull(beside);
        var avoid = new List<PixelRect>(DrawnPlates.Rects(plates));
        foreach (var age in Ages)
        {
            var placed = CombatTextRule.PlaceNumber(beside!.Value, NumberW, NumberH, age, Width, Height, avoid);
            Assert.NotNull(placed);
            Assert.False(DrawnPlates.Hits(placed!.Value, plates), $"target {target} age {age}: number {placed} sits on a drawn plate");
        }
    }
}
