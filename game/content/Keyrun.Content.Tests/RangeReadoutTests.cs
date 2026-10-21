using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// Lane RANGE-READOUT (finding f-682aacb5): an out-of-range slot must read without colour (BIBLE s3, research/06:
/// shape plus colour, never colour alone). The red icon tint stays; on top of it the icon dims and a range glyph
/// draws in the slot's bottom-left corner, and a press that fails for range shows a short "Out of range" error line.
/// The decisions are <see cref="RangeReadout"/>; AbilityBar applies them.
/// </summary>
public sealed class RangeReadoutTests
{
    private const long AbilityRange = 5000;
    private const long Far = 20000;
    private const long Near = 3000;

    [Fact]
    public void Out_of_range_slot_draws_the_range_glyph()
    {
        Assert.True(RangeReadout.Cue(outOfRange: true).Glyph);
    }

    [Fact]
    public void In_range_slot_draws_no_range_glyph()
    {
        Assert.False(RangeReadout.Cue(outOfRange: false).Glyph);
    }

    [Fact]
    public void Out_of_range_icon_dims_and_in_range_icon_does_not()
    {
        var oor = RangeReadout.Cue(outOfRange: true).IconAlphaPct;
        var inRange = RangeReadout.Cue(outOfRange: false).IconAlphaPct;
        Assert.Equal(100, inRange);
        Assert.InRange(oor, 30, 70); // dimmed enough to read as unusable, not so far the glyph loses its icon
    }

    [Fact]
    public void A_press_at_a_living_target_beyond_range_fails_for_range()
    {
        var target = new RangeTarget(Alive: true, Hostile: true, RangeMm: Far);
        Assert.True(RangeReadout.PressFailsForRange(target, targetShape: true, AbilityRange, blockedEarlier: false));
    }

    [Fact]
    public void A_press_in_range_does_not_fail_for_range()
    {
        var target = new RangeTarget(Alive: true, Hostile: true, RangeMm: Near);
        Assert.False(RangeReadout.PressFailsForRange(target, targetShape: true, AbilityRange, blockedEarlier: false));
        var edge = new RangeTarget(Alive: true, Hostile: true, RangeMm: AbilityRange);
        Assert.False(RangeReadout.PressFailsForRange(edge, targetShape: true, AbilityRange, blockedEarlier: false)); // sim: > Range fails, == passes
    }

    [Fact]
    public void A_press_the_sim_rejects_for_another_reason_first_shows_no_range_error()
    {
        // Casting.StartCast checks not_ready and insufficient_resource before range, so those presses never read as range.
        var target = new RangeTarget(Alive: true, Hostile: true, RangeMm: Far);
        Assert.False(RangeReadout.PressFailsForRange(target, targetShape: true, AbilityRange, blockedEarlier: true));
    }

    [Fact]
    public void A_press_with_no_live_target_or_no_target_shape_shows_no_range_error()
    {
        Assert.False(RangeReadout.PressFailsForRange(null, targetShape: true, AbilityRange, blockedEarlier: false));
        var dead = new RangeTarget(Alive: false, Hostile: true, RangeMm: Far);
        Assert.False(RangeReadout.PressFailsForRange(dead, targetShape: true, AbilityRange, blockedEarlier: false));
        var far = new RangeTarget(Alive: true, Hostile: true, RangeMm: Far);
        Assert.False(RangeReadout.PressFailsForRange(far, targetShape: false, AbilityRange, blockedEarlier: false));
    }

    [Fact]
    public void The_error_line_reads_out_of_range_and_fades_after_its_window()
    {
        Assert.Equal("Out of range", RangeReadout.ErrorText);
        const int shownAt = 100;
        Assert.True(RangeReadout.ErrorVisible(shownAt, shownAt));
        Assert.True(RangeReadout.ErrorVisible(shownAt, shownAt + RangeReadout.ErrorTicks - 1));
        Assert.False(RangeReadout.ErrorVisible(shownAt, shownAt + RangeReadout.ErrorTicks)); // transient: no permanent text
        Assert.False(RangeReadout.ErrorVisible(null, shownAt));
        Assert.InRange(RangeReadout.ErrorTicks, 20, 60); // 1 to 3 s at 20 Hz
    }
}
