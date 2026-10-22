using System;
using System.Linq;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// f-08279d82 (D10 MVP line): the one pre-run screen's Character tab reads the pure <see cref="CharacterSheet"/>: the
/// class, the key it queues at, the Ascension state and the five gear slots with tier, Facet and the equipped item's
/// name (BIBLE s7). These tests take a real end-of-run choice of three (<see cref="Rewards.RollRewardChoices(long, int,
/// RunResult, System.Collections.Generic.IReadOnlyList{string}, System.Collections.Generic.IReadOnlyDictionary{string,
/// System.Collections.Generic.IReadOnlyList{Facet}}, System.Collections.Generic.IReadOnlyDictionary{string, Item})"/>),
/// equip it, and check the sheet shows it, through a save round trip, with the Tier-10 cap holding.
/// </summary>
public sealed class CharacterSheetTests
{
    private const string Dungeon = "hollow-vault";

    private static readonly ContentRegistry Registry = ContentLoader.Load();

    private static Lobby Fresh(Progression p, int key = 2) =>
        Lobby.Open(Registry, p, Dungeon, Registry.Clauses.Keys.ToList(), key, 0, ScreenState.NoClauseStartKey);

    private static Progression RoundTrip(Progression p)
    {
        var load = ProgressionSave.Load(ProgressionSave.Serialize(p), 99);
        Assert.Null(load.Error);
        return load.Progression;
    }

    [Fact(DisplayName = "Sheet: a fresh character shows the class, the key and five empty slots")]
    public void Fresh_character_has_five_empty_slots()
    {
        var p = Progression.Start(1);
        var sheet = CharacterSheet.Of(Registry, p, Fresh(p, key: 4));
        Assert.Equal("warden", sheet.ClassId);
        Assert.Equal("Warden", sheet.ClassName);
        Assert.Equal(4, sheet.KeyLevel);
        Assert.Equal(Rewards.GearSlots, sheet.Gear.Select(g => g.Slot));
        Assert.All(sheet.Gear, g => Assert.False(g.Equipped));
        Assert.Equal(0, sheet.Ascension.Marks);
        Assert.False(sheet.Ascension.Ascended);
    }

    [Fact(DisplayName = "Sheet: a reward taken from the choice of three shows on the Character tab and survives the save")]
    public void Choice_of_three_reward_is_shown_and_round_trips()
    {
        var p = Progression.Start(1).BankRun(RunResult.Timed, 6);
        var offer = Rewards.RollRewardChoices(7, 6, RunResult.Timed, Array.Empty<string>(), Lobby.FacetPools(Registry, "warden"), p.Equipped);
        Assert.Equal(3, offer.Count);
        var card = offer.First(c => !Ascension.IsCard(c));
        p = p.Take(card);

        var sheet = CharacterSheet.Of(Registry, p, Fresh(p));
        var row = sheet.Gear.Single(g => g.Slot == card.Slot);
        Assert.True(row.Equipped);
        Assert.Equal(card.Tier, row.Tier);
        Assert.Equal(card.Facet.Id, row.FacetId);
        Assert.Equal(card.Facet.Text, row.FacetText);
        Assert.Equal(card.Charges, row.Charges);
        Assert.True(row.FacetActive); // a Warden pool Facet on the Warden
        var expectedName = Registry.Gear.Values
            .Where(g => GearFacets.SlotId(g.Slot) == card.Slot && g.Tier <= card.Tier)
            .OrderByDescending(g => g.Tier).First().Name;
        Assert.Equal(expectedName, row.Name);
        Assert.False(string.IsNullOrEmpty(row.Name));
        Assert.All(sheet.Gear.Where(g => g.Slot != card.Slot), g => Assert.False(g.Equipped));

        // The save written at the take ("SCREEN save reason=take") reads back to the same sheet.
        var back = CharacterSheet.Of(Registry, RoundTrip(p), Fresh(p));
        Assert.Equal(sheet.Gear, back.Gear);
        Assert.Equal(sheet.Ascension, back.Ascension);
    }

    [Fact(DisplayName = "Sheet: tier is capped at 10 on the sheet and in the save, and never below 1")]
    public void Tier_cap_holds_on_the_sheet_and_in_the_save()
    {
        var facet = Lobby.FacetPools(Registry, "warden")["weapon"][0];
        var p = Progression.Start(1).Take(new Item("reward-weapon-t14-0", "weapon", 14, facet, Rewards.StartingCharges));
        p = p.Take(new Item("reward-ring-t0-0", "ring", 0, Lobby.FacetPools(Registry, "warden")["ring"][0], Rewards.StartingCharges));
        var sheet = CharacterSheet.Of(Registry, RoundTrip(p), Fresh(p));
        Assert.Equal(Progression.MaxTier, sheet.Gear.Single(g => g.Slot == "weapon").Tier);
        Assert.Equal(1, sheet.Gear.Single(g => g.Slot == "ring").Tier);
        Assert.All(sheet.Gear.Where(g => g.Equipped), g => Assert.InRange(g.Tier, 1, Progression.MaxTier));
    }

    [Fact(DisplayName = "Sheet: another class's Facet is marked inactive for the class picked on the Character tab")]
    public void Facet_of_another_class_is_inactive()
    {
        var facet = Lobby.FacetPools(Registry, "warden")["weapon"][0];
        var p = Progression.Start(1).Take(new Item("reward-weapon-t3-0", "weapon", 3, facet, Rewards.StartingCharges));
        var lobby = Fresh(p).PickClass(Registry, p, "cinder");
        var sheet = CharacterSheet.Of(Registry, p, lobby);
        Assert.Equal("cinder", sheet.ClassId);
        Assert.False(sheet.Gear.Single(g => g.Slot == "weapon").FacetActive);
    }

    [Fact(DisplayName = "Sheet: Ascension marks and the ascended flag are the character's own (Ascension.Of)")]
    public void Ascension_state_is_read_from_the_progression()
    {
        var p = Progression.Start(1);
        foreach (var slot in Rewards.GearSlots)
        {
            p = p.Take(new Item($"reward-{slot}-t10-0", slot, 10, Lobby.FacetPools(Registry, "warden")[slot][0], Rewards.StartingCharges));
        }
        p = p.BankRun(RunResult.Timed, 12);
        var sheet = CharacterSheet.Of(Registry, p, Fresh(p));
        Assert.Equal(Ascension.Of(p), sheet.Ascension);
        Assert.True(sheet.Ascension.Ascended);
        Assert.True(sheet.Ascension.Marks > 0);
    }

    [Fact(DisplayName = "Seats: the Queue tab lists the human's seat and the bot seats Queue fills, in actor order")]
    public void Seats_are_the_party_queue_fills()
    {
        var seats = CharacterSheet.Seats(Registry, "cinder", withBots: true);
        Assert.Equal(3, seats.Count);
        var human = seats.Single(s => s.Human);
        Assert.Equal("cinder", human.ClassId);
        Assert.Equal(HostRunSetup.HumanId, human.ActorId);
        Assert.Equal(new[] { "warden", "chanter" }, seats.Where(s => !s.Human).Select(s => s.ClassId));
        Assert.Equal(seats.OrderBy(s => s.ActorId), seats);
        Assert.Single(CharacterSheet.Seats(Registry, "warden", withBots: false));
    }
}
