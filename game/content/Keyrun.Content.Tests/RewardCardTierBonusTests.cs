using System;
using System.Globalization;
using System.Linq;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// BIBLE s7 "Tier is power and it is capped at 10" and "End of every run: choose one of three" (finding f-e880cd4c):
/// two run-end reward cards for the same Facet at Tier 1 and Tier 2 must not read the same. Each card states what its
/// tier gives, and the numbers come from the content gear table (gear.json via <see cref="GearItems.StatsAt"/>), never
/// from the view.
/// </summary>
public sealed class RewardCardTierBonusTests
{
    private static readonly ContentRegistry Reg = ContentLoader.Load();

    private const string FacetText = "Your Bash also chills the target.";

    // "+2%" style percent of a basis-point stat, computed here from the loaded data so the test follows gear.json.
    private static string Pct(long bp) => (Math.Abs(bp) / 100m).ToString("0.##", CultureInfo.InvariantCulture) + "%";

    [Fact]
    public void Same_facet_cards_at_tier_one_and_two_read_differently_and_name_each_tiers_bonus_from_the_gear_table()
    {
        var t1 = GearItems.CardLines(Reg.Gear, GearSlot.Charm, 1, FacetText);
        var t2 = GearItems.CardLines(Reg.Gear, GearSlot.Charm, 2, FacetText);

        Assert.NotEqual(string.Join("\n", t1), string.Join("\n", t2));
        Assert.Contains(FacetText, t1);
        Assert.Contains(FacetText, t2);

        var s1 = GearItems.StatsAt(Reg.Gear, GearSlot.Charm, 1);
        var s2 = GearItems.StatsAt(Reg.Gear, GearSlot.Charm, 2);
        Assert.NotEqual(s1.DamageDoneBp, s2.DamageDoneBp);
        Assert.Equal("Tier 1: +" + Pct(s1.DamageDoneBp) + " damage dealt", t1[0]);
        Assert.Equal("Tier 2: +" + Pct(s2.DamageDoneBp) + " damage dealt", t2[0]);
    }

    [Theory]
    [InlineData(GearSlot.Weapon)]
    [InlineData(GearSlot.Armor)]
    [InlineData(GearSlot.Charm)]
    [InlineData(GearSlot.Ring)]
    [InlineData(GearSlot.Keystone)]
    public void Every_slot_states_a_distinct_bonus_at_every_tier(GearSlot slot)
    {
        var lines = Enumerable.Range(1, GearItems.MaxTier).Select(t => GearItems.CardLines(Reg.Gear, slot, t, FacetText)[0]).ToList();
        Assert.Equal(lines.Count, lines.Distinct(StringComparer.Ordinal).Count());
        for (var t = 1; t <= GearItems.MaxTier; t++)
        {
            Assert.StartsWith(FormattableString.Invariant($"Tier {t}: "), lines[t - 1], StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Damage_taken_reads_as_a_reduction_with_its_tier_value()
    {
        var s = GearItems.StatsAt(Reg.Gear, GearSlot.Armor, 3);
        Assert.True(s.DamageTakenBp < 0);
        Assert.Contains("-" + Pct(s.DamageTakenBp) + " damage taken", GearItems.CardLines(Reg.Gear, GearSlot.Armor, 3, FacetText)[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Bonus_text_formats_both_stats_and_none()
    {
        Assert.Equal("+3% damage dealt, -2.5% damage taken", GearItems.BonusText(300, -250));
        Assert.Equal("no stat bonus", GearItems.BonusText(0, 0));
    }
}
