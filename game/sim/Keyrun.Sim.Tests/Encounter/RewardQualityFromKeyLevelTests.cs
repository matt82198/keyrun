using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Encounter;
using Xunit;

namespace Keyrun.Sim.Tests.Encounter;

/// <summary>
/// Guard for Matt 2026-10-20 (ledger f-baeb031a, landed in #348): "loot quality comes from key level; +2/+3 pays by
/// jumping the key 2 or 3 levels; choose-one-of-three stays." The tier of the three offered items is a function of the
/// key that was run (higher key, higher or equal tier, never above tier 10 per BIBLE s7); the chest (+1/+2/+3) only
/// moves the key. An untimed key keeping its level is f-7c7abdf4 (TimerTests / KeyMoveTests); this file only checks it
/// is not contradicted.
/// </summary>
public sealed class RewardQualityFromKeyLevelTests
{
    private const int Par = 10_000;

    // Run clocks that land a timed run on each chest: within 60% of par (+3), within 80% (+2), otherwise (+1).
    private static readonly (long Clock, int Chest)[] Chests = { (5_000, 3), (7_000, 2), (9_000, 1) };

    private static (int Min, int Max) ObservedBand(int key)
    {
        var tiers = Enumerable.Range(0, 300)
            .SelectMany(seed => Rewards.RollRewardChoices(seed, key, RunResult.Timed, Rewards.GearSlots))
            .Select(i => i.Tier)
            .ToList();
        return (tiers.Min(), tiers.Max());
    }

    [Fact]
    public void A_plus_one_two_or_three_chest_on_the_same_key_offers_the_same_three_and_only_moves_the_key()
    {
        for (var key = 2; key <= 20; key++)
        {
            var offers = new List<string>();
            foreach (var (clock, chest) in Chests)
            {
                Assert.Equal(chest, Timer.UpgradeTier(RunResult.Timed, clock, Par));
                Assert.Equal(key + chest, Timer.KeyAfterRun(key, RunResult.Timed, clock, Par));
                // The cards are rolled for the key that was run (RunEndView.Of passes run.Encounter.KeyLevel).
                var items = Enumerable.Range(0, 50)
                    .SelectMany(seed => Rewards.RollRewardChoices(seed, key, RunResult.Timed, Rewards.GearSlots))
                    .ToList();
                Assert.Equal(150, items.Count);
                offers.Add(string.Join(",", items.Select(i => i.Id)));
            }
            Assert.Single(offers.Distinct());
        }
    }

    [Fact]
    public void The_bigger_chest_pays_through_the_next_key_a_higher_or_equal_tier_band()
    {
        for (var key = 2; key <= 17; key++)
        {
            var bands = Chests
                .Select(c => ObservedBand(Timer.KeyAfterRun(key, RunResult.Timed, c.Clock, Par)))
                .ToList(); // +3, +2, +1
            Assert.True(bands[0].Min >= bands[1].Min && bands[1].Min >= bands[2].Min, $"key {key}: min tiers {string.Join("/", bands)}");
            Assert.True(bands[0].Max >= bands[1].Max && bands[1].Max >= bands[2].Max, $"key {key}: max tiers {string.Join("/", bands)}");
        }
        // Somewhere along the ladder the +3 jump strictly out-pays the +1.
        var strictly = Enumerable.Range(2, 16).Any(key =>
            ObservedBand(Timer.KeyAfterRun(key, RunResult.Timed, 5_000, Par)).Min
            > ObservedBand(Timer.KeyAfterRun(key, RunResult.Timed, 9_000, Par)).Min);
        Assert.True(strictly, "+3 never offered a higher tier floor than +1 on any key 2-17");
    }

    [Fact]
    public void A_higher_key_offers_a_higher_or_equal_tier_and_the_top_of_the_ladder_beats_the_bottom()
    {
        var previous = ObservedBand(2);
        for (var key = 3; key <= 20; key++)
        {
            var band = ObservedBand(key);
            Assert.True(band.Min >= previous.Min, $"key {key} min tier {band.Min} < key {key - 1} min tier {previous.Min}");
            Assert.True(band.Max >= previous.Max, $"key {key} max tier {band.Max} < key {key - 1} max tier {previous.Max}");
            previous = band;
        }
        Assert.True(ObservedBand(20).Min > ObservedBand(2).Max, "key 20's worst card is not better than key 2's best");
    }

    [Fact]
    public void No_offered_tier_exceeds_10_at_any_key_or_result_and_there_are_always_three()
    {
        var results = new[] { RunResult.Timed, RunResult.Depleted, RunResult.Abandoned };
        foreach (var result in results)
        {
            for (var key = 2; key <= 40; key++)
            {
                for (var seed = 0; seed < 100; seed++)
                {
                    var items = Rewards.RollRewardChoices(seed, key, result, Rewards.GearSlots);
                    Assert.Equal(3, items.Count);
                    Assert.All(items, i => Assert.InRange(i.Tier, 1, 10));
                }
            }
        }
    }

    [Fact]
    public void An_untimed_key_keeps_its_level_and_its_cards_match_a_timed_run_on_that_key()
    {
        foreach (var result in new[] { RunResult.Depleted, RunResult.Abandoned })
        {
            for (var key = 2; key <= 20; key++)
            {
                Assert.Equal(key, Timer.KeyAfterRun(key, result, 20_000, Par));
                for (var seed = 0; seed < 20; seed++)
                {
                    var timed = Rewards.RollRewardChoices(seed, key, RunResult.Timed, Rewards.GearSlots).Select(i => i.Id);
                    var untimed = Rewards.RollRewardChoices(seed, key, result, Rewards.GearSlots).Select(i => i.Id);
                    Assert.Equal(timed, untimed);
                }
            }
        }
    }
}
