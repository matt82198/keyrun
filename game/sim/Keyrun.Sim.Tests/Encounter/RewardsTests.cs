using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Encounter;
using Xunit;

namespace Keyrun.Sim.Tests.Encounter;

/// <summary>
/// tests/sim/encounter/rewards.test.ts ported 1:1, plus golden rolls. No oracle fixture covers rewards (the
/// tests/oracle generator has no rewards scenario), so the golden ids below were produced by running the frozen
/// sim/encounter/rewards.ts and sim/core/prng.ts at 6e1548d under node 24 (only the import specifiers changed to
/// add ".ts"; RunResult inlined as its string union).
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
            Assert.All(Rewards.RollRewardChoices(seed, 3, RunResult.Timed, Rewards.GearSlots), i => Assert.True(i.Tier <= 8));
        }
    }

    [Fact]
    public void Never_rolls_the_hard_BiS_tiers_on_a_depleted_or_abandoned_result_even_at_a_high_key()
    {
        for (var seed = 0; seed < 100; seed++)
        {
            foreach (var result in new[] { RunResult.Depleted, RunResult.Abandoned })
            {
                Assert.All(Rewards.RollRewardChoices(seed, 20, result, Rewards.GearSlots), i => Assert.True(i.Tier <= 8));
            }
        }
    }

    [Fact]
    public void Never_rolls_the_hard_BiS_tiers_below_key_15_even_on_a_timed_clear()
    {
        for (var seed = 0; seed < 100; seed++)
        {
            Assert.All(Rewards.RollRewardChoices(seed, Rewards.HardBisMinKey - 1, RunResult.Timed, Rewards.GearSlots), i => Assert.True(i.Tier <= 8));
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
    [InlineData(7L, 2, "depleted", "", "reward-ring-ring-chorus-t1-0 reward-weapon-weapon-haft-t1-1 reward-weapon-weapon-haft-t2-2")]
    [InlineData(11L, 15, "timed", "", "reward-armor-armor-ward-t6-0 reward-keystone-keystone-tempo-t7-1 reward-ring-ring-verse-t6-2")]
    [InlineData(123456789L, 20, "timed", "", "reward-weapon-weapon-haft-t7-0 reward-keystone-keystone-echo-t8-1 reward-weapon-weapon-brand-t8-2")]
    [InlineData(-1L, 20, "abandoned", "", "reward-armor-armor-brace-t7-0 reward-keystone-keystone-echo-t7-1 reward-armor-armor-ward-t7-2")]
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
