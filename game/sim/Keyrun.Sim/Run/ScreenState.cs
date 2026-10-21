using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Encounter;

namespace Keyrun.Sim.Runs;

/// <summary>
/// What the screen's Queue button hands the host: one dungeon, one key, one Clause, and the human's talented kit
/// (BIBLE s6: the talents picked on the screen are the abilities the run plays).
/// </summary>
/// <param name="DungeonId">The dungeon.</param>
/// <param name="KeyLevel">The key, 2-20.</param>
/// <param name="ClauseId">The one picked Clause.</param>
public sealed record QueueRequest(string DungeonId, int KeyLevel, string ClauseId)
{
    /// <summary>
    /// Actor id to that actor's talent-resolved abilities (by ability id), or null when nothing is picked. The host
    /// resolves the screen's picks into plain AbilityDefs (the sim never sees a ClassDef); this is the talent layer
    /// of <see cref="RunOptions.Loadouts"/>.
    /// </summary>
    public IReadOnlyDictionary<int, IReadOnlyDictionary<string, AbilityDef>>? Loadouts { get; init; }

    /// <summary>Actor id to that actor's summed gear stats (BIBLE s7), or null: <see cref="RunOptions.Gear"/>.</summary>
    public IReadOnlyDictionary<int, GearStats>? Gear { get; init; }

    /// <summary>The run options for the queued run: its one Clause and the queued loadouts.</summary>
    /// <returns>The options.</returns>
    public RunOptions ToRunOptions() => new() { ClauseId = ClauseId, Loadouts = Loadouts, Gear = Gear };
}

/// <summary>
/// The screen's Key band (BIBLE s10 "Key (stepper, Clause pick, locks on queue)"; s8 key 2-20): an immutable state the
/// Godot screen renders. Every refused action (a step or pick while locked, a pick of a Clause not offered) returns
/// this same instance, so a caller can tell a refusal by reference.
/// </summary>
/// <param name="DungeonId">The dungeon.</param>
/// <param name="ClauseIds">Every Clause id content defines.</param>
/// <param name="KeyLevel">The key, 2-20.</param>
/// <param name="Clause">The Clause offer for this key and the pick.</param>
/// <param name="Locked">True once queued.</param>
public sealed record ScreenState(string DungeonId, IReadOnlyList<string> ClauseIds, int KeyLevel, ClauseChoice Clause, bool Locked)
{
    /// <summary>
    /// The readiness line under the key stepper (BIBLE s10: "Ready" or a waiting reason; it never blocks Queue). Only
    /// the queue lock is known to the band today: a locked band waits on the run to start, an open one is ready to queue.
    /// </summary>
    public string ReadinessLine => Locked
        ? FormattableString.Invariant($"Queued at key {KeyLevel}: waiting for the run to start.")
        : FormattableString.Invariant($"Ready: key {KeyLevel} can be queued now.");

    /// <summary>Lowest key (BIBLE s8: "a number, 2-20 for the POC").</summary>
    public const int MinKey = 2;

    /// <summary>Highest key.</summary>
    public const int MaxKey = 20;

    /// <summary>Opens the band at <paramref name="keyLevel"/> (clamped to 2-20), unlocked, first Clause offer picked.</summary>
    /// <param name="dungeonId">The dungeon.</param>
    /// <param name="clauseIds">Every Clause id content defines.</param>
    /// <param name="keyLevel">The starting key.</param>
    /// <returns>The state.</returns>
    public static ScreenState Open(string dungeonId, IReadOnlyList<string> clauseIds, int keyLevel)
    {
        ArgumentNullException.ThrowIfNull(dungeonId);
        ArgumentNullException.ThrowIfNull(clauseIds);
        var key = Clamp(keyLevel);
        return new ScreenState(dungeonId, clauseIds, key, ClausePick.Open(clauseIds, dungeonId, key), false);
    }

    /// <summary>
    /// The key stepped by <paramref name="delta"/> (clamped). The Clause offer is redrawn for the new key; the pick is
    /// kept when the new key still offers it, else the first offer is picked (exactly one Clause, never none).
    /// </summary>
    /// <param name="delta">Steps, negative for down.</param>
    /// <returns>The new state, or this one when locked.</returns>
    public ScreenState StepKey(int delta)
    {
        if (Locked)
        {
            return this;
        }
        var key = Clamp((long)KeyLevel + delta);
        var offer = ClausePick.Open(ClauseIds, DungeonId, key);
        var clause = offer.Pick(Clause.Picked) ?? offer;
        return this with { KeyLevel = key, Clause = clause };
    }

    /// <summary>Picks one of the current offers.</summary>
    /// <param name="clauseId">The Clause id.</param>
    /// <returns>The new state, or this one when locked or the Clause is not offered.</returns>
    public ScreenState PickClause(string clauseId)
    {
        if (Locked || Clause.Pick(clauseId) is not { } picked)
        {
            return this;
        }
        return this with { Clause = picked };
    }

    /// <summary>
    /// Queues: locks the band and returns the request, or null (and this state) when already queued. The request
    /// carries <paramref name="loadouts"/> (the screen's resolved talent picks) so the run plays the talented kit; a
    /// null or empty map queues no loadout, and the run keeps the base abilities.
    /// </summary>
    /// <param name="loadouts">Actor id to that actor's talent-resolved abilities, or null.</param>
    /// <returns>The locked state and the request.</returns>
    public (ScreenState State, QueueRequest? Request) Queue(
        IReadOnlyDictionary<int, IReadOnlyDictionary<string, AbilityDef>>? loadouts = null)
    {
        if (Locked)
        {
            return (this, null);
        }
        var carried = loadouts is { Count: > 0 } ? loadouts : null;
        return (this with { Locked = true }, new QueueRequest(DungeonId, KeyLevel, Clause.Picked) { Loadouts = carried });
    }

    private static int Clamp(long key) => (int)Math.Min(MaxKey, Math.Max(MinKey, key));
}

/// <summary>
/// The private last-three-runs strip (BIBLE s4 "clear moves something visible"; s10 "time, result, and what the clear
/// moved"; the screen's own scope here is key level and timed-or-not, from the saved <see cref="Progression.History"/>).
/// Pure text, so the Godot strip only draws what this returns.
/// </summary>
public static class RunStripText
{
    /// <summary>How many of the most recent runs the strip shows.</summary>
    public const int Shown = 3;

    /// <summary>The strip lines, most recent first, at most <see cref="Shown"/>; empty when there are no runs.</summary>
    /// <param name="history">The saved runs, oldest first (<see cref="Progression.History"/>).</param>
    /// <returns>One line per shown run.</returns>
    public static IReadOnlyList<string> Lines(IReadOnlyList<RunRecord> history)
    {
        ArgumentNullException.ThrowIfNull(history);
        return history.Reverse().Take(Shown)
            .Select(r => FormattableString.Invariant($"Key {r.KeyLevel}, {Word(r.Result)}"))
            .ToList();
    }

    private static string Word(RunResult result) => result switch
    {
        RunResult.Timed => "timed",
        RunResult.Depleted => "depleted",
        RunResult.Abandoned => "abandoned",
        _ => throw new ArgumentOutOfRangeException(nameof(result), result, "unknown run result"),
    };
}
