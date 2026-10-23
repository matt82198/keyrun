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
/// clear, an untimed clear, a lasting wipe) and checks the completion roll (<see cref="CompletionRoll.ForRun"/>, the one
/// the run-end panel draws; R-ROLL-1 removed <c>EndOfRun.Offer</c>): one roll per seat over the class pool, the won item
/// lands on the character and survives the save the screen writes; the Rewards choice-of-three primitive keeps its
/// strict-upgrade and Ascension guarantees. Reward quality by key level is
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

    // The choice-of-three reads of the removed EndOfRun.Offer (R-ROLL-1 (b)(iii)) are the Rewards primitive's, which stays:
    // the Ascension and strict-upgrade facts below pin that primitive over the run's own reward seed.
    private static IReadOnlyList<Item> ChoicesOf(Run run, IReadOnlyDictionary<string, Item> equipped) =>
        Rewards.RollRewardChoices(EndOfRun.RewardSeed(run), run.Encounter.KeyLevel, run.RunEnd.Result!.Value, Rewards.GearSlots, Pools, equipped);

    [Theory]
    [InlineData(End.TimedClear, RunResult.Timed)]
    [InlineData(End.UntimedClear, RunResult.Depleted)]
    [InlineData(End.Wipe, RunResult.Depleted)]
    public void Every_run_end_rolls_every_seat_once_over_the_class_pool(End end, RunResult expected)
    {
        // Migrated from "exactly three distinct cards" (the removed Offer): the end-of-run reward is one completion roll per
        // seat (R-ROLL-1), each item drawn over the class facet pool (no table: the Warden fallback), never the placeholders.
        for (var seed = 1L; seed <= 40; seed++)
        {
            var run = Ended(end, keyLevel: 2 + (int)(seed % 19), seed: seed);
            Assert.Equal(expected, run.RunEnd.Result);
            var rolls = CompletionRoll.ForRun(run);
            Assert.NotNull(rolls);
            Assert.Equal(new[] { 1, 2 }, rolls!.Select(r => r.Seat).OrderBy(s => s));
            Assert.All(rolls.Select(r => r.Item).OfType<Item>(), c =>
            {
                Assert.Contains(c.Slot, Rewards.GearSlots);
                Assert.Contains(c.Facet, CompletionFacets.Fallback[c.Slot]);
                Assert.InRange(c.Tier, 1, Progression.MaxTier);
            });
        }
    }

    [Fact]
    public void A_live_run_has_no_roll()
    {
        var (live, _) = RunTestKit.StepUntil(Live(7, 100000), 50);
        Assert.Null(CompletionRoll.ForRun(live));
    }

    [Theory]
    [InlineData(End.TimedClear)]
    [InlineData(End.UntimedClear)]
    [InlineData(End.Wipe)]
    public void The_roll_is_the_run_seeded_roll_the_panel_reads(End end)
    {
        // The reward seed (server/src/run-end.ts): run seed x 1000003 + the ending tick. The panel's roll (ForRun) is the
        // same pure roll the run-end phase emitted, over the same seed, key and pool.
        var run = Ended(end);
        var seed = unchecked((run.State.Seed * 1_000_003L) + run.RunEnd.ResultTick!.Value);
        Assert.Equal(seed, EndOfRun.RewardSeed(run));
        var expected = CompletionRoll.Roll(seed, run.Encounter.KeyLevel, run.RunEnd.Result!.Value, new[] { 1, 2 }, null, _ => slot => CompletionFacets.Fallback[slot]);
        var actual = CompletionRoll.ForRun(run)!.OrderBy(r => r.Seat).ToList();
        Assert.Equal(expected.Select(r => (r.Seat, r.Item?.Id)), actual.Select(r => (r.Seat, r.Item?.Id)));
    }

    [Theory]
    [InlineData(End.TimedClear)]
    [InlineData(End.UntimedClear)]
    [InlineData(End.Wipe)]
    public void The_item_won_goes_onto_the_character_and_survives_the_screen_save(End end)
    {
        // Migrated from "the pick": the first seed whose seat-1 roll wins an item (a roll can give nothing).
        Item? won = null;
        Run run = null!;
        for (var seed = 1L; seed <= 40 && won is null; seed++)
        {
            run = Ended(end, seed: seed);
            won = CompletionRoll.ForRun(run)!.Single(r => r.Seat == 1).Item;
        }
        Assert.NotNull(won);
        // The screen's order: bank at the run end (win or lose), then the won item goes on (Screen.Run.cs OnStepped).
        var character = Progression.Start(99).BankRun(run.RunEnd.Result!.Value).Take(won!);
        Assert.Equal(won, character.Equipped[won!.Slot]);
        Assert.Equal(won, character.LastRun!.Taken);
        var reloaded = ProgressionSave.Load(ProgressionSave.Serialize(character), 99);
        Assert.Null(reloaded.Error);
        Assert.Equal(won.Id, reloaded.Progression.Equipped[won.Slot].Id);
        Assert.Equal(won.Tier, reloaded.Progression.Equipped[won.Slot].Tier);
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

    [Theory]
    [InlineData(End.TimedClear)]
    [InlineData(End.UntimedClear)]
    [InlineData(End.Wipe)]
    public void A_character_at_the_cap_is_offered_exactly_one_ascension_card(End end)
    {
        // Every gear slot holds its first pool facet at tier 10: the cap. The offer a finished run shows must carry one
        // Ascension card (BIBLE s7 "After the cap: Ascension") and two other distinct cards, whatever the ending.
        var capped = Rewards.GearSlots.ToDictionary(
            s => s,
            s => new Item($"owned-{s}", s, Progression.MaxTier, Pools[s][0], 2),
            StringComparer.Ordinal);
        for (var seed = 1L; seed <= 12; seed++)
        {
            var run = Ended(end, keyLevel: 2 + (int)(seed % 19), seed: seed);
            var offer = ChoicesOf(run, capped);
            Assert.Equal(3, offer.Count);
            Assert.Equal(1, offer.Count(Ascension.IsCard));
            Assert.Equal(3, offer.Select(Key).Distinct(StringComparer.Ordinal).Count());
        }
    }

    [Fact]
    public void A_character_one_tier_below_the_cap_is_offered_no_ascension_card()
    {
        var almost = Rewards.GearSlots.ToDictionary(
            s => s,
            s => new Item($"owned-{s}", s, s == "ring" ? Progression.MaxTier - 1 : Progression.MaxTier, Pools[s][0], 2),
            StringComparer.Ordinal);
        for (var seed = 1L; seed <= 12; seed++)
        {
            var run = Ended(End.TimedClear, keyLevel: 2 + (int)(seed % 19), seed: seed);
            Assert.DoesNotContain(ChoicesOf(run, almost), Ascension.IsCard);
        }
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
        // Amended 2026-10-21 (U9, Ascension): a character at the cap in every slot is offered one Ascension card as the
        // third card (BIBLE s7 "After the cap"). The first two cards are still the plain roll, so the tier band stays a
        // function of the key: gear above the band adds no quality to the other cards.
        var gear = Rewards.GearSlots.ToDictionary(s => s, s => new Item($"owned-{s}", s, 10, Pools[s][0], 0), StringComparer.Ordinal);
        for (var seed = 0L; seed < 100; seed++)
        {
            var plain = Rewards.RollRewardChoices(seed, 4, RunResult.Timed, Rewards.GearSlots, Pools);
            var offer = Rewards.RollRewardChoices(seed, 4, RunResult.Timed, Rewards.GearSlots, Pools, gear);
            Assert.Equal(plain.Take(2).Select(i => i.Id), offer.Take(2).Select(i => i.Id));
            Assert.True(Ascension.IsCard(offer[2]));
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
        // One slot, three facets: exactly three distinct (slot, facet) cards exist, so the random draw collides often
        // and, on some seeds, more than the 20 re-draws allow. The cards must still be the three distinct facets.
        // (Before f-0b0afff2 this was one facet at key 4, tiers 1-3: three "distinct" cards that were one facet at
        // three tiers. BIBLE s7 "Facets change function" makes that a non-choice, so that pool now fails closed.)
        var three = new Dictionary<string, IReadOnlyList<Facet>>(StringComparer.Ordinal)
        {
            ["ring"] = new[] { new Facet("ring.a", "a", "x"), new Facet("ring.b", "b", "x"), new Facet("ring.c", "c", "x") },
        };
        for (var seed = 0L; seed < 20000; seed++)
        {
            var offer = Rewards.RollRewardChoices(seed, 4, RunResult.Timed, new[] { "ring" }, three);
            Assert.Equal(3, offer.Select(i => i.Facet.Id).Distinct(StringComparer.Ordinal).Count());
            Assert.Equal(3, offer.Select(Key).Distinct(StringComparer.Ordinal).Count());
        }
        var one = new Dictionary<string, IReadOnlyList<Facet>>(StringComparer.Ordinal) { ["ring"] = new[] { new Facet("ring.only", "only", "x") } };
        Assert.Throws<ArgumentException>(() => Rewards.RollRewardChoices(1, 4, RunResult.Timed, new[] { "ring" }, one));
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
