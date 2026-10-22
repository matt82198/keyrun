using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// f-1bf2dace (capture kf-main-0e748ca1 t=62 s): the timer text "+2  3:42 / par 12:00" was drawn right-aligned inside the
/// Slack bar, over the blue fill and across the white +3/+2 tier marks of the timer strip, so it read "+2 3:42 /,par 12:00".
/// HudLayout.SlackSplit gives the timer text its own box at the right end of the Slack slot; the fill, the strip and its
/// marks keep to the bar left of it, so no mark or fill edge can cross the timer text.
/// </summary>
public sealed class HudTimerSplitTests
{
    // Measured widths are font-dependent; these bracket the fallback font at 14 px for "+2  0:00 / par 12:00" up to
    // "+20  00:00 / par 12:00".
    [Theory(DisplayName = "f-1bf2dace: the timer box and the Slack bar never overlap and both stay in the slot")]
    [InlineData(1280, 720, 130)]
    [InlineData(1280, 720, 170)]
    [InlineData(1920, 1080, 130)]
    [InlineData(1920, 1080, 170)]
    public void Timer_box_is_clear_of_the_bar(int w, int h, int textWidth)
    {
        var slot = HudLayout.TopBar(w, h).Slack;
        var parts = HudLayout.SlackSplit(slot, textWidth);
        Assert.False(parts.Bar.Intersects(parts.Timer), $"bar {parts.Bar} overlaps timer {parts.Timer}");
        Assert.True(parts.Bar.Right + HudLayout.TimerGap <= parts.Timer.X, "no gap between the bar and the timer box");
        Assert.Equal(slot.X, parts.Bar.X);
        Assert.Equal(slot.Right, parts.Timer.Right);
        Assert.Equal(slot.Y, parts.Bar.Y);
        Assert.Equal(slot.Y, parts.Timer.Y);
        Assert.Equal(slot.Height, parts.Bar.Height);
        Assert.Equal(slot.Height, parts.Timer.Height);
        Assert.True(parts.Timer.Width >= textWidth + (2 * HudLayout.TimerPad), $"timer box {parts.Timer.Width} cannot hold text {textWidth}");
        Assert.True(parts.Bar.Width >= HudLayout.MinSlackFill, $"bar {parts.Bar.Width} too narrow for the deaths label");
    }

    [Fact(DisplayName = "f-1bf2dace: a tier mark anywhere on the strip (0..1 of the bar) lands left of the timer box")]
    public void Strip_marks_never_reach_the_timer_text()
    {
        var parts = HudLayout.SlackSplit(HudLayout.TopBar(1280, 720).Slack, 150);
        foreach (var fraction in new[] { 0.0, 0.6, 0.8, 1.0 })
        {
            var x = parts.Bar.X + (parts.Bar.Width * fraction);
            Assert.True(x < parts.Timer.X, $"mark at {x} crosses the timer box from {parts.Timer.X}");
        }
    }

    [Fact(DisplayName = "f-1bf2dace: on a slot too narrow for both the bar keeps its minimum and the box never leaves the slot")]
    public void Narrow_slot_keeps_the_bar()
    {
        var slot = new PixelRect(10, 60, 200, 18);
        var parts = HudLayout.SlackSplit(slot, 170);
        Assert.Equal(HudLayout.MinSlackFill, parts.Bar.Width);
        Assert.False(parts.Bar.Intersects(parts.Timer));
        Assert.Equal(slot.Right, parts.Timer.Right);
        Assert.True(parts.Timer.X >= slot.X);
    }
}
