using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Encounter;
using Xunit;

namespace Keyrun.Sim.Tests.Encounter;

/// <summary>
/// BIBLE s7 "End of every run: choose one of three" and "Facets change function": the three cards are a choice between
/// functions, so no (slot, facet) appears twice, not even at two different tiers (finding f-0b0afff2). The D15 capture
/// on main 9f3d091a (frame one-042, key 2, run seed 1) offered Weapon T2 wideningBreak, Keystone T1 warlordsMark and
/// Keystone T2 warlordsMark: the old roll only kept (slot, facet, tier) distinct, and at key 2 the band is tiers 1-2.
/// </summary>
public sealed class RewardsDistinctFacetsTests
{
    // The capture: run seed 1 at key 2. EndOfRun.RewardSeed is seed x 1000003 + end tick; tick 691 is the first end
    // tick that reproduces the captured three cards in the captured order (scan of ticks 0-99999 against main 9f3d091a).
    private const long CaptureRunSeed = 1;
    private const long CaptureEndTick = 691;
    private const long CaptureRewardSeed = (CaptureRunSeed * 1_000_003L) + CaptureEndTick;
    private const int CaptureKey = 2;

    private static readonly string[] Classes = { "warden", "cinder", "chanter" };

    private static readonly IReadOnlyDictionary<string, Item> NoGear = new Dictionary<string, Item>(StringComparer.Ordinal);

    private static string FacetKey(Item i) => $"{i.Slot}:{i.Facet.Id}";

    private static void AssertDistinctFacets(IReadOnlyList<Item> items, string context)
    {
        Assert.Equal(3, items.Count);
        var keys = items.Select(FacetKey).ToList();
        Assert.True(
            keys.Distinct(StringComparer.Ordinal).Count() == 3,
            $"{context}: two cards share a (slot, facet): {string.Join(" | ", items.Select(i => i.Id))}");
    }

    [Fact]
    public void The_captured_key_2_roll_offers_three_distinct_facets()
    {
        var pool = RewardsContentPoolTests.ContentPool("warden");
        var items = Rewards.RollRewardChoices(CaptureRewardSeed, CaptureKey, RunResult.Timed, Rewards.GearSlots, pool, NoGear);
        AssertDistinctFacets(items, $"capture seed {CaptureRewardSeed} key {CaptureKey}");
    }

    [Fact]
    public void The_captured_roll_keeps_its_first_two_cards_and_redraws_only_the_repeat()
    {
        // Draw without replacement over facets by re-drawing on a facet repeat: the stream up to the repeat is unchanged.
        var pool = RewardsContentPoolTests.ContentPool("warden");
        var items = Rewards.RollRewardChoices(CaptureRewardSeed, CaptureKey, RunResult.Timed, Rewards.GearSlots, pool, NoGear);
        Assert.Equal("reward-weapon-warden.weapon.wideningBreak-t2-0", items[0].Id);
        Assert.Equal("reward-keystone-warden.keystone.warlordsMark-t1-1", items[1].Id);
        Assert.NotEqual("warden.keystone.warlordsMark", items[2].Facet.Id);
    }

    [Theory]
    [InlineData("warden")]
    [InlineData("cinder")]
    [InlineData("chanter")]
    public void No_seed_or_key_offers_the_same_facet_twice_from_a_content_pool(string classId)
    {
        var pool = RewardsContentPoolTests.ContentPool(classId);
        for (var key = 2; key <= 20; key++)
        {
            for (var seed = 0L; seed < 300; seed++)
            {
                var s = (seed * 1_000_003L) + (seed * 37);
                AssertDistinctFacets(Rewards.RollRewardChoices(s, key, RunResult.Timed, Rewards.GearSlots, pool), $"{classId} seed {s} key {key}");
                AssertDistinctFacets(Rewards.RollRewardChoices(s, key, RunResult.Timed, Rewards.GearSlots, pool, NoGear), $"{classId} seed {s} key {key} no gear");
            }
        }
    }

    [Fact]
    public void No_seed_or_key_offers_the_same_facet_twice_from_the_placeholder_pools()
    {
        for (var key = 2; key <= 20; key++)
        {
            for (var seed = 0L; seed < 300; seed++)
            {
                AssertDistinctFacets(Rewards.RollRewardChoices(seed, key, RunResult.Timed, Rewards.GearSlots), $"placeholder seed {seed} key {key}");
            }
        }
    }

    [Fact]
    public void The_upgrade_guarantee_never_repeats_a_facet_already_on_offer()
    {
        // Equipped gear that makes the plain roll hold no upgrade forces the guaranteed-upgrade replacement of card 3;
        // that replacement must not be a facet cards 1-2 already offer at another tier.
        foreach (var classId in Classes)
        {
            var pool = RewardsContentPoolTests.ContentPool(classId);
            for (var key = 2; key <= 20; key++)
            {
                for (var seed = 0L; seed < 200; seed++)
                {
                    var plain = Rewards.RollRewardChoices(seed, key, RunResult.Timed, Rewards.GearSlots, pool);
                    // Hold each offered card's slot at that card's tier and facet: none of the three is then an upgrade
                    // (same facet, same tier), so the guarantee has to find one.
                    var equipped = new Dictionary<string, Item>(StringComparer.Ordinal);
                    foreach (var card in plain)
                    {
                        if (!equipped.TryGetValue(card.Slot, out var held) || card.Tier > held.Tier)
                        {
                            equipped[card.Slot] = card;
                        }
                    }
                    var items = Rewards.RollRewardChoices(seed, key, RunResult.Timed, Rewards.GearSlots, pool, equipped);
                    AssertDistinctFacets(items, $"{classId} seed {seed} key {key} guarded");
                }
            }
        }
    }

    [Fact]
    public void A_pool_with_fewer_than_three_facets_fails_closed()
    {
        var pool = RewardsContentPoolTests.ContentPool("warden");
        var two = new Dictionary<string, IReadOnlyList<Facet>>(pool, StringComparer.Ordinal) { ["ring"] = pool["ring"].Take(2).ToList() };
        Assert.Throws<ArgumentException>(() => Rewards.RollRewardChoices(1, 10, RunResult.Timed, new[] { "ring" }, two));
    }
}
