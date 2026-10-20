using System;
using System.Collections.Generic;

namespace Keyrun.Content;

/// <summary>
/// A gear item's stats in the existing integer damage pipeline, basis points (BIBLE s7 "Tier is power"). An omitted
/// stat is 0.
/// </summary>
public sealed record GearStatsDef
{
    /// <summary>Added to the wearer's damageDoneBp (never negative).</summary>
    public long DamageDoneBp { get; init; }

    /// <summary>Added to the wearer's damageTakenBp (never positive: gear never makes the wearer take more).</summary>
    public long DamageTakenBp { get; init; }
}

/// <summary>
/// A base gear item (game/content/data/gear.json, C#-only; BIBLE s7 "Five slots"): its slot, the tier it is defined at,
/// its stats at that tier and the step it gains per tier above it. Every slot has a tier-1 base; a later item for the
/// same slot at a higher tier re-bases the curve from that tier. Facets stay on the Facet table (facets/*.json): this
/// item carries numbers only (D32 decides facet depth).
/// </summary>
public sealed record GearItemDef : IContentItem
{
    /// <inheritdoc />
    public required string Id { get; init; }

    /// <summary>The slot.</summary>
    public required GearSlot Slot { get; init; }

    /// <summary>The tier these stats are at, 1..10.</summary>
    public required int Tier { get; init; }

    /// <summary>Display name.</summary>
    public required string Name { get; init; }

    /// <summary>Stats at <see cref="Tier"/>.</summary>
    public required GearStatsDef Stats { get; init; }

    /// <summary>Added once per tier above <see cref="Tier"/>.</summary>
    public required GearStatsDef PerTier { get; init; }
}

/// <summary>The gear table's lookups (BIBLE s7 "Tier is power and it is capped at 10").</summary>
public static class GearItems
{
    /// <summary>The tier cap (BIBLE s7 "Ten is the end").</summary>
    public const int MaxTier = 10;

    /// <summary>
    /// The stats of an item in <paramref name="slot"/> at <paramref name="tier"/>: the slot's item with the highest
    /// defined tier not above <paramref name="tier"/>, plus its per-tier step for each tier above that.
    /// </summary>
    /// <param name="gear">The gear table (<see cref="ContentRegistry.Gear"/>).</param>
    /// <param name="slot">The slot.</param>
    /// <param name="tier">The tier, 1..10.</param>
    /// <returns>The stats.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The tier is outside 1..10.</exception>
    public static GearStatsDef StatsAt(IReadOnlyDictionary<string, GearItemDef> gear, GearSlot slot, int tier)
    {
        ArgumentNullException.ThrowIfNull(gear);
        if (tier < 1 || tier > MaxTier) throw new ArgumentOutOfRangeException(nameof(tier), tier, "tier must be 1.." + MaxTier);
        GearItemDef? best = null;
        foreach (var item in gear.Values)
        {
            if (item.Slot == slot && item.Tier <= tier && (best is null || item.Tier > best.Tier)) best = item;
        }
        if (best is null) throw new ArgumentException("no gear item for slot " + GearFacets.SlotId(slot) + " at or below tier " + tier, nameof(gear));
        long steps = tier - best.Tier;
        return new GearStatsDef
        {
            DamageDoneBp = best.Stats.DamageDoneBp + (steps * best.PerTier.DamageDoneBp),
            DamageTakenBp = best.Stats.DamageTakenBp + (steps * best.PerTier.DamageTakenBp),
        };
    }
}
