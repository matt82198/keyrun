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

    // Lane RANGE-TINT-NOTARGET: like WoW, the red range tint only reads while the player has a living hostile target
    // out of that ability's range. A 5000 mm ability against a target 20000 mm away is out of range when it counts.
    private const long AbilityRange = 5000;
    private const long Far = 20000;

    [Fact]
    public void Living_hostile_target_out_of_range_is_tinted()
    {
        var target = new RangeTarget(Alive: true, Hostile: true, RangeMm: Far);
        Assert.True(AbilitySlotLook.ShowsOutOfRange(target, hostileAbility: true, AbilityRange, runOver: false));
    }

    [Fact]
    public void Living_hostile_target_in_range_is_not_tinted()
    {
        var target = new RangeTarget(Alive: true, Hostile: true, RangeMm: AbilityRange);
        Assert.False(AbilitySlotLook.ShowsOutOfRange(target, hostileAbility: true, AbilityRange, runOver: false));
    }

    [Fact]
    public void No_target_is_not_tinted()
    {
        Assert.False(AbilitySlotLook.ShowsOutOfRange(null, hostileAbility: true, AbilityRange, runOver: false));
    }

    [Fact]
    public void Dead_target_is_not_tinted()
    {
        var corpse = new RangeTarget(Alive: false, Hostile: true, RangeMm: Far);
        Assert.False(AbilitySlotLook.ShowsOutOfRange(corpse, hostileAbility: true, AbilityRange, runOver: false));
    }

    [Fact]
    public void Friendly_target_is_not_tinted()
    {
        var ally = new RangeTarget(Alive: true, Hostile: false, RangeMm: Far);
        Assert.False(AbilitySlotLook.ShowsOutOfRange(ally, hostileAbility: true, AbilityRange, runOver: false));
    }

    [Fact]
    public void Non_hostile_ability_is_not_tinted()
    {
        var target = new RangeTarget(Alive: true, Hostile: true, RangeMm: Far);
        Assert.False(AbilitySlotLook.ShowsOutOfRange(target, hostileAbility: false, AbilityRange, runOver: false));
    }

    [Fact]
    public void Ranged_less_ability_is_not_tinted()
    {
        var target = new RangeTarget(Alive: true, Hostile: true, RangeMm: Far);
        Assert.False(AbilitySlotLook.ShowsOutOfRange(target, hostileAbility: true, abilityRangeMm: 0, runOver: false));
    }

    [Fact]
    public void After_run_end_nothing_is_tinted()
    {
        var target = new RangeTarget(Alive: true, Hostile: true, RangeMm: Far);
        Assert.False(AbilitySlotLook.ShowsOutOfRange(target, hostileAbility: true, AbilityRange, runOver: true));
    }

    [Fact]
    public void Bar_call_without_run_state_tints_a_living_far_target_and_not_a_cleared_one()
    {
        // The ability bar passes no run-over flag (the run-end panel owns that state); on a cleared run the player's
        // target is a corpse, so the three-argument call alone leaves the slot untinted.
        Assert.True(AbilitySlotLook.ShowsOutOfRange(new RangeTarget(Alive: true, Hostile: true, RangeMm: Far), hostileAbility: true, AbilityRange));
        Assert.False(AbilitySlotLook.ShowsOutOfRange(new RangeTarget(Alive: false, Hostile: true, RangeMm: Far), hostileAbility: true, AbilityRange));
    }
}
