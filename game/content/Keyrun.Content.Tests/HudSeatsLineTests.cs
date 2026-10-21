using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// Finding f-3c1f4542: the queue readout ("Seats filled 3 of 3.") was drawn over ability slots 5 and Q. Ruling: group
/// information lives with the party frames, never over the action bar, so in a live run the readout's rectangle shares
/// no pixel with any ability slot rectangle (<see cref="ActionBarLayout"/>, the rects src/Hud/AbilityBar.cs draws).
/// </summary>
public sealed class HudSeatsLineTests
{
    [Theory]
    [InlineData(1280, 720)]
    [InlineData(1920, 1080)]
    public void Seats_line_never_intersects_an_ability_slot(int width, int height)
    {
        for (var seats = 1; seats <= 5; seats++)
        {
            var readout = ActionBarLayout.QueueReadout(width, height, seats);
            Assert.True(readout.Width > 0 && readout.Height > 0, $"readout {readout} is empty at {width}x{height}");
            for (var count = 1; count <= 10; count++)
            {
                var slots = ActionBarLayout.SlotRects(width, height, count);
                Assert.Equal(count, slots.Count);
                for (var i = 0; i < slots.Count; i++)
                {
                    Assert.False(
                        readout.Intersects(slots[i]),
                        $"seats line {readout} intersects ability slot {i} {slots[i]} at {width}x{height} (seats={seats}, slots={count})");
                }
            }
        }
    }

    [Theory]
    [InlineData(1280, 720)]
    [InlineData(1920, 1080)]
    public void Seats_line_stays_inside_the_viewport_and_off_the_bar_meters(int width, int height)
    {
        for (var seats = 1; seats <= 5; seats++)
        {
            var readout = ActionBarLayout.QueueReadout(width, height, seats);
            Assert.True(readout.X >= 0 && readout.Y >= 0 && readout.Right <= width && readout.Bottom <= height, $"readout {readout} leaves {width}x{height}");
            for (var count = 1; count <= 10; count++)
            {
                var bar = ActionBarLayout.Footprint(width, height, count);
                Assert.False(readout.Intersects(bar), $"seats line {readout} intersects the bar footprint {bar} at {width}x{height}");
            }
        }
    }
}
