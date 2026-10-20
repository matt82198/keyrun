using System;
using System.Collections.Generic;
using System.Linq;

namespace Keyrun.Sim.Runs;

/// <summary>One row of the run-end "What got you" list: what one enemy kind did to one seat.</summary>
/// <param name="Seat">The player seat it hit.</param>
/// <param name="SourceId">The enemy actor id (after <see cref="CauseRows.MergeByName"/>, the lowest id of the kind).</param>
/// <param name="SourceName">The enemy's display name.</param>
/// <param name="Damage">Health the seat lost to it: the damage events' <c>healthDamage</c> (after absorb, capped at
/// health; Damage.cs <see cref="Combat.Damage.ResolveModified"/>).</param>
/// <param name="Hits">Damage events.</param>
/// <param name="KillingBlow">True when its hit downed the seat (the sim's <c>death</c> event).</param>
public sealed record CauseRow(int Seat, int SourceId, string SourceName, long Damage, int Hits, bool KillingBlow);

/// <summary>
/// The "What got you" fold (BIBLE s10 "Death is not a scolding: the run log privately shows each player what got
/// them", lane WHAT-GOT-YOU). The private run log keys entries by actor id, but the panel names them by class, so two
/// actors of one kind read as a duplicate row ("Choir Acolyte" twice). <see cref="MergeByName"/> folds the rows that
/// share a seat and a display name into one, so the player reads one row per enemy kind. Pure; no Godot.
/// </summary>
public static class CauseRows
{
    /// <summary>
    /// Merges rows that share a seat and a display name (ordinal): damage and hits summed, the killing blow kept if any
    /// actor of the kind landed it, the lowest source id kept. Ordered as the panel reads them: the killing blow first,
    /// then damage descending, then source id ascending.
    /// </summary>
    /// <param name="rows">Per-actor rows.</param>
    /// <returns>One row per (seat, name).</returns>
    public static IReadOnlyList<CauseRow> MergeByName(IEnumerable<CauseRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var merged = new List<CauseRow>();
        foreach (var row in rows)
        {
            var i = merged.FindIndex(m => m.Seat == row.Seat && string.Equals(m.SourceName, row.SourceName, StringComparison.Ordinal));
            if (i < 0)
            {
                merged.Add(row);
                continue;
            }
            var m = merged[i];
            merged[i] = m with
            {
                SourceId = Math.Min(m.SourceId, row.SourceId),
                Damage = m.Damage + row.Damage,
                Hits = m.Hits + row.Hits,
                KillingBlow = m.KillingBlow || row.KillingBlow,
            };
        }
        return merged
            .OrderByDescending(c => c.KillingBlow)
            .ThenByDescending(c => c.Damage)
            .ThenBy(c => c.SourceId)
            .ToList();
    }
}
