using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// The ability tooltip's pixel rules (<see cref="AbilityTooltipLayout"/>): the hold threshold, the word wrap, and the box
/// above the bar, inside the viewport and clear of the party frames (Hud.tscn PartyFrames, 16..256 x 124..480) and the
/// top bar band, for every slot of the ten-slot bar at the project's 1280x720 and at a narrow 960x540.
/// </summary>
public sealed class AbilityTooltipLayoutTests
{
    private static readonly PixelRect Party = new(16, 124, 240, 356);

    private static List<PixelRect> Avoid(int vw, int vh)
    {
        var top = HudLayout.TopBar(vw, vh);
        var band = top.Band(vw);
        return new List<PixelRect> { Party, band with { Height = System.Math.Max(band.Bottom, HudLayout.ForcesBar(top).Bottom) } };
    }

    [Theory(DisplayName = "every slot's tooltip sits above the bar, inside the viewport, off the party frames and the top bar")]
    [InlineData(1280, 720, 7)]
    [InlineData(1280, 720, 12)]
    [InlineData(960, 540, 7)]
    public void Box_fits_for_every_slot(int vw, int vh, int bodyLines)
    {
        var rects = ActionBarLayout.SlotRects(vw, vh, 10);
        var bar = ActionBarLayout.Footprint(vw, vh, 10);
        var height = AbilityTooltipLayout.Height(bodyLines);
        for (var i = 0; i < 9; i++)
        {
            var box = AbilityTooltipLayout.Place(vw, vh, rects[i], bar, height, Avoid(vw, vh));
            Assert.True(AbilityTooltipLayout.Fits(box, vw, vh, Avoid(vw, vh)), $"slot {i + 1} at {vw}x{vh}: {box} does not fit");
            Assert.True(box.Bottom <= bar.Y - AbilityTooltipLayout.ErrorBand, $"slot {i + 1}: {box} covers the bar or its error line");
            Assert.False(box.Intersects(Party), $"slot {i + 1}: {box} covers the party frames");
        }
    }

    [Fact(DisplayName = "without the party frames to avoid, the box centres over its slot")]
    public void Box_centres_over_its_slot()
    {
        var rects = ActionBarLayout.SlotRects(1280, 720, 10);
        var bar = ActionBarLayout.Footprint(1280, 720, 10);
        var box = AbilityTooltipLayout.Place(1280, 720, rects[4], bar, 100, new List<PixelRect>());
        Assert.Equal(rects[4].X + (rects[4].Width / 2), box.X + (box.Width / 2));
        Assert.Equal(bar.Y - AbilityTooltipLayout.ErrorBand - AbilityTooltipLayout.Gap - 100, box.Y);
    }

    [Fact(DisplayName = "a key shows its tooltip only when held longer than 300 ms")]
    public void Hold_threshold_is_300_ms()
    {
        Assert.False(AbilityTooltipLayout.HoldShows(0));
        Assert.False(AbilityTooltipLayout.HoldShows(300));
        Assert.True(AbilityTooltipLayout.HoldShows(301));
        Assert.True(AbilityTooltipLayout.HoldShows(400));
    }

    [Fact(DisplayName = "word wrap keeps every multi-word line within the width")]
    public void Wrap_keeps_lines_within_width()
    {
        static int Measure(string s) => s.Length * 7;
        var lines = AbilityTooltipLayout.Wrap("Deals 80 physical damage, builds 15 Momentum and opens Sundered on the target for 3s.", 140, Measure);
        Assert.True(lines.Count > 1);
        Assert.All(lines, l => Assert.True(Measure(l) <= 140 || !l.Contains(' '), l));
        Assert.Equal("Deals 80 physical damage, builds 15 Momentum and opens Sundered on the target for 3s.", string.Join(" ", lines));
    }
}
