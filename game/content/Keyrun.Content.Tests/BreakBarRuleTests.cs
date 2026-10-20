using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// The boss frame's Break bar rule (<see cref="BreakBarRule"/>, BREAK-BAR; BIBLE s9 mechanic 2): the sim bar drains
/// from max to 0, so the frame shows break progress, (max - current) / max, filling as the party's interrupts and
/// controls land; the tick it reaches 0 the bar is full and flashes for <see cref="BreakBarRule.FlashTicks"/>, stays
/// full ("broken") while the Twins are staggered, and is empty again the tick the sim refills it. Integer math only.
/// </summary>
public sealed class BreakBarRuleTests
{
    private const int Reset = 200;

    [Fact]
    public void A_full_sim_bar_is_an_empty_break_bar()
    {
        BreakBarLook look = BreakBarRule.For(100, 100, 0, 50, Reset);
        Assert.Equal(BreakBarPhase.Filling, look.Phase);
        Assert.Equal(0, look.FillPermille);
        Assert.Equal(0, look.FlashAlphaPermille);
    }

    [Theory]
    [InlineData(100, 0)]
    [InlineData(75, 250)]
    [InlineData(40, 600)]
    [InlineData(1, 990)]
    public void The_bar_fills_as_the_sim_value_drains(long current, int fill)
    {
        BreakBarLook look = BreakBarRule.For(current, 100, 0, 50, Reset);
        Assert.Equal(BreakBarPhase.Filling, look.Phase);
        Assert.Equal(fill, look.FillPermille);
        Assert.Equal(BreakBarRule.FillColor, look.Color);
    }

    [Fact]
    public void Fill_rises_monotonically_with_each_drain()
    {
        int last = -1;
        for (long current = 100; current > 0; current -= 7)
        {
            int fill = BreakBarRule.For(current, 100, 0, 1, Reset).FillPermille;
            Assert.True(fill > last, $"fill {fill} at current {current} did not rise past {last}");
            last = fill;
        }
    }

    [Fact]
    public void The_break_tick_is_a_full_bar_with_a_bright_flash()
    {
        // Broken at tick 300: the sim sets CooldownReadyTick = 300 + 200.
        BreakBarLook look = BreakBarRule.For(0, 100, 500, 300, Reset);
        Assert.Equal(BreakBarPhase.Broken, look.Phase);
        Assert.Equal(1000, look.FillPermille);
        Assert.Equal(BreakBarRule.BrokenColor, look.Color);
        Assert.Equal(BreakBarRule.FlashAlphaPermille, look.FlashAlphaPermille);
    }

    [Fact]
    public void The_flash_fades_then_the_bar_holds_broken_until_the_reset()
    {
        int mid = BreakBarRule.For(0, 100, 500, 300 + (BreakBarRule.FlashTicks / 2), Reset).FlashAlphaPermille;
        Assert.InRange(mid, 1, BreakBarRule.FlashAlphaPermille - 1);
        BreakBarLook held = BreakBarRule.For(0, 100, 500, 300 + BreakBarRule.FlashTicks, Reset);
        Assert.Equal(BreakBarPhase.Broken, held.Phase);
        Assert.Equal(1000, held.FillPermille);
        Assert.Equal(0, held.FlashAlphaPermille);
    }

    [Fact]
    public void The_refilled_sim_bar_resets_the_break_bar_to_empty()
    {
        BreakBarLook look = BreakBarRule.For(100, 100, 0, 500, Reset);
        Assert.Equal(BreakBarPhase.Filling, look.Phase);
        Assert.Equal(0, look.FillPermille);
    }

    [Fact]
    public void A_bar_with_no_max_draws_empty()
    {
        BreakBarLook look = BreakBarRule.For(0, 0, 0, 5, Reset);
        Assert.Equal(BreakBarPhase.Filling, look.Phase);
        Assert.Equal(0, look.FillPermille);
    }
}
