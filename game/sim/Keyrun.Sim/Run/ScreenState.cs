using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Encounter;

namespace Keyrun.Sim.Runs;

/// <summary>
/// What the screen's Queue button hands the host: one dungeon, one key, the key's Clause, and the human's talented kit
/// (BIBLE s6: the talents picked on the screen are the abilities the run plays).
/// </summary>
/// <param name="DungeonId">The dungeon.</param>
/// <param name="KeyLevel">The key, 2-20.</param>
/// <param name="ClauseId">The key's Clause (BIBLE s8: the key sets it, <see cref="ClauseRotation.ForKey"/>), or null below the
/// key-set start key, where no Clause runs.</param>
public sealed record QueueRequest(string DungeonId, int KeyLevel, string? ClauseId)
{
    /// <summary>
    /// Actor id to that actor's talent-resolved abilities (by ability id), or null when nothing is picked. The host
    /// resolves the screen's picks into plain AbilityDefs (the sim never sees a ClassDef); this is the talent layer
    /// of <see cref="RunOptions.Loadouts"/>.
    /// </summary>
    public IReadOnlyDictionary<int, IReadOnlyDictionary<string, AbilityDef>>? Loadouts { get; init; }

    /// <summary>Actor id to that actor's summed gear stats (BIBLE s7), or null: <see cref="RunOptions.Gear"/>.</summary>
    public IReadOnlyDictionary<int, GearStats>? Gear { get; init; }

    /// <summary>The run options for the queued run: its key's Clause and the queued loadouts.</summary>
    /// <returns>The options.</returns>
    public RunOptions ToRunOptions() => new() { ClauseId = ClauseId, Loadouts = Loadouts, Gear = Gear };
}

/// <summary>
/// The screen's Key band (BIBLE s10 "Key (stepper, locks on queue)"; s8 key 2-20; R1 2026-10-21: the Clause is set by
/// the key, there is no pre-queue Clause pick): an immutable state the Godot screen renders. Every refused action (a
/// step while locked) returns this same instance, so a caller can tell a refusal by reference.
/// </summary>
/// <param name="DungeonId">The dungeon.</param>
/// <param name="ClauseIds">Every Clause id content defines.</param>
/// <param name="KeyLevel">The key, 2-20.</param>
/// <param name="RotationIndex">The host's affix rotation index, the same one the run is built with (the ISO week).</param>
/// <param name="ClauseStartKey">The first key that carries a Clause (content run.json affixes.clauseStartKey).</param>
/// <param name="Locked">True once queued.</param>
public sealed record ScreenState(
    string DungeonId,
    IReadOnlyList<string> ClauseIds,
    int KeyLevel,
    int RotationIndex,
    int ClauseStartKey,
    bool Locked)
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

    /// <summary>
    /// A start key no key reaches: the schedule is absent, so the key carries no Clause and Queue hands the host none.
    /// Callers pass it as the clause start key to <see cref="Open(string, IReadOnlyList{string}, int, int, int)"/> when there is no schedule.
    /// </summary>
    public const int NoClauseStartKey = MaxKey + 1;

    /// <summary>Opens the band at <paramref name="keyLevel"/> (clamped to 2-20), unlocked.</summary>
    /// <param name="dungeonId">The dungeon.</param>
    /// <param name="clauseIds">Every Clause id content defines.</param>
    /// <param name="keyLevel">The starting key.</param>
    /// <param name="rotationIndex">The host's affix rotation index (the one the run is built with).</param>
    /// <param name="clauseStartKey">The first key that carries a Clause (content run.json affixes.clauseStartKey).</param>
    /// <returns>The state.</returns>
    public static ScreenState Open(string dungeonId, IReadOnlyList<string> clauseIds, int keyLevel, int rotationIndex, int clauseStartKey)
    {
        ArgumentNullException.ThrowIfNull(dungeonId);
        ArgumentNullException.ThrowIfNull(clauseIds);
        return new ScreenState(dungeonId, clauseIds, Clamp(keyLevel), rotationIndex, clauseStartKey, false);
    }

    /// <summary>
    /// The Clause the key carries (BIBLE s8, read-only: <see cref="ClauseRotation.ForKey"/> over this band's key and
    /// rotation), or null below the start key. Nothing on the band sets it; stepping the key moves it.
    /// </summary>
    public string? KeyClause => ClauseRotation.ForKey(ClauseIds, KeyLevel, RotationIndex, ClauseStartKey);

    /// <summary>The key stepped by <paramref name="delta"/> (clamped). The key's Clause follows the new key.</summary>
    /// <param name="delta">Steps, negative for down.</param>
    /// <returns>The new state, or this one when locked.</returns>
    public ScreenState StepKey(int delta)
    {
        if (Locked)
        {
            return this;
        }
        return this with { KeyLevel = Clamp((long)KeyLevel + delta) };
    }

    /// <summary>
    /// Queues: locks the band and returns the request, or null (and this state) when already queued. The request
    /// carries the key's Clause and <paramref name="loadouts"/> (the screen's resolved talent picks) so the run plays
    /// the talented kit; a null or empty map queues no loadout, and the run keeps the base abilities.
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
        return (this with { Locked = true }, new QueueRequest(DungeonId, KeyLevel, KeyClause) { Loadouts = carried });
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
