using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// BIBLE s1 "Screen -> Queue -> Dungeon -> End-of-run choice of three -> Screen" and s7 "End of every run: choose one of
/// three ... every clear moves something visible". Drives real runs to each end the interactive game sees (a timed
/// clear, an untimed clear, a lasting wipe) and checks the offer <see cref="EndOfRun.Offer"/> makes (the one the
/// run-end panel draws): exactly three distinct cards, at least one a strict upgrade for the character, and the pick
/// lands on the character and survives the save the screen writes. Reward quality by key level is
/// <see cref="Encounter.RewardQualityFromKeyLevelTests"/> (PR #386) and the untimed key hold is #341/#385; not repeated.
/// An abandoned run gets nothing (STATE.md D45); that is pinned in <see cref="AbandonedRunGetsNothingTests"/>.
/// </summary>
public sealed class EndOfRunChoiceTests
{
    private const int EnemyId = 10;
    private const int ClearAfterTicks = 700;

    // Four facets per slot, as the shipped class tables have (content/data/facets/*.json).
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<Facet>> Pools = Rewards.GearSlots.ToDictionary(
        s => s,
        s => (IReadOnlyList<Facet>)Enumerable.Range(0, 4).Select(i => new Facet($"{s}.f{i}", $"{s} facet {i}", $"ability.{s}.{i}")).ToList(),
        StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, Item> NoGear = new Dictionary<string, Item>(StringComparer.Ordinal);

    public enum End
    {
        TimedClear,
        UntimedClear,
        Wipe,
    }

    private static Run Live(int keyLevel, int parTicks, long seed = 515151) =>
        RunRoot.CreateRun(
            seed,
            new[]
            {
                RunTestKit.Member(1, "warden", ActorRole.Tank, 0, 0, 500, 100),
                RunTestKit.Member(2, "cinder", ActorRole.Damage, 1000, 0, 250, 100),
            },
            RunTestKit.OnePull("choice-fixture", "the-only-pull", RunTestKit.Spawn(EnemyId, "dummy", 2000, 0, 1000)),
            RunTestKit.Content(lockoutTicks: 0) with { KeyLevel = keyLevel, ParTicks = parTicks });

    private static Run Ended(End end, int keyLevel = 7, long seed = 515151)
    {
        var (live, _) = RunTestKit.StepUntil(Live(keyLevel, end == End.UntimedClear ? 10 : 100000, seed), ClearAfterTicks);
        Assert.Null(live.RunEnd.Result);
        if (end == End.Wipe)
        {
            foreach (var id in new[] { 1, 2 })
            {
                live.State.Actors[id].Alive = false;
                live.State.Actors[id].Health = 0;
            }
            var (wiped, _) = RunTestKit.StepUntil(live, live.State.Tick + RunWiring.WipeGraceTicks + 2);
            Assert.NotNull(wiped.RunEnd.Result);
            return wiped;
        }
        live.State.Actors[EnemyId].Alive = false;
        live.State.Actors[EnemyId].Health = 0;
        return RunRoot.StepRun(live, Array.Empty<Command>()).Run;
    }

    private static string Key(Item i) => $"{i.Slot}:{i.Facet.Id}:{i.Tier}";

    [Theory]
    [InlineData(End.TimedClear, RunResult.Timed)]
    [InlineData(End.UntimedClear, RunResult.Depleted)]
    [InlineData(End.Wipe, RunResult.Depleted)]
    public void Every_run_end_offers_exactly_three_distinct_cards_from_the_pool(End end, RunResult expected)
    {
        for (var seed = 1L; seed <= 40; seed++)
        {
            var run = Ended(end, keyLevel: 2 + (int)(seed % 19), seed: seed);
            Assert.Equal(expected, run.RunEnd.Result);
            var offer = EndOfRun.Offer(run, Pools, NoGear);
            Assert.NotNull(offer);
            Assert.Equal(3, offer!.Count);
            Assert.Equal(3, offer.Select(Key).Distinct(StringComparer.Ordinal).Count());
            Assert.All(offer, c =>
            {
                Assert.Contains(c.Slot, Rewards.GearSlots);
                Assert.Contains(c.Facet, Pools[c.Slot]);
                Assert.InRange(c.Tier, 1, Progression.MaxTier);
            });
        }
    }

    [Fact]
    public void A_live_run_offers_nothing()
    {
        var (live, _) = RunTestKit.StepUntil(Live(7, 100000), 50);
        Assert.Null(EndOfRun.Offer(live, Pools, NoGear));
    }

    [Theory]
    [InlineData(End.TimedClear)]
    [InlineData(End.UntimedClear)]
    [InlineData(End.Wipe)]
    public void The_offer_is_the_run_seeded_roll_the_panel_has_always_drawn(End end)
    {
        // The panel's reward seed (server/src/run-end.ts): run seed x 1000003 + the ending tick. Moving the roll out of
        // the HUD must not change a single card a fresh character is shown.
        var run = Ended(end);
        var seed = unchecked((run.State.Seed * 1_000_003L) + run.RunEnd.ResultTick!.Value);
        Assert.Equal(seed, EndOfRun.RewardSeed(run));
        var expected = Rewards.RollRewardChoices(seed, run.Encounter.KeyLevel, run.RunEnd.Result!.Value, Rewards.GearSlots, Pools);
        Assert.Equal(expected.Select(i => i.Id), EndOfRun.Offer(run, Pools, NoGear)!.Select(i => i.Id));
    }

    [Theory]
    [InlineData(End.TimedClear, 0)]
    [InlineData(End.UntimedClear, 1)]
    [InlineData(End.Wipe, 2)]
    public void The_pick_goes_onto_the_character_and_survives_the_screen_save(End end, int pick)
    {
        var run = Ended(end);
        var offer = EndOfRun.Offer(run, Pools, NoGear)!;
        // The screen's order: bank at the run end (win or lose), then the take from the panel.
        var character = Progression.Start(99).BankRun(run.RunEnd.Result!.Value).Take(offer[pick]);
        Assert.Equal(offer[pick], character.Equipped[offer[pick].Slot]);
        Assert.Equal(offer[pick], character.LastRun!.Taken);
        var reloaded = ProgressionSave.Load(ProgressionSave.Serialize(character), 99);
        Assert.Null(reloaded.Error);
        Assert.Equal(offer[pick].Id, reloaded.Progression.Equipped[offer[pick].Slot].Id);
        Assert.Equal(offer[pick].Tier, reloaded.Progression.Equipped[offer[pick].Slot].Tier);
    }

    // A character whose every slot already holds, at the band's top tier, the facet the plain roll would offer in that
    // slot is the worst case for "every clear moves something visible": without the guarantee none of the cards is an
    // upgrade whenever the plain roll's three cards all name an equipped facet at or below its tier.
    private static Dictionary<string, Item> GearMatching(IReadOnlyList<Item> plain, int tier)
    {
        var gear = new Dictionary<string, Item>(StringComparer.Ordinal);
        foreach (var slot in Rewards.GearSlots)
        {
            var facet = plain.FirstOrDefault(c => c.Slot == slot)?.Facet ?? Pools[slot][0];
            gear[slot] = new Item($"owned-{slot}", slot, tier, facet, 2);
        }
        return gear;
    }

    [Fact]
    public void A_clear_whose_plain_roll_upgrades_nothing_still_offers_one_strict_upgrade()
    {
        var found = 0;
        for (var seed = 0L; seed < 4000; seed++)
        {
            var plain = Rewards.RollRewardChoices(seed, 7, RunResult.Timed, Rewards.GearSlots, Pools);
            // Every plain card is the equipped facet at a tier no higher than the one owned: no upgrade among them.
            if (plain.Select(c => c.Slot).Distinct().Count() != plain.Count)
            {
                continue;
            }
            var gear = GearMatching(plain, plain.Max(c => c.Tier));
            Assert.DoesNotContain(plain, c => EndOfRun.IsUpgrade(c, gear));
            found++;
            var offer = Rewards.RollRewardChoices(seed, 7, RunResult.Timed, Rewards.GearSlots, Pools, gear);
            Assert.Equal(3, offer.Count);
            Assert.Equal(3, offer.Select(Key).Distinct(StringComparer.Ordinal).Count());
            Assert.Contains(offer, c => EndOfRun.IsUpgrade(c, gear));
            // Minimal: only the third card changes.
            Assert.Equal(plain.Take(2).Select(i => i.Id), offer.Take(2).Select(i => i.Id));
        }
        Assert.True(found > 100, $"only {found} worst-case seeds");
    }

    [Fact]
    public void A_roll_that_already_holds_an_upgrade_is_left_exactly_as_drawn()
    {
        for (var seed = 0L; seed < 500; seed++)
        {
            var plain = Rewards.RollRewardChoices(seed, 9, RunResult.Depleted, Rewards.GearSlots, Pools);
            Assert.Equal(plain.Select(i => i.Id), Rewards.RollRewardChoices(seed, 9, RunResult.Depleted, Rewards.GearSlots, Pools, NoGear).Select(i => i.Id));
            var gear = new Dictionary<string, Item>(StringComparer.Ordinal) { ["weapon"] = new("owned", "weapon", 1, Pools["weapon"][0], 2) };
            if (plain.Any(c => EndOfRun.IsUpgrade(c, gear)))
            {
                Assert.Equal(plain.Select(i => i.Id), Rewards.RollRewardChoices(seed, 9, RunResult.Depleted, Rewards.GearSlots, Pools, gear).Select(i => i.Id));
            }
        }
    }

    [Fact]
    public void The_guaranteed_upgrade_stays_inside_the_key_band_and_never_trades_tier_away()
    {
        // Gear one tier under the band top in every slot: the upgrade is the band top, never above the key's band.
        for (var seed = 0L; seed < 300; seed++)
        {
            var plain = Rewards.RollRewardChoices(seed, 2, RunResult.Timed, Rewards.GearSlots, Pools);
            var gear = GearMatching(plain, 2);
            var offer = Rewards.RollRewardChoices(seed, 2, RunResult.Timed, Rewards.GearSlots, Pools, gear);
            var upgrades = offer.Where(c => EndOfRun.IsUpgrade(c, gear)).ToList();
            Assert.NotEmpty(upgrades);
            Assert.All(offer, c => Assert.InRange(c.Tier, 1, 2)); // key 2 band is tiers 1-2
            Assert.All(upgrades, c => Assert.True(c.Tier >= gear[c.Slot].Tier));
        }
    }

    [Fact]
    public void Gear_above_the_key_band_gets_the_plain_roll_because_quality_comes_from_the_key()
    {
        var gear = Rewards.GearSlots.ToDictionary(s => s, s => new Item($"owned-{s}", s, 10, Pools[s][0], 0), StringComparer.Ordinal);
        for (var seed = 0L; seed < 100; seed++)
        {
            var plain = Rewards.RollRewardChoices(seed, 4, RunResult.Timed, Rewards.GearSlots, Pools);
            Assert.Equal(plain.Select(i => i.Id), Rewards.RollRewardChoices(seed, 4, RunResult.Timed, Rewards.GearSlots, Pools, gear).Select(i => i.Id));
        }
    }

    [Theory]
    [InlineData(null, 5, true)] // empty slot
    [InlineData(true, 4, true)] // same facet, the card is a higher tier
    [InlineData(true, 5, false)] // same facet, same tier
    [InlineData(true, 6, false)] // same facet, the card is lower
    [InlineData(false, 5, true)] // a facet not owned, no tier lost
    [InlineData(false, 6, false)] // a new facet that would trade a tier away is not a strict upgrade
    public void A_strict_upgrade_is_an_empty_slot_a_higher_tier_or_a_new_facet_at_no_tier_loss(bool? sameFacet, int ownedTier, bool upgrade)
    {
        var card = new Item("card", "ring", 5, new Facet("a", "a", "x"), 2);
        var gear = new Dictionary<string, Item>(StringComparer.Ordinal);
        if (sameFacet is { } same)
        {
            gear["ring"] = new Item("owned", "ring", ownedTier, same ? card.Facet : new Facet("other", "o", "x"), 2);
        }
        Assert.Equal(upgrade, EndOfRun.IsUpgrade(card, gear));
    }

    [Fact]
    public void Three_distinct_cards_even_when_the_draw_collides_twenty_times()
    {
        // One slot, one facet, key 4 (tiers 1-3): exactly three distinct cards exist, so the random draw collides often
        // and, on some seeds, more than the 20 re-draws allow. The cards must still be the three distinct ones.
        var one = new Dictionary<string, IReadOnlyList<Facet>>(StringComparer.Ordinal) { ["ring"] = new[] { new Facet("ring.only", "only", "x") } };
        for (var seed = 0L; seed < 20000; seed++)
        {
            var offer = Rewards.RollRewardChoices(seed, 4, RunResult.Timed, new[] { "ring" }, one);
            Assert.Equal(3, offer.Select(Key).Distinct(StringComparer.Ordinal).Count());
        }
    }

    [Fact]
    public void A_pool_too_small_for_three_distinct_cards_fails_closed()
    {
        // Key 2 offers tiers 1-2: one slot with one facet has only two distinct cards. Fail closed (CLAUDE.md
        // invariant 4) rather than show the player a duplicate.
        var one = new Dictionary<string, IReadOnlyList<Facet>>(StringComparer.Ordinal) { ["ring"] = new[] { new Facet("ring.only", "only", "x") } };
        Assert.Throws<ArgumentException>(() => Rewards.RollRewardChoices(1, 2, RunResult.Timed, new[] { "ring" }, one));
    }
}
