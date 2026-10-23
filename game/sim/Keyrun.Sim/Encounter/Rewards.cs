using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
/// End-of-run choice of three (sim/encounter/rewards.ts; BIBLE s7): a pure function of seed, key and owned slots via
/// the xoshiro128** PRNG. The tier band widens with the key; tiers 9-10 at key 15+ whatever the run result (Matt
/// 2026-10-20, ledger f-baeb031a: loot quality comes from key level, the result pays through the key jump; a
/// documented break from the frozen rewards.ts, which opened tiers 9-10 only on a timed clear).
/// The three never share a (slot, facet), not even at two tiers (BIBLE s7 "Facets change function", f-0b0afff2): a
/// facet already on offer is re-drawn up to 20 times. The frozen rewards.ts only kept (slot, facet, tier) distinct.
/// </summary>
public static class Rewards
{
    /// <summary>The five gear slots, in order (rewards.ts:36).</summary>
    public static readonly IReadOnlyList<string> GearSlots = new[] { "weapon", "armor", "charm", "ring", "keystone" };

    /// <summary>Crafting charges a new item starts with (rewards.ts:39).</summary>
    public const int StartingCharges = 2;

    /// <summary>The Facet id an Ascension card carries (BIBLE s7 "After the cap: Ascension"); no gear Facet uses it.</summary>
    public const string AscensionFacetId = "ascension";

    // The Ascension card's text. It grants no power: taking it adds a mark and changes no gear (Ascension.cs).
    private const string AscensionFacetText = "Ascension: an Ascension mark on the character; power never moves";

    /// <summary>Minimum key that can drop tiers 9-10, for any run result (rewards.ts:41; f-baeb031a).</summary>
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
    // f-baeb031a: a function of key level only; rewards.ts:85 also required a timed result for tiers 9-10.
    internal static (int Min, int Max) TierBand(int keyLevel)
    {
        var key = ClampKey(keyLevel);
        var b = Math.Min(SoftTierCap, (int)IntMath.FloorDiv(key - 2, 2) + 1);
        var min = Math.Max(1, b - 1);
        var hardBis = key >= HardBisMinKey;
        var max = hardBis ? MaxTier : Math.Min(SoftTierCap, b + 1);
        return (min, max);
    }

    // The placeholder lookup (rewards.ts:93 FACET_POOLS[slot]); an unknown slot throws exactly as before.
    private static IReadOnlyList<Facet> PlaceholderPool(string slot) =>
        FacetPools.TryGetValue(slot, out var f) ? f : throw new ArgumentException($"unknown gear slot \"{slot}\"", "pool");

    /// <summary>The placeholder facet pool for a slot (rewards.ts:47 FACET_POOLS); throws on an unknown slot.</summary>
    /// <param name="slot">A gear slot.</param>
    internal static IReadOnlyList<Facet> PlaceholderFacets(string slot) => PlaceholderPool(slot);

    // rewards.ts:90 drawItem: slot, then facet, then tier, three PRNG draws. The facet is drawn over the list
    // facetsOf returns for the slot (the placeholder pools, or a caller-supplied content pool).
    internal static (Item Item, PrngState State) DrawItem(PrngState state, IReadOnlyList<string> pool, Func<string, IReadOnlyList<Facet>> facetsOf, (int Min, int Max) band, int suffix)
    {
        var (slotIdx, s1) = Prng.NextInt(state, pool.Count);
        var slot = pool[(int)slotIdx];
        var facets = facetsOf(slot);
        var (facetIdx, s2) = Prng.NextInt(s1, facets.Count);
        var facet = facets[(int)facetIdx];
        var (tierRoll, s3) = Prng.NextInt(s2, band.Max - band.Min + 1);
        var tier = band.Min + (int)tierRoll;
        var id = string.Create(CultureInfo.InvariantCulture, $"reward-{slot}-{facet.Id}-t{tier}-{suffix}");
        return (new Item(id, slot, tier, facet, StartingCharges), s3);
    }

    // A card's identity for the choice of three is its (slot, facet), never its tier (BIBLE s7 "Facets change
    // function"; f-0b0afff2): the same facet at two tiers is a choice between numbers, not functions.
    private static string FacetKey(Item item) => string.Create(CultureInfo.InvariantCulture, $"{item.Slot}:{item.Facet.Id}");

    /// <summary>
    /// rewards.ts:119 rollRewardChoices: three distinct items. An empty <paramref name="slotsOwned"/> means all five
    /// slots. The seed goes through <see cref="Prng.SeedState(long)"/> (ToUint32, as the TS seedState).
    /// </summary>
    /// <exception cref="ArgumentException">An owned slot is not one of <see cref="GearSlots"/> (the TS would crash
    /// on an undefined facet pool).</exception>
    public static IReadOnlyList<Item> RollRewardChoices(long seed, int keyLevel, RunResult result, IReadOnlyList<string> slotsOwned) =>
        Roll(seed, keyLevel, slotsOwned, PlaceholderPool);

    /// <summary>
    /// The same roll over a caller-supplied facet pool (lane 3C-c; NEW behaviour, no TS oracle: the TS sim is frozen
    /// with the placeholder pools). Draw order, seed discipline, tier band, choice count and the collision re-draw
    /// are exactly <see cref="RollRewardChoices(long, int, RunResult, IReadOnlyList{string})"/>'s; only the facet
    /// list each slot draws over is <paramref name="facetPools"/>[slot]. The host passes the content facet table
    /// scoped to the party class (the sim takes the pool, it never reads class state or Keyrun.Content), so a card's
    /// <see cref="Item.Id"/> embeds the real facet id ("reward-weapon-warden.weapon.keenEdge-t3-0") and its
    /// <see cref="Facet.AbilityId"/> is the content ability id the HUD resolves.
    /// </summary>
    /// <param name="seed">Run-end seed (through <see cref="Prng.SeedState(long)"/>).</param>
    /// <param name="keyLevel">Key level.</param>
    /// <param name="result">Run result. It does not change the roll (f-baeb031a: the tier band is a function of the key
    /// level only); kept so callers state the result the cards are for.</param>
    /// <param name="slotsOwned">Slots to draw from; empty means all five.</param>
    /// <param name="facetPools">Facets per gear slot, in the order the draw indexes them.</param>
    /// <returns>Three items.</returns>
    /// <exception cref="ArgumentException">A drawn slot has no entry or an empty list in
    /// <paramref name="facetPools"/>.</exception>
    public static IReadOnlyList<Item> RollRewardChoices(long seed, int keyLevel, RunResult result, IReadOnlyList<string> slotsOwned, IReadOnlyDictionary<string, IReadOnlyList<Facet>> facetPools)
    {
        ArgumentNullException.ThrowIfNull(facetPools);
        IReadOnlyList<Facet> FacetsOf(string slot) =>
            facetPools.TryGetValue(slot, out var f) && f.Count > 0
                ? f
                : throw new ArgumentException($"no facets for gear slot \"{slot}\"", nameof(facetPools));
        return Roll(seed, keyLevel, slotsOwned, FacetsOf);
    }

    /// <summary>
    /// The content-pool roll with BIBLE s7's "every clear moves something visible" guarantee (Matt 2026-10-20:
    /// "choose-one-of-three always includes an upgrade"; STATE.md D32 increment 2): when none of the three cards
    /// <see cref="RollRewardChoices(long, int, RunResult, IReadOnlyList{string}, IReadOnlyDictionary{string, IReadOnlyList{Facet}})"/>
    /// draws is a strict upgrade over <paramref name="equipped"/> (<see cref="IsUpgrade"/>), the third card is replaced
    /// by the first strict upgrade in slot order then pool order: an empty slot at the drawn card's tier, else the
    /// lowest higher tier inside the key's band, else a Facet the slot does not hold at the tier it holds. A roll that
    /// already holds an upgrade is returned exactly as drawn, so a fresh character sees the same cards as before. The
    /// tier never leaves the key's band (loot quality comes from the key level): gear above the band gets the plain
    /// roll, and so does a character below the cap with nothing left to upgrade at this key. Only the cap earns an
    /// Ascension card: when every slot the roll draws over holds a tier-10 item, the third card is an Ascension card
    /// (<see cref="WithAscensionCard"/>) and the upgrade search is skipped.
    /// </summary>
    /// <param name="seed">Run-end seed.</param>
    /// <param name="keyLevel">Key level.</param>
    /// <param name="result">Run result (does not change the roll).</param>
    /// <param name="slotsOwned">Slots to draw from; empty means all five.</param>
    /// <param name="facetPools">Facets per gear slot.</param>
    /// <param name="equipped">The character's equipped gear by slot.</param>
    /// <returns>Three distinct items, at least one a strict upgrade whenever the key's band allows one.</returns>
    /// <exception cref="ArgumentException">A drawn slot has no facets, or the pool cannot make three distinct cards.</exception>
    public static IReadOnlyList<Item> RollRewardChoices(long seed, int keyLevel, RunResult result, IReadOnlyList<string> slotsOwned, IReadOnlyDictionary<string, IReadOnlyList<Facet>> facetPools, IReadOnlyDictionary<string, Item> equipped)
    {
        ArgumentNullException.ThrowIfNull(equipped);
        var items = RollRewardChoices(seed, keyLevel, result, slotsOwned, facetPools);
        if (CappedAt(slotsOwned, equipped))
        {
            return WithAscensionCard(items, slotsOwned, equipped);
        }
        if (items.Any(i => IsUpgrade(i, equipped)))
        {
            return items;
        }
        var band = TierBand(keyLevel);
        var taken = new HashSet<string>(StringComparer.Ordinal) { FacetKey(items[0]), FacetKey(items[1]) };
        foreach (var slot in slotsOwned.Count > 0 ? slotsOwned : GearSlots)
        {
            if (!facetPools.TryGetValue(slot, out var facets) || facets.Count == 0)
            {
                continue;
            }
            var owned = equipped.TryGetValue(slot, out var o) ? o : null;
            int tier;
            if (owned is null)
            {
                tier = items[2].Tier;
            }
            else if (Math.Max(band.Min, owned.Tier + 1) <= band.Max)
            {
                tier = Math.Max(band.Min, owned.Tier + 1);
            }
            else if (owned.Tier >= band.Min && owned.Tier <= band.Max)
            {
                tier = owned.Tier;
            }
            else
            {
                continue;
            }
            foreach (var facet in facets)
            {
                var card = new Item(string.Create(CultureInfo.InvariantCulture, $"reward-{slot}-{facet.Id}-t{tier}-2"), slot, tier, facet, StartingCharges);
                if (IsUpgrade(card, equipped) && !taken.Contains(FacetKey(card)))
                {
                    return new[] { items[0], items[1], card };
                }
            }
        }
        return items;
    }

    /// <summary>
    /// The Ascension offer (BIBLE s7 "After the cap: Ascension"; Matt 2026-10-21 "choose one of three"). When every slot
    /// <paramref name="slotsOwned"/> draws over (all five when empty) holds an item at tier 10 (<see cref="MaxTier"/>),
    /// the third card is replaced by one Ascension card: tier 10, in the first draw slot, with the facet
    /// <see cref="AscensionFacetId"/> and the held item's ability. The first two cards stay as drawn. Below the cap,
    /// <paramref name="items"/> is returned unchanged.
    /// </summary>
    /// <param name="items">The three cards the roll drew.</param>
    /// <param name="slotsOwned">Slots the roll draws over; empty means all five.</param>
    /// <param name="equipped">The character's equipped gear by slot.</param>
    /// <returns>The three cards, the third an Ascension card once capped.</returns>
    /// <exception cref="ArgumentException"><paramref name="items"/> is not three cards.</exception>
    public static IReadOnlyList<Item> WithAscensionCard(IReadOnlyList<Item> items, IReadOnlyList<string> slotsOwned, IReadOnlyDictionary<string, Item> equipped)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(slotsOwned);
        ArgumentNullException.ThrowIfNull(equipped);
        if (items.Count != 3)
        {
            throw new ArgumentException("an offer is three cards", nameof(items));
        }
        if (!CappedAt(slotsOwned, equipped))
        {
            return items;
        }
        var slot = (slotsOwned.Count > 0 ? slotsOwned : GearSlots)[0];
        var held = equipped[slot];
        var facet = new Facet(AscensionFacetId, AscensionFacetText, held.Facet.AbilityId);
        var card = new Item(string.Create(CultureInfo.InvariantCulture, $"reward-{slot}-{AscensionFacetId}-t{MaxTier}-2"), slot, MaxTier, facet, held.Charges);
        return new[] { items[0], items[1], card };
    }

    // True when every slot the roll draws over holds an item at the cap (an empty slot is below it).
    private static bool CappedAt(IReadOnlyList<string> slotsOwned, IReadOnlyDictionary<string, Item> equipped)
    {
        var pool = slotsOwned.Count > 0 ? slotsOwned : GearSlots;
        return pool.All(slot => equipped.TryGetValue(slot, out var item) && item.Tier >= MaxTier);
    }

    /// <summary>
    /// True when taking <paramref name="card"/> is a strict upgrade for the character (BIBLE s7 "a higher tier in some
    /// slot, or a Facet the character does not own yet"): its slot is empty, or it is a higher tier than the slot
    /// holds, or it is a Facet the slot does not hold at the same tier. Taking a reward replaces the slot's item (no
    /// armory yet, STATE.md D32 increment 3), so a new Facet at a lower tier trades power away and is not counted.
    /// </summary>
    /// <param name="card">The card.</param>
    /// <param name="equipped">The character's equipped gear by slot.</param>
    /// <returns>Whether it is a strict upgrade.</returns>
    public static bool IsUpgrade(Item card, IReadOnlyDictionary<string, Item> equipped)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(equipped);
        // An Ascension card changes no gear (its Facet is a mark, not a function), so it is never an upgrade.
        if (card.Facet.Id == AscensionFacetId)
        {
            return false;
        }
        if (!equipped.TryGetValue(card.Slot, out var owned))
        {
            return true;
        }
        return card.Tier > owned.Tier || (card.Tier == owned.Tier && !string.Equals(card.Facet.Id, owned.Facet.Id, StringComparison.Ordinal));
    }

    private static IReadOnlyList<Item> Roll(long seed, int keyLevel, IReadOnlyList<string> slotsOwned, Func<string, IReadOnlyList<Facet>> facetsOf)
    {
        var pool = slotsOwned.Count > 0 ? slotsOwned : GearSlots;
        var band = TierBand(keyLevel);
        var state = Prng.SeedState(seed);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var items = new List<Item>(3);
        for (var i = 0; i < 3; i++)
        {
            Item? chosen = null;
            for (var attempt = 0; attempt < 20; attempt++)
            {
                var (drawn, next) = DrawItem(state, pool, facetsOf, band, i);
                state = next;
                // Draw without replacement over (slot, facet): a facet already on offer is re-drawn whatever its tier
                // (f-0b0afff2). A roll with no facet repeat consumes exactly the stream it always did, so its cards are
                // unchanged; only a roll that used to offer one facet at two tiers re-draws that card.
                if (!seen.Contains(FacetKey(drawn)))
                {
                    chosen = drawn;
                    break;
                }
            }
            // rewards.ts:141: after 20 collisions, one more draw from the current state. The frozen TS kept it even as
            // a duplicate; BIBLE s7 offers three distinct choices, so a repeated facet falls to the first unseen
            // (slot, facet) in slot then pool order at the drawn tier, and a pool with fewer than three distinct
            // facets fails closed.
            var item = chosen ?? DrawItem(state, pool, facetsOf, band, i).Item;
            if (seen.Contains(FacetKey(item)))
            {
                item = FirstUnseen(pool, facetsOf, item.Tier, i, seen)
                    ?? throw new ArgumentException("the pool cannot offer three distinct facets", nameof(slotsOwned));
            }
            seen.Add(FacetKey(item));
            items.Add(item);
        }
        return items;
    }

    private static Item? FirstUnseen(IReadOnlyList<string> pool, Func<string, IReadOnlyList<Facet>> facetsOf, int tier, int suffix, HashSet<string> seen)
    {
        foreach (var slot in pool)
        {
            foreach (var facet in facetsOf(slot))
            {
                var item = new Item(string.Create(CultureInfo.InvariantCulture, $"reward-{slot}-{facet.Id}-t{tier}-{suffix}"), slot, tier, facet, StartingCharges);
                if (!seen.Contains(FacetKey(item)))
                {
                    return item;
                }
            }
        }
        return null;
    }
}
