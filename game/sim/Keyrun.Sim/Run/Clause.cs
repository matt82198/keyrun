using System;
using System.Collections.Generic;

namespace Keyrun.Sim.Runs;

/// <summary>The party's Clause pick before queueing (BIBLE s8). RED stub.</summary>
public sealed record ClauseChoice(IReadOnlyList<string> Offers, string Picked)
{
    /// <summary>Picks an offered Clause, else null.</summary>
    public ClauseChoice? Pick(string clauseId) => throw new NotImplementedException();

    /// <summary>The run options carrying the one picked Clause.</summary>
    public RunOptions ToRunOptions() => throw new NotImplementedException();
}

/// <summary>The pick-of-three Clause offer (BIBLE s8). RED stub.</summary>
public static class ClausePick
{
    /// <summary>Clauses offered per pick.</summary>
    public const int OfferCount = 3;

    /// <summary>The offer seed for a dungeon and key.</summary>
    public static uint SeedFor(string dungeonId, int keyLevel) => throw new NotImplementedException();

    /// <summary>Three distinct ids drawn from <paramref name="clauseIds"/>.</summary>
    public static IReadOnlyList<string> Offer(IReadOnlyList<string> clauseIds, uint seed) => throw new NotImplementedException();

    /// <summary>The offer for a dungeon and key, first offer picked.</summary>
    public static ClauseChoice Open(IReadOnlyList<string> clauseIds, string dungeonId, int keyLevel) => throw new NotImplementedException();
}
