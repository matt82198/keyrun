using System.Linq;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// BIBLE s7 "After the cap: Ascension ... Power never moves again." Ascension is a read-only query over
/// <see cref="Progression"/>: a character is ascended once every gear slot holds an item at
/// <see cref="Progression.MaxTier"/>, and asking never changes the character.
/// </summary>
public sealed class AscensionTests
{
    private static Item At(string slot, int tier) =>
        new($"reward-{slot}-edge-t{tier}-0", slot, tier, new Facet($"{slot}-edge", "Strike also applies Sundered", "ability-1"), Rewards.StartingCharges);

    private static Progression AllAt(int tier) =>
        Rewards.GearSlots.Aggregate(Progression.Start(7).BankRun(RunResult.Timed), (p, s) => p.Take(At(s, tier)));

    [Fact]
    public void AllSlotsAtTierTen_ReportsAscended()
    {
        Assert.True(Ascension.IsAscended(AllAt(Progression.MaxTier)));
    }

    [Fact]
    public void OneSlotBelowCap_NotAscended()
    {
        var oneBelow = AllAt(Progression.MaxTier).Take(At("ring", Progression.MaxTier - 1));
        Assert.False(Ascension.IsAscended(oneBelow));

        // An empty slot is below the cap too: four tier-10 items are not five.
        var fourOfFive = Rewards.GearSlots.Where(s => s != "keystone")
            .Aggregate(Progression.Start(7), (p, s) => p.Take(At(s, Progression.MaxTier)));
        Assert.False(Ascension.IsAscended(fourOfFive));

        Assert.False(Ascension.IsAscended(Progression.Start(7)));
    }

    [Fact]
    public void Ascended_GrantsNoPower()
    {
        var before = AllAt(Progression.MaxTier);
        var snapshot = before.Equipped.ToDictionary(kv => kv.Key, kv => kv.Value);
        var ash = before.AshTotal;

        Assert.True(Ascension.IsAscended(before));

        // Asking changed nothing: the same tiers, Facets and charges in every slot, the same Ash.
        Assert.Equal(snapshot.Count, before.Equipped.Count);
        foreach (var (slot, item) in snapshot)
        {
            Assert.Equal(item, before.Equipped[slot]);
        }
        Assert.Equal(ash, before.AshTotal);

        // And past the cap a higher reward still lands at tier 10: Ascension never lifts power.
        var after = before.Take(At("weapon", Progression.MaxTier + 5));
        Assert.True(Ascension.IsAscended(after));
        Assert.All(after.Equipped.Values, i => Assert.Equal(Progression.MaxTier, i.Tier));
    }
}
