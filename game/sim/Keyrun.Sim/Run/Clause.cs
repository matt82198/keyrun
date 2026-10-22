using System;
using System.Collections.Generic;
using System.Linq;

namespace Keyrun.Sim.Runs;

/// <summary>
/// The key-set Clause (BIBLE s8, Matt 2026-10-20 verbatim: "Clause = no pre-queue pick, the key sets it (rotating) and
/// Clauses start at a higher key (e.g. +5)"). Below the starting key no Clause runs; from it, exactly one Clause runs,
/// chosen by the host's rotation index over the ordinally sorted ids (like Xal'atath's affix), and it layers on top
/// of Fortified and Tyrannical (<see cref="Keyrun.Sim.Encounter.Affixes"/>). Two Clauses never stack: the result is
/// one id or none, and <see cref="Keyrun.Sim.Encounter.Clauses.ApplyClause"/> refuses a second.
/// </summary>
public static class ClauseRotation
{
    /// <summary>The Clause the key carries, or null below <paramref name="startKey"/>.</summary>
    /// <param name="clauseIds">Every Clause id content defines (order does not matter).</param>
    /// <param name="keyLevel">The key level.</param>
    /// <param name="rotationIndex">The host's rotation index (the ISO week); any integer.</param>
    /// <param name="startKey">The first key that carries a Clause (content run.json affixes.clauseStartKey).</param>
    /// <returns>The one Clause id, or null.</returns>
    /// <exception cref="ArgumentException">No Clause ids, or a duplicate id.</exception>
    public static string? ForKey(IReadOnlyList<string> clauseIds, int keyLevel, int rotationIndex, int startKey)
    {
        ArgumentNullException.ThrowIfNull(clauseIds);
        if (keyLevel < startKey)
        {
            return null;
        }
        var pool = clauseIds.Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToList();
        if (pool.Count == 0 || pool.Count != clauseIds.Count)
        {
            throw new ArgumentException($"a key-set Clause needs distinct ids, got [{string.Join(", ", clauseIds)}]", nameof(clauseIds));
        }
        var slot = ((rotationIndex % pool.Count) + pool.Count) % pool.Count;
        return pool[slot];
    }
}
