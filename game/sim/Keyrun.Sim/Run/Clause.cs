using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Runs;

/// <summary>
/// The party's Clause pick before queueing (BIBLE s8): three offers and the one picked. There is no "none": a choice
/// always holds exactly one offered Clause, so exactly one Clause runs.
/// </summary>
/// <param name="Offers">The three offered Clause ids, in offer order.</param>
/// <param name="Picked">The picked id; always one of <paramref name="Offers"/>.</param>
public sealed record ClauseChoice(IReadOnlyList<string> Offers, string Picked)
{
    /// <summary>The choice with <paramref name="clauseId"/> picked, or null when it is not one of the offers.</summary>
    /// <param name="clauseId">The Clause id.</param>
    /// <returns>The new choice, or null (refused).</returns>
    public ClauseChoice? Pick(string clauseId) =>
        Offers.Contains(clauseId, StringComparer.Ordinal) ? this with { Picked = clauseId } : null;

    /// <summary>
    /// The run options for this pick: <see cref="RunOptions.ClauseId"/> is the one picked Clause, which
    /// <see cref="RunOptions.ResolveEncounterRules"/> folds onto the run's rules.
    /// </summary>
    /// <returns>The options.</returns>
    public RunOptions ToRunOptions() => new() { ClauseId = Picked };
}

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

/// <summary>
/// The pick-of-three Clause offer (BIBLE s8; the frozen TS screen/src/clauses.ts pickClauseOffers): a pure function of
/// the dungeon and key level through the xoshiro128** PRNG, so the same key always offers the same three and the
/// whole party sees one offer. The draw is a partial Fisher-Yates over the ordinally sorted ids (bounded, no retry
/// loop), so the result does not depend on the order content lists the Clauses in.
/// </summary>
public static class ClausePick
{
    /// <summary>Clauses offered per pick (BIBLE s8 "offered three").</summary>
    public const int OfferCount = 3;

    /// <summary>The offer seed: FNV-1a of "&lt;dungeonId&gt;:&lt;keyLevel&gt;".</summary>
    /// <param name="dungeonId">The dungeon id (ASCII).</param>
    /// <param name="keyLevel">The key level.</param>
    /// <returns>The seed.</returns>
    public static uint SeedFor(string dungeonId, int keyLevel)
    {
        ArgumentNullException.ThrowIfNull(dungeonId);
        return Fnv1a.Hash(string.Create(CultureInfo.InvariantCulture, $"{dungeonId}:{keyLevel}"));
    }

    /// <summary>Three distinct ids drawn from <paramref name="clauseIds"/> with the PRNG seeded by <paramref name="seed"/>.</summary>
    /// <param name="clauseIds">Every Clause id content defines.</param>
    /// <param name="seed">The offer seed (<see cref="SeedFor"/>).</param>
    /// <returns>The three offers, in draw order.</returns>
    /// <exception cref="ArgumentException">Fewer than three distinct ids (an offer of three cannot be made).</exception>
    public static IReadOnlyList<string> Offer(IReadOnlyList<string> clauseIds, uint seed)
    {
        ArgumentNullException.ThrowIfNull(clauseIds);
        var pool = clauseIds.Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToList();
        if (pool.Count < OfferCount || pool.Count != clauseIds.Count)
        {
            throw new ArgumentException($"a Clause offer needs {OfferCount} distinct ids, got [{string.Join(", ", clauseIds)}]", nameof(clauseIds));
        }
        var state = Prng.SeedState(seed);
        var offers = new List<string>(OfferCount);
        for (var i = 0; i < OfferCount; i++)
        {
            var (j, next) = Prng.NextInt(state, pool.Count - i);
            state = next;
            var pick = i + (int)j;
            (pool[i], pool[pick]) = (pool[pick], pool[i]);
            offers.Add(pool[i]);
        }
        return offers;
    }

    /// <summary>The offer for <paramref name="dungeonId"/> at <paramref name="keyLevel"/>, with the first offer picked.</summary>
    /// <param name="clauseIds">Every Clause id content defines.</param>
    /// <param name="dungeonId">The dungeon id.</param>
    /// <param name="keyLevel">The key level.</param>
    /// <returns>The choice.</returns>
    public static ClauseChoice Open(IReadOnlyList<string> clauseIds, string dungeonId, int keyLevel)
    {
        var offers = Offer(clauseIds, SeedFor(dungeonId, keyLevel));
        return new ClauseChoice(offers, offers[0]);
    }
}
