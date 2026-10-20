using System;
using System.Collections.Generic;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;

namespace Keyrun.Sim.Runs;

/// <summary>
/// Gear stats in the existing integer damage pipeline (BIBLE s7 "Tier is power"), in basis points: the wearer's
/// outgoing damage change and incoming damage change (negative reduces damage taken).
/// </summary>
/// <param name="DamageDoneBp">Added to the attacker's damageDoneBp on every hit it deals.</param>
/// <param name="DamageTakenBp">Added to the target's damageTakenBp on every hit it takes.</param>
public readonly record struct GearStats(long DamageDoneBp, long DamageTakenBp)
{
    /// <summary>Stat-wise sum.</summary>
    /// <param name="a">Left.</param>
    /// <param name="b">Right.</param>
    /// <returns>The sum.</returns>
    public static GearStats operator +(GearStats a, GearStats b) =>
        new(a.DamageDoneBp + b.DamageDoneBp, a.DamageTakenBp + b.DamageTakenBp);
}

/// <summary>
/// Sums the five equipped slots' stats (BIBLE s7 "Five slots") and puts the total on an actor. The stat table is the
/// host's (Keyrun.Content gear.json via <c>GearItems.StatsAt</c>); the sim takes it as a function so it never reads
/// content. Extension point for D32 (gear depth): a Facet that changes numbers would add to the same total.
/// </summary>
public static class GearPower
{
    /// <summary>The summed stats of every equipped item, in <see cref="Rewards.GearSlots"/> order; an empty slot adds nothing.</summary>
    /// <param name="equipped">Equipped items by slot.</param>
    /// <param name="statsAt">The stats of an item in (slot, tier).</param>
    /// <returns>The total.</returns>
    public static GearStats Sum(IReadOnlyDictionary<string, Item> equipped, Func<string, int, GearStats> statsAt)
    {
        ArgumentNullException.ThrowIfNull(equipped);
        ArgumentNullException.ThrowIfNull(statsAt);
        GearStats total = default;
        foreach (var slot in Rewards.GearSlots)
        {
            if (equipped.TryGetValue(slot, out var item))
            {
                total += statsAt(slot, item.Tier);
            }
        }
        return total;
    }

    /// <summary>Puts <paramref name="stats"/> on <paramref name="actor"/> (replacing any gear stats it had).</summary>
    /// <param name="actor">The wearer.</param>
    /// <param name="stats">The summed gear stats.</param>
    public static void Apply(ActorState actor, GearStats stats)
    {
        ArgumentNullException.ThrowIfNull(actor);
        actor.GearDamageDoneBp = stats.DamageDoneBp;
        actor.GearDamageTakenBp = stats.DamageTakenBp;
    }
}
