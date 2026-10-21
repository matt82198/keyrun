using System;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;

namespace Keyrun.Hud;

/// <summary>
/// The private Ascension readout (BIBLE s7 "After the cap: Ascension ... Ascension marks on the character";
/// f-275d47c9), drawn on the between-runs screen's Character tab and on the run-end panel, out of the dungeon only,
/// as Dark and Darker's character screen does (Matt 2026-10-20). Every line is text over one
/// <see cref="AscensionState"/> from <see cref="Ascension.Of"/> on the character's own <see cref="Progression"/>, the
/// same object the run banks into, so the screen can never show a state the save does not hold. Private only: whether
/// another player may see it is BIBLE s12 [DECIDE] (open), so no line is ever sent to or drawn for another seat, and
/// every line says so. Godot-free (tested in Keyrun.Sim.Tests).
/// </summary>
public static class AscensionReadout
{
    /// <summary>The private-only suffix every drawn line ends with.</summary>
    public const string OnlyYou = "only you see this";

    /// <summary>
    /// The Character tab line: "Ascension: 3/5 slots at Tier 10  (only you see this)" before the cap, and
    /// "Ascended  -  2 Ascension marks (timed above +10)  (only you see this)" after it.
    /// </summary>
    /// <param name="state">The character's state.</param>
    /// <returns>The line.</returns>
    public static string CharacterLine(AscensionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return Standing(state) + "  (" + OnlyYou + ")";
    }

    /// <summary>
    /// The run-end panel line, read after the run is banked: "Ascension mark earned: timed +14  (2 marks, only you see
    /// this)" when this run earned one, else the same standing state as <see cref="CharacterLine"/>.
    /// </summary>
    /// <param name="state">The character's state after the bank.</param>
    /// <returns>The line.</returns>
    public static string RunEndLine(AscensionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.LastRunMarked && state.LastRun is { } run)
        {
            return FormattableString.Invariant($"Ascension mark earned: timed +{run.KeyLevel}  ({MarksText(state.Marks)}, {OnlyYou})");
        }
        return CharacterLine(state);
    }

    /// <summary>
    /// The proof line, "SCREEN ascension ascended=1 capped=5 marks=1 last=timed key=12 mark=1 private=1": every value
    /// the drawn lines are made of (<c>last=none key=-</c> before any run is recorded).
    /// </summary>
    /// <param name="prefix">"SCREEN" or "HUD runend".</param>
    /// <param name="state">The character's state.</param>
    /// <returns>The line.</returns>
    public static string LogLine(string prefix, AscensionState state)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        ArgumentNullException.ThrowIfNull(state);
        var last = state.LastRun is { } r ? r.Result.ToJs() : "none";
        var key = state.LastRun is { } k ? k.KeyLevel.ToString(System.Globalization.CultureInfo.InvariantCulture) : "-";
        return FormattableString.Invariant(
            $"{prefix} ascension ascended={(state.Ascended ? 1 : 0)} capped={state.SlotsAtCap} marks={state.Marks} last={last} key={key} mark={(state.LastRunMarked ? 1 : 0)} private=1");
    }

    private static string Standing(AscensionState state) => state.Ascended
        ? FormattableString.Invariant($"Ascended  -  {state.Marks} Ascension {(state.Marks == 1 ? "mark" : "marks")} (timed above +{Ascension.MarkKeyAbove})")
        : FormattableString.Invariant($"Ascension: {state.SlotsAtCap}/{Rewards.GearSlots.Count} slots at Tier {Progression.MaxTier}");

    private static string MarksText(int marks) => marks == 1 ? "1 mark" : FormattableString.Invariant($"{marks} marks");
}
