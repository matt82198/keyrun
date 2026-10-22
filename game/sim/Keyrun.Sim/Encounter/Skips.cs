using System;
using System.Collections.Generic;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Encounter;

/// <summary>How a pack the party left behind was left.</summary>
public enum SkipOutcome
{
    /// <summary>Never engaged.</summary>
    Skipped,

    /// <summary>Engaged, then dropped out of the engaged set (a leash).</summary>
    Leashed,
}

/// <summary>One recorded pack.</summary>
/// <param name="PackId">Group id.</param>
/// <param name="Tick">Tick of the record.</param>
/// <param name="PartyPos">Party centroid.</param>
/// <param name="Outcome">Skipped or leashed.</param>
/// <param name="ForcesForgone">Forces the pack's living members are worth.</param>
public sealed record SkipEntry(string PackId, int Tick, Vec3 PartyPos, SkipOutcome Outcome, int ForcesForgone);

/// <summary>The skip record.</summary>
/// <param name="EverEngaged">Groups that have ever engaged.</param>
/// <param name="Entries">Recorded packs.</param>
public sealed record SkipLedger(IReadOnlyList<string> EverEngaged, IReadOnlyList<SkipEntry> Entries)
{
    /// <summary>Nothing recorded.</summary>
    public static readonly SkipLedger Empty = new(Array.Empty<string>(), Array.Empty<SkipEntry>());
}

/// <summary>RED STUB: replaced by the real skip record in the green commit.</summary>
public static class Skips
{
    /// <summary>Stub.</summary>
    /// <param name="next">State.</param>
    /// <param name="encounter">Encounter.</param>
    /// <param name="groups">Groups.</param>
    /// <param name="layout">Layout.</param>
    /// <param name="goal">Goal.</param>
    /// <param name="openSeats">Open seats.</param>
    /// <param name="emit">Sink.</param>
    /// <returns>The encounter.</returns>
    public static EncounterState Step(SimState next, EncounterState encounter, IReadOnlyList<AggroGroup> groups, NavLayout layout, ForcesGoal? goal, IReadOnlyList<int>? openSeats, Action<SimEvent> emit) => encounter;

    /// <summary>Stub.</summary>
    /// <param name="e">Event.</param>
    /// <returns>Null.</returns>
    public static string? LogLine(SimEvent e) => null;
}
