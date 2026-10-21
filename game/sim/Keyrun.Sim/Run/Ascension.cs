using System;
using System.Linq;
using Keyrun.Sim.Encounter;

namespace Keyrun.Sim.Runs;

/// <summary>
/// The character's Ascension as the screen and the run-end panel show it (<see cref="Ascension.Of"/>): one read of the
/// same <see cref="Progression"/> the run uses, never a second computation.
/// </summary>
/// <param name="Ascended"><see cref="Ascension.IsAscended"/>.</param>
/// <param name="SlotsAtCap">Gear slots holding an item at <see cref="Progression.MaxTier"/> (0-5).</param>
/// <param name="Marks"><see cref="Ascension.Marks"/>.</param>
/// <param name="LastRun">The last run in <see cref="Progression.History"/>, or null when none is recorded.</param>
public sealed record AscensionState(bool Ascended, int SlotsAtCap, int Marks, RunRecord? LastRun)
{
    /// <summary>True when the last recorded run earned a mark (<see cref="Ascension.IsMark"/>).</summary>
    public bool LastRunMarked => LastRun is { } r && Ascension.IsMark(r);
}

/// <summary>
/// BIBLE s7 "After the cap: Ascension. Higher keys, cosmetics, the key ladder, and Ascension marks on the character.
/// Power never moves again." A pure, read-only query over <see cref="Progression"/>: it reads the equipped gear and
/// the banked run history and returns a verdict, never a new <see cref="Progression"/>, so asking can neither reset
/// the character nor change a stat (Ascension grants no power, ever).
/// </summary>
/// <remarks>
/// Trigger (orchestrator decision, reversible): every one of the five <see cref="Rewards.GearSlots"/> holds an item at
/// <see cref="Progression.MaxTier"/>. An empty slot counts as below the cap.
/// Marks (f-275d47c9; STATE.md rule 5 "timed above key 10"; lane decision, reversible): one per run in
/// <see cref="Progression.History"/> that was timed, above key <see cref="MarkKeyAbove"/>, and played while already
/// ascended ("after the cap"). Distinct marks per affix and Clause are not modelled: the history records no affix or
/// Clause yet. Whether Ascension is visible to other players is BIBLE s12 [DECIDE] and is not modelled here: the HUD
/// shows it to the player only.
/// </remarks>
public static class Ascension
{
    /// <summary>A mark needs a timed key strictly above this level.</summary>
    public const int MarkKeyAbove = 10;

    /// <summary>True once every gear slot is filled at <see cref="Progression.MaxTier"/>.</summary>
    /// <param name="progression">The character.</param>
    /// <returns>Whether the character has ascended.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="progression"/> is null.</exception>
    public static bool IsAscended(Progression progression) => SlotsAtCap(progression) == Rewards.GearSlots.Count;

    /// <summary>Gear slots holding an item at <see cref="Progression.MaxTier"/>.</summary>
    /// <param name="progression">The character.</param>
    /// <returns>0 to the number of gear slots.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="progression"/> is null.</exception>
    public static int SlotsAtCap(Progression progression)
    {
        ArgumentNullException.ThrowIfNull(progression);
        return Rewards.GearSlots.Count(slot =>
            progression.Equipped.TryGetValue(slot, out var item) && item.Tier >= Progression.MaxTier);
    }

    /// <summary>True when <paramref name="run"/> earned a mark: timed, above key <see cref="MarkKeyAbove"/>, ascended.</summary>
    /// <param name="run">The recorded run.</param>
    /// <returns>Whether it is a mark.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="run"/> is null.</exception>
    public static bool IsMark(RunRecord run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return run.Ascended && run.Timed && run.KeyLevel > MarkKeyAbove;
    }

    /// <summary>The character's Ascension marks, counted from <see cref="Progression.History"/>.</summary>
    /// <param name="progression">The character.</param>
    /// <returns>The count.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="progression"/> is null.</exception>
    public static int Marks(Progression progression)
    {
        ArgumentNullException.ThrowIfNull(progression);
        return progression.History.Count(IsMark);
    }

    /// <summary>The whole readout for <paramref name="progression"/>, from the reads above.</summary>
    /// <param name="progression">The character.</param>
    /// <returns>The state.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="progression"/> is null.</exception>
    public static AscensionState Of(Progression progression)
    {
        ArgumentNullException.ThrowIfNull(progression);
        var last = progression.History.Count == 0 ? null : progression.History[^1];
        return new AscensionState(IsAscended(progression), SlotsAtCap(progression), Marks(progression), last);
    }
}
