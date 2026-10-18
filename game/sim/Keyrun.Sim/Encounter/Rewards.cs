using System;
using System.Collections.Generic;
using System.Globalization;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Encounter;

/// <summary>A gear facet (rewards.ts:22 Facet).</summary>
/// <param name="Id">Facet id.</param>
/// <param name="Text">Display text.</param>
/// <param name="AbilityId">The ability it retags.</param>
public sealed record Facet(string Id, string Text, string AbilityId);

/// <summary>A rolled gear item (rewards.ts:28 Item).</summary>
/// <param name="Id">"reward-&lt;slot&gt;-&lt;facet&gt;-t&lt;tier&gt;-&lt;index&gt;".</param>
/// <param name="Slot">Gear slot: weapon, armor, charm, ring or keystone.</param>
/// <param name="Tier">Tier 1 to 10.</param>
/// <param name="Facet">Its facet.</param>
/// <param name="Charges">Crafting charges.</param>
public sealed record Item(string Id, string Slot, int Tier, Facet Facet, int Charges);

/// <summary>
/// End-of-run choice of three (sim/encounter/rewards.ts; BIBLE s7): a pure function of seed, key, result and owned
/// slots via the xoshiro128** PRNG. The tier band widens with the key; tiers 9-10 only on a timed clear at key 15+.
/// The three are never identical (slot, facet, tier), re-drawn up to 20 times on a collision.
/// </summary>
public static class Rewards
{
    /// <summary>The five gear slots, in order (rewards.ts:36).</summary>
    public static readonly IReadOnlyList<string> GearSlots = new[] { "weapon", "armor", "charm", "ring", "keystone" };

    /// <summary>Crafting charges a new item starts with (rewards.ts:39).</summary>
    public const int StartingCharges = 2;

    /// <summary>Minimum key, on a timed clear, that can drop tiers 9-10 (rewards.ts:41).</summary>
    public const int HardBisMinKey = 15;

    private const int MaxTier = 10;
    private const int SoftTierCap = 8;

    // rewards.ts:47 FACET_POOLS, verbatim.
    private static readonly IReadOnlyDictionary<string, Facet[]> FacetPools = new Dictionary<string, Facet[]>(StringComparer.Ordinal)
    {
        ["weapon"] = new[]
        {
            new Facet("weapon-edge", "Strike also applies Sundered to a second target", "ability-1"),
            new Facet("weapon-haft", "Strike's Window lasts 1s longer", "ability-1"),
            new Facet("weapon-brand", "Strike marks the target: next ally ability crits", "ability-1"),
        },
        ["armor"] = new[]
        {
            new Facet("armor-brace", "Brace also grants 10% damage reduction to one ally", "ability-2"),
            new Facet("armor-anchor", "Brace's Window radius is doubled", "ability-2"),
            new Facet("armor-ward", "Brace refunds its cost on a successful interrupt", "ability-2"),
        },
        ["charm"] = new[]
        {
            new Facet("charm-ember", "Heat builds 20% faster", "ability-3"),
            new Facet("charm-flare", "Detonate's Payoff also applies to adjacent Combos", "ability-3"),
            new Facet("charm-cinder", "Detonate no longer resets Heat on a failed push-your-luck", "ability-3"),
        },
        ["ring"] = new[]
        {
            new Facet("ring-verse", "Stance swaps cost no GCD", "ability-4"),
            new Facet("ring-chorus", "Verse effects also apply to the lowest-health ally", "ability-4"),
            new Facet("ring-coda", "The final Verse note deals bonus damage to Windowed targets", "ability-4"),
        },
        ["keystone"] = new[]
        {
            new Facet("keystone-echo", "Combos landed inside a Window grant bonus Ash", "ability-5"),
            new Facet("keystone-lock", "Interrupting a boss cast extends the next Window by 1s", "ability-5"),
            new Facet("keystone-tempo", "Slack decays 10% slower", "ability-5"),
        },
    };

    // rewards.ts:75 clampKey: round then clamp to [2, 20] (integer key: the round is the identity).
    private static int ClampKey(int keyLevel) => Math.Min(20, Math.Max(2, keyLevel));

    // rewards.ts:81 tierBand. key >= 2, so (key - 2) / 2 is a non-negative floor (ORACLE.md s8: rewards.ts:83).
    private static (int Min, int Max) TierBand(int keyLevel, RunResult result)
    {
        var key = ClampKey(keyLevel);
        var b = Math.Min(SoftTierCap, (int)IntMath.FloorDiv(key - 2, 2) + 1);
        var min = Math.Max(1, b - 1);
        var hardBis = result == RunResult.Timed && key >= HardBisMinKey;
        var max = hardBis ? MaxTier : Math.Min(SoftTierCap, b + 1);
        return (min, max);
    }

    // rewards.ts:90 drawItem: slot, then facet, then tier, three PRNG draws.
    private static (Item Item, PrngState State) DrawItem(PrngState state, IReadOnlyList<string> pool, (int Min, int Max) band, int suffix)
    {
        var (slotIdx, s1) = Prng.NextInt(state, pool.Count);
        var slot = pool[(int)slotIdx];
        var facets = FacetPools.TryGetValue(slot, out var f) ? f : throw new ArgumentException($"unknown gear slot \"{slot}\"", nameof(pool));
        var (facetIdx, s2) = Prng.NextInt(s1, facets.Length);
        var facet = facets[(int)facetIdx];
        var (tierRoll, s3) = Prng.NextInt(s2, band.Max - band.Min + 1);
        var tier = band.Min + (int)tierRoll;
        var id = string.Create(CultureInfo.InvariantCulture, $"reward-{slot}-{facet.Id}-t{tier}-{suffix}");
        return (new Item(id, slot, tier, facet, StartingCharges), s3);
    }

    private static string ItemKey(Item item) => string.Create(CultureInfo.InvariantCulture, $"{item.Slot}:{item.Facet.Id}:{item.Tier}");

    /// <summary>
    /// rewards.ts:119 rollRewardChoices: three distinct items. An empty <paramref name="slotsOwned"/> means all five
    /// slots. The seed goes through <see cref="Prng.SeedState(long)"/> (ToUint32, as the TS seedState).
    /// </summary>
    /// <exception cref="ArgumentException">An owned slot is not one of <see cref="GearSlots"/> (the TS would crash
    /// on an undefined facet pool).</exception>
    public static IReadOnlyList<Item> RollRewardChoices(long seed, int keyLevel, RunResult result, IReadOnlyList<string> slotsOwned)
    {
        var pool = slotsOwned.Count > 0 ? slotsOwned : GearSlots;
        var band = TierBand(keyLevel, result);
        var state = Prng.SeedState(seed);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var items = new List<Item>(3);
        for (var i = 0; i < 3; i++)
        {
            Item? chosen = null;
            for (var attempt = 0; attempt < 20; attempt++)
            {
                var (drawn, next) = DrawItem(state, pool, band, i);
                state = next;
                if (!seen.Contains(ItemKey(drawn)))
                {
                    chosen = drawn;
                    break;
                }
            }
            // rewards.ts:141: after 20 collisions, one more draw from the current state, kept even if a duplicate.
            var item = chosen ?? DrawItem(state, pool, band, i).Item;
            seen.Add(ItemKey(item));
            items.Add(item);
        }
        return items;
    }
}
