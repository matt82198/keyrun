using System;
using System.Collections.Generic;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;

namespace Keyrun.Hud;

/// <summary>
/// The run-end panel's per-card lines (BIBLE s7 "End of every run: choose one of three"; U10). An Ascension card
/// (<see cref="Ascension.IsCard"/>) is an Ascension mark, not a tier bonus: its only line is
/// <see cref="AscensionMarkLine"/>, and no "Tier N:" line is shown for it. Every other card keeps the lines its own
/// builder gives. Godot-free, so the Sim test project links this file and tests it.
/// </summary>
public static class RunEndCardLines
{
    /// <summary>The one line an Ascension card shows on the run-end panel.</summary>
    public const string AscensionMarkLine = "Ascension mark";

    /// <summary>
    /// The lines the run-end panel shows for <paramref name="card"/>: <see cref="AscensionMarkLine"/> alone for an
    /// Ascension card, otherwise whatever <paramref name="normalCardLines"/> gives for it (the content gear table's
    /// tier bonus line first, then the Facet text, via <c>GearItems.CardLines</c> at the call site).
    /// </summary>
    /// <param name="card">The card.</param>
    /// <param name="normalCardLines">Builds the lines of an ordinary card.</param>
    /// <returns>The card's lines.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="card"/> or <paramref name="normalCardLines"/> is null.</exception>
    public static IReadOnlyList<string> For(Item card, Func<Item, IReadOnlyList<string>> normalCardLines)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(normalCardLines);
        if (Ascension.IsCard(card))
        {
            return new[] { AscensionMarkLine };
        }
        return normalCardLines(card);
    }
}
