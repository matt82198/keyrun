using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// The Lock ring rule (<see cref="LockRule"/>, LOCK-VIEW): an open Lock is a violet ring that shrinks from
/// <see cref="LockRule.OpenStartRadiusMm"/> to <see cref="LockRule.OpenEndRadiusMm"/> as the Window runs out; a broken
/// Lock flashes green and a failed one red for <see cref="LockRule.FlashTicks"/> ticks, then the ring goes idle
/// (invisible); an enraged boss keeps a dim red ring under everything else. Integer math only.
/// </summary>
public sealed class LockRuleTests
{
    private const int Window = 60;
    private const int Never = -1;

    private static LockLook Open(int ticksLeft, bool enraged = false) =>
        LockRule.For(true, ticksLeft, Window, 0, 0, enraged, Never, Never);

    [Fact]
    public void Open_at_the_full_window_is_a_violet_ring_of_the_start_radius()
    {
        LockLook look = Open(Window);
        Assert.Equal(LockPhase.Open, look.Phase);
        Assert.Equal(0xC77DFF, look.Color);
        Assert.Equal(4000, look.RadiusMm);
        Assert.True(look.AlphaPermille > 0);
    }

    [Fact]
    public void Open_with_no_ticks_left_has_shrunk_to_the_end_radius()
    {
        LockLook look = Open(0);
        Assert.Equal(LockPhase.Open, look.Phase);
        Assert.Equal(0xC77DFF, look.Color);
        Assert.Equal(1500, look.RadiusMm);
    }

    [Theory]
    [InlineData(60, 4000)]
    [InlineData(30, 2750)]
    [InlineData(15, 2125)]
    [InlineData(0, 1500)]
    [InlineData(90, 4000)]
    [InlineData(-5, 1500)]
    public void Open_radius_shrinks_linearly_with_the_time_left_and_clamps(int ticksLeft, int expectedMm)
    {
        Assert.Equal(expectedMm, Open(ticksLeft).RadiusMm);
    }

    [Fact]
    public void Open_radius_never_grows_as_the_window_runs_out()
    {
        int last = int.MaxValue;
        for (int left = Window; left >= 0; left--)
        {
            int r = Open(left).RadiusMm;
            Assert.True(r <= last, "radius grew at " + left + " ticks left");
            last = r;
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(29)]
    public void Broken_flashes_green_for_the_flash_ticks(int since)
    {
        LockLook look = LockRule.For(false, 0, Window, 1, 0, false, since, Never);
        Assert.Equal(LockPhase.Broken, look.Phase);
        Assert.Equal(0x60E080, look.Color);
        Assert.True(look.AlphaPermille > 0);
    }

    [Fact]
    public void Broken_flash_ends_after_thirty_ticks_and_the_ring_goes_idle()
    {
        Assert.Equal(30, LockRule.FlashTicks);
        LockLook look = LockRule.For(false, 0, Window, 1, 0, false, 30, Never);
        Assert.Equal(LockPhase.Idle, look.Phase);
        Assert.Equal(0, look.AlphaPermille);
    }

    [Fact]
    public void Broken_flash_fades_as_it_ages()
    {
        int first = LockRule.For(false, 0, Window, 1, 0, false, 0, Never).AlphaPermille;
        int late = LockRule.For(false, 0, Window, 1, 0, false, 29, Never).AlphaPermille;
        Assert.True(late < first, "alpha " + late + " not below " + first);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(29)]
    public void Failed_flashes_red_for_the_flash_ticks(int since)
    {
        LockLook look = LockRule.For(false, 0, Window, 0, 1, false, Never, since);
        Assert.Equal(LockPhase.Failed, look.Phase);
        Assert.Equal(0xFF5050, look.Color);
        Assert.True(look.AlphaPermille > 0);
    }

    [Fact]
    public void Failed_flash_ends_and_the_ring_goes_idle()
    {
        LockLook look = LockRule.For(false, 0, Window, 0, 1, false, Never, 30);
        Assert.Equal(LockPhase.Idle, look.Phase);
        Assert.Equal(0, look.AlphaPermille);
    }

    [Fact]
    public void A_flash_needs_its_count_so_a_stale_tick_never_flashes()
    {
        Assert.Equal(LockPhase.Idle, LockRule.For(false, 0, Window, 0, 0, false, 5, Never).Phase);
        Assert.Equal(LockPhase.Idle, LockRule.For(false, 0, Window, 0, 0, false, Never, 5).Phase);
    }

    [Fact]
    public void Idle_is_invisible_and_has_no_enrage_ring()
    {
        LockLook look = LockRule.For(false, 0, Window, 0, 0, false, Never, Never);
        Assert.Equal(LockPhase.Idle, look.Phase);
        Assert.Equal(0, look.AlphaPermille);
        Assert.False(look.EnrageRing);
    }

    [Fact]
    public void Enraged_with_nothing_open_is_a_persistent_dim_red_ring()
    {
        foreach (int since in new[] { Never, 30, 1000 })
        {
            LockLook look = LockRule.For(false, 0, Window, 0, 3, true, Never, since);
            Assert.Equal(LockPhase.Enraged, look.Phase);
            Assert.Equal(0xA03030, look.Color);
            Assert.Equal(2000, look.RadiusMm);
            Assert.True(look.AlphaPermille > 0);
            Assert.True(look.EnrageRing);
        }
    }

    [Fact]
    public void An_open_lock_overlays_the_enrage_ring_which_stays_under_it()
    {
        LockLook look = Open(Window, enraged: true);
        Assert.Equal(LockPhase.Open, look.Phase);
        Assert.Equal(0xC77DFF, look.Color);
        Assert.Equal(4000, look.RadiusMm);
        Assert.True(look.EnrageRing);
        Assert.False(Open(Window).EnrageRing);
    }

    [Fact]
    public void The_third_fail_flashes_red_over_the_new_enrage_ring()
    {
        LockLook look = LockRule.For(false, 0, Window, 0, 3, true, Never, 0);
        Assert.Equal(LockPhase.Failed, look.Phase);
        Assert.True(look.EnrageRing);
    }

    [Fact]
    public void Open_wins_over_a_flash_from_the_previous_lock()
    {
        LockLook look = LockRule.For(true, Window, Window, 0, 1, false, Never, 3);
        Assert.Equal(LockPhase.Open, look.Phase);
    }

    [Fact]
    public void Same_inputs_give_the_same_look()
    {
        Assert.Equal(Open(17, true), Open(17, true));
    }

    [Fact]
    public void A_zero_length_window_does_not_divide_by_zero()
    {
        Assert.Equal(1500, LockRule.For(true, 0, 0, 0, 0, false, Never, Never).RadiusMm);
    }
}
