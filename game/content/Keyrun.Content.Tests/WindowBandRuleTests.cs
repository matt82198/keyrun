using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// The Window ring colour band (<see cref="WindowBandRule"/>, BIBLE s4 "The Window ring shows which classes' Payoffs
/// can close it by colour band"): the band holds one segment per class that can close the Window, in the class
/// colour. A class closes it when it is not the Window's setup class, owns at least one Payoff ability, and the combo
/// table has its (setupClass, payoffClass) cell: the exact row for a target/self Window, the <c>"*"</c> field row for a
/// floor Window (the same lookups as the sim's Payoff.TryClosePayoff and TryClosePayoffOnFloor). Integer math only.
/// </summary>
public sealed class WindowBandRuleTests
{
    private static readonly ContentRegistry Reg = ContentLoader.Load();

    [Theory]
    [InlineData("warden", "cinder,chanter")]
    [InlineData("cinder", "warden,chanter")]
    [InlineData("chanter", "warden,cinder")]
    public void A_target_window_is_closed_by_every_other_class_with_a_payoff_and_a_combo(string setupClass, string expected)
    {
        Assert.Equal(expected, string.Join(",", WindowBandRule.ClosingClasses(Reg, setupClass, floor: false)));
    }

    [Theory]
    [InlineData("warden", "cinder,chanter")]
    [InlineData("cinder", "warden,chanter")]
    [InlineData("chanter", "warden,cinder")]
    public void A_floor_window_is_closed_through_its_field_row(string setupClass, string expected)
    {
        Assert.Equal(expected, string.Join(",", WindowBandRule.ClosingClasses(Reg, setupClass, floor: true)));
    }

    [Fact]
    public void A_class_never_appears_in_the_band_of_its_own_window()
    {
        Assert.True(Reg.Classes.Count >= 3, "vacuity guard: only " + Reg.Classes.Count + " classes");
        foreach (string setup in Reg.Classes.Keys)
        {
            Assert.DoesNotContain(setup, WindowBandRule.ClosingClasses(Reg, setup, floor: false));
            Assert.DoesNotContain(setup, WindowBandRule.ClosingClasses(Reg, setup, floor: true));
        }
    }

    [Fact]
    public void A_class_with_no_payoff_ability_is_left_out_of_the_band()
    {
        ContentRegistry noCinderPayoff = Reg with
        {
            Abilities = new ValueMap<AbilityDef>(Reg.Abilities.Select(kv => kv.Key.StartsWith("cinder.", System.StringComparison.Ordinal)
                ? new KeyValuePair<string, AbilityDef>(kv.Key, kv.Value with
                {
                    Effects = new ValueList<EffectOp>(kv.Value.Effects.Where(op => op is not PayoffOp and not ComboOp)),
                    Variants = null,
                })
                : kv)),
        };
        Assert.Equal(new[] { "chanter" }, WindowBandRule.ClosingClasses(noCinderPayoff, "warden", floor: false));
        Assert.Equal(new[] { "chanter" }, WindowBandRule.ClosingClasses(noCinderPayoff, "warden", floor: true));
    }

    [Fact]
    public void A_pair_with_no_combo_cell_is_left_out_of_the_band()
    {
        string[] classes = { "warden", "cinder", "chanter" };
        var payoffs = new HashSet<string>(classes);
        var cells = new[] { ("warden", "chanter"), ("warden", "*") };
        Assert.Equal(new[] { "chanter" }, WindowBandRule.ClosingClasses(classes, payoffs, cells, "warden", floor: false));
        Assert.Equal(new[] { "cinder", "chanter" }, WindowBandRule.ClosingClasses(classes, payoffs, cells, "warden", floor: true));
        Assert.Empty(WindowBandRule.ClosingClasses(classes, payoffs, cells, "cinder", floor: false));
    }

    [Fact]
    public void The_band_is_one_equal_contiguous_segment_per_closer_in_its_class_colour()
    {
        IReadOnlyList<WindowBandSegment> band = WindowBandRule.Band(new[] { "cinder", "chanter" });
        Assert.Equal(2, band.Count);
        Assert.Equal(new WindowBandSegment("cinder", WindowBandRule.ClassColor("cinder"), 0, 500), band[0]);
        Assert.Equal(new WindowBandSegment("chanter", WindowBandRule.ClassColor("chanter"), 500, 1000), band[1]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    public void The_band_always_covers_the_whole_ring_without_gaps(int closers)
    {
        IReadOnlyList<WindowBandSegment> band = WindowBandRule.Band(Enumerable.Range(0, closers).Select(i => "c" + i).ToArray());
        Assert.Equal(closers, band.Count);
        Assert.Equal(0, band[0].StartPermille);
        Assert.Equal(1000, band[^1].EndPermille);
        for (int i = 1; i < band.Count; i++)
        {
            Assert.Equal(band[i - 1].EndPermille, band[i].StartPermille);
            Assert.True(band[i].EndPermille > band[i].StartPermille, "empty segment " + i);
        }
    }

    [Fact]
    public void No_closer_means_no_band()
    {
        Assert.Empty(WindowBandRule.Band(System.Array.Empty<string>()));
    }

    [Fact]
    public void The_three_classes_have_distinct_colours()
    {
        int[] colors = Reg.Classes.Keys.Select(WindowBandRule.ClassColor).ToArray();
        Assert.Equal(colors.Length, colors.Distinct().Count());
        Assert.DoesNotContain(WindowBandRule.UnknownClassColor, colors);
    }
}
