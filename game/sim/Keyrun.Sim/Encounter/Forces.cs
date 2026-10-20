using System;
using System.Collections.Generic;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Encounter;

/// <summary>The Enemy Forces bar as the HUD shows it (BIBLE s9.2; Mythic+ enemy forces). Integers only.</summary>
/// <param name="Points">Points banked so far.</param>
/// <param name="Required">Points that fill the bar (100%).</param>
public sealed record ForcesReading(int Points, int Required)
{
    /// <summary>Banked share of the bar in basis points (10000 = 100%); can pass 10000 on an overpull.</summary>
    public int PercentBp => 0;

    /// <summary>True once the bar is full.</summary>
    public bool Full => false;

    /// <summary>The bar label.</summary>
    public string Label => string.Empty;

    /// <summary>The HUD proof line.</summary>
    public string LogLine() => string.Empty;
}

/// <summary>The Enemy Forces tally (stub).</summary>
public static class Forces
{
    /// <summary>Points banked by dead enemies (stub).</summary>
    public static int Points(SimState state, IReadOnlyDictionary<string, int> pointsByEnemy) => 0;

    /// <summary>The reading (stub).</summary>
    public static ForcesReading Read(SimState state, IReadOnlyDictionary<string, int> pointsByEnemy, int required) =>
        new(0, required);
}
