using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// Lane ABILITYBAR-RANGE-TINT: an out-of-range ability reads like WoW, its icon tinted red, and the slot keeps its
/// normal edge (a red border read as an error). The decision is <see cref="AbilitySlotLook"/>; AbilityBar applies it.
/// </summary>
public sealed class AbilitySlotLookTests
{
    private static bool RedDominant(Rgba8 c) => c.R >= 2 * c.G && c.R >= 2 * c.B && c.R >= 128;

    [Fact]
    public void Out_of_range_icon_modulate_is_red_dominant()
    {
        var tint = AbilitySlotLook.IconModulate(outOfRange: true);
        Assert.True(RedDominant(tint), $"out-of-range icon modulate {tint} is not red-dominant");
        Assert.Equal(255, tint.A);
    }

    [Fact]
    public void Out_of_range_draws_no_red_edge()
    {
        var edge = AbilitySlotLook.Edge(outOfRange: true);
        Assert.False(RedDominant(edge), $"out-of-range edge {edge} is red");
        Assert.Equal(AbilitySlotLook.Edge(outOfRange: false), edge);
        Assert.Equal(AbilitySlotLook.EdgeWidth(outOfRange: false), AbilitySlotLook.EdgeWidth(outOfRange: true));
    }

    [Fact]
    public void In_range_icon_keeps_the_default_tint()
    {
        Assert.Equal(AbilitySlotLook.IconTint, AbilitySlotLook.IconModulate(outOfRange: false));
        Assert.False(RedDominant(AbilitySlotLook.IconModulate(outOfRange: false)));
    }
}
