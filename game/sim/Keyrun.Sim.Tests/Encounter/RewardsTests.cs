using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Encounter;
using Xunit;

namespace Keyrun.Sim.Tests.Encounter;

/// <summary>
/// tests/sim/encounter/rewards.test.ts ported 1:1, plus golden rolls. No oracle fixture covers rewards (the
/// tests/oracle generator has no rewards scenario), so the golden ids below were produced by running the frozen
/// sim/encounter/rewards.ts and sim/core/prng.ts at 6e1548d under node 24 (only the import specifiers changed to
/// add ".ts"; RunResult inlined as its string union). One row is a documented break from the frozen TS: since
/// f-baeb031a the tier band is a function of key level only (see the abandoned key-20 row).
/// </summary>
public sealed class RewardsTests
{
    private static string Key(Item i) => $"{i.Slot}:{i.Facet.Id}:{i.Tier}";

    [Fact]
    public void Is_a_pure_function_of_its_inputs_same_args_always_produce_the_same_three_items()
    {
        Assert.Equal(Rewards.RollRewardChoices(42, 10, RunResult.Timed, Rewards.GearSlots), Rewards.RollRewardChoices(42, 10, RunResult.Timed, Rewards.GearSlots));
    }

    [Fact]
    public void Changes_with_the_seed()
    {
        Assert.NotEqual(
            Rewards.RollRewardChoices(1, 10, RunResult.Timed, Rewards.GearSlots).Select(i => i.Id),
            Rewards.RollRewardChoices(2, 10, RunResult.Timed, Rewards.GearSlots).Select(i => i.Id));
    }

    [Fact]
    public void Never_returns_two_choices_with_the_same_slot_facet_tier_across_many_seeds()
    {
        for (var seed = 0; seed < 200; seed++)
        {
            var items = Rewards.RollRewardChoices(seed, 15, RunResult.Timed, Rewards.GearSlots);
            Assert.Equal(3, items.Count);
            Assert.Equal(3, items.Select(Key).Distinct().Count());
        }
    }

    [Fact]
    public void Never_rolls_tier_above_8_on_a_low_key_even_across_many_seeds()
    {
        for (var seed = 0; seed < 100; seed++)
        {
            var items = Rewards.RollRewardChoices(seed, 3, RunResult.Timed, Rewards.GearSlots);
            Assert.Equal(3, items.Count);
            Assert.All(items, i => Assert.True(i.Tier <= 8));
        }
    }

    // Deliberate spec change, Matt 2026-10-20 (ledger f-baeb031a): "loot quality comes from key level; +2/+3 pays
    // by jumping the key 2 or 3 levels; choose-one-of-three stays." This replaces the frozen rewards.ts rule that
    // only a timed clear could roll tiers 9-10 (the old test asserted a depleted or abandoned key-20 run stays at
    // tier 8 or below). The run result now pays only through the key-level change (Timer.cs), never the loot band.
    [Fact]
    public void Can_roll_tiers_9_10_on_a_depleted_or_abandoned_result_at_key_15_or_more()
    {
        foreach (var result in new[] { RunResult.Depleted, RunResult.Abandoned })
        {
            var saw = false;
            for (var seed = 0; seed < 300 && !saw; seed++)
            {
                var items = Rewards.RollRewardChoices(seed, Rewards.HardBisMinKey, result, Rewards.GearSlots);
                Assert.Equal(3, items.Count);
                saw = items.Any(i => i.Tier >= 9);
            }
            Assert.True(saw, $"no tier 9-10 in 300 seeds at key {Rewards.HardBisMinKey} on a {result} run");
        }
    }

    // f-baeb031a: quality is a function of key level only, so at every key the timed, depleted and abandoned rolls
    // draw over the same tier band. Same seed, key and slots means the same PRNG draws over the same band, so the
    // three cards must be identical whatever the result, and the observed tier range must match too.
    [Fact]
    public void Same_key_level_gives_the_same_tier_band_for_timed_and_depleted()
    {
        for (var key = 2; key <= 20; key++)
        {
            var timedTiers = new SortedSet<int>();
            var depletedTiers = new SortedSet<int>();
            for (var seed = 0; seed < 200; seed++)
            {
                var timed = Rewards.RollRewardChoices(seed, key, RunResult.Timed, Rewards.GearSlots);
                var depleted = Rewards.RollRewardChoices(seed, key, RunResult.Depleted, Rewards.GearSlots);
                var abandoned = Rewards.RollRewardChoices(seed, key, RunResult.Abandoned, Rewards.GearSlots);
                Assert.Equal(3, timed.Count);
                Assert.Equal(timed.Select(i => i.Id), depleted.Select(i => i.Id));
                Assert.Equal(timed.Select(i => i.Id), abandoned.Select(i => i.Id));
                timedTiers.UnionWith(timed.Select(i => i.Tier));
                depletedTiers.UnionWith(depleted.Select(i => i.Tier));
            }
            Assert.Equal(timedTiers, depletedTiers);
        }
    }

    [Fact]
    public void Never_rolls_the_hard_BiS_tiers_below_key_15_even_on_a_timed_clear()
    {
        for (var seed = 0; seed < 100; seed++)
        {
            var items = Rewards.RollRewardChoices(seed, Rewards.HardBisMinKey - 1, RunResult.Timed, Rewards.GearSlots);
            Assert.Equal(3, items.Count);
            Assert.All(items, i => Assert.True(i.Tier <= 8));
        }
    }

    [Fact]
    public void Can_roll_tiers_9_10_on_a_timed_clear_at_key_15_or_more()
    {
        var saw = Enumerable.Range(0, 300).Any(seed => Rewards.RollRewardChoices(seed, 20, RunResult.Timed, Rewards.GearSlots).Any(i => i.Tier >= 9));
        Assert.True(saw);
    }

    [Fact]
    public void Every_rolled_tier_stays_within_1_10_and_every_slot_is_one_of_the_five_gear_slots()
    {
        for (var seed = 0; seed < 50; seed++)
        {
            foreach (var item in Rewards.RollRewardChoices(seed, 18, RunResult.Timed, Rewards.GearSlots))
            {
                Assert.InRange(item.Tier, 1, 10);
                Assert.Contains(item.Slot, Rewards.GearSlots);
                Assert.Equal(2, item.Charges);
            }
        }
    }

    [Theory]
    [InlineData(42L, 10, "timed", "", "reward-armor-armor-ward-t4-0 reward-ring-ring-verse-t6-1 reward-keystone-keystone-tempo-t5-2")]
    // Documented break from the frozen TS (f-0b0afff2, BIBLE s7 "Facets change function"): the frozen rewards.ts offers
    // this key-2 roll "...-weapon-haft-t1-1 reward-weapon-weapon-haft-t2-2", one facet at two tiers, because it only kept
    // (slot, facet, tier) distinct. The sim now re-draws a facet already on offer; the first two cards and the PRNG
    // stream up to the repeat are unchanged, and the re-draw lands on keystone-echo t2.
    [InlineData(7L, 2, "depleted", "", "reward-ring-ring-chorus-t1-0 reward-weapon-weapon-haft-t1-1 reward-keystone-keystone-echo-t2-2")]
    [InlineData(11L, 15, "timed", "", "reward-armor-armor-ward-t6-0 reward-keystone-keystone-tempo-t7-1 reward-ring-ring-verse-t6-2")]
    [InlineData(123456789L, 20, "timed", "", "reward-weapon-weapon-haft-t7-0 reward-keystone-keystone-echo-t8-1 reward-weapon-weapon-brand-t8-2")]
    // Documented break from the frozen TS (f-baeb031a): rewards.ts:85 gives this abandoned key-20 roll the band 7-8
    // ("...-echo-t7-1"); the key-only band is 7-10. The ids are the frozen rewards.ts and prng.ts run under node 24
    // with only line 85 changed to `hardBis = key >= HARD_BIS_MIN_KEY` (the same harness reproduces all six frozen
    // rows unchanged under the original line); only the second card's tier moves, t7 to t9.
    [InlineData(-1L, 20, "abandoned", "", "reward-armor-armor-brace-t7-0 reward-keystone-keystone-echo-t9-1 reward-armor-armor-ward-t7-2")]
    [InlineData(0L, 3, "timed", "ring,charm", "reward-ring-ring-coda-t1-0 reward-charm-charm-flare-t2-1 reward-ring-ring-verse-t1-2")]
    public void Golden_rolls_match_the_frozen_TS(long seed, int key, string result, string slots, string expectedIds)
    {
        var r = result switch { "timed" => RunResult.Timed, "depleted" => RunResult.Depleted, _ => RunResult.Abandoned };
        IReadOnlyList<string> owned = slots.Length == 0 ? Rewards.GearSlots : slots.Split(',');
        Assert.Equal(expectedIds, string.Join(" ", Rewards.RollRewardChoices(seed, key, r, owned).Select(i => i.Id)));
    }

    [Fact]
    public void An_empty_owned_slot_list_falls_back_to_all_five_slots()
    {
        Assert.Equal(
            Rewards.RollRewardChoices(9, 6, RunResult.Timed, Rewards.GearSlots).Select(i => i.Id),
            Rewards.RollRewardChoices(9, 6, RunResult.Timed, System.Array.Empty<string>()).Select(i => i.Id));
    }
}
