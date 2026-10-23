using System;
using System.Collections.Generic;
using static Keyrun.Content.ContentValidator;

namespace Keyrun.Content;

/// <summary>
/// The end-of-run completion roll's table (game/content/data/loot/completion.json; C#-only, R-ROLL-1, BIBLE s1): one
/// roll per party seat, dropping an item with <see cref="DropBpTimed"/> on a timed clear and <see cref="DropBpDepleted"/>
/// on a depleted one (basis points of 10000), the item's slot drawn over <see cref="Slots"/>.
/// </summary>
public sealed record CompletionTable
{
    private static readonly string[] GearSlotNames = { "weapon", "armor", "charm", "ring", "keystone" };

    /// <summary>One seat's drop chance on a timed clear, basis points in [0, 10000].</summary>
    public required long DropBpTimed { get; init; }

    /// <summary>One seat's drop chance on a depleted clear, basis points in [0, 10000].</summary>
    public required long DropBpDepleted { get; init; }

    /// <summary>The five gear slots, each listed once, in order.</summary>
    public required ValueList<string> Slots { get; init; }

    /// <summary>
    /// Fails closed on a bad table: each drop rate in [0, 10000], and the slots exactly the five gear slots, each once.
    /// Called by <see cref="ContentLoader"/> beside <see cref="ContentValidator.Validate"/>; kept off the rule list, which
    /// is pinned by the registry tests.
    /// </summary>
    /// <param name="table">The loaded table.</param>
    internal static void Check(CompletionTable table)
    {
        if (table.DropBpTimed < 0 || table.DropBpTimed > 10000)
            Fail("completion dropBpTimed must be in [0, 10000], got " + table.DropBpTimed);
        if (table.DropBpDepleted < 0 || table.DropBpDepleted > 10000)
            Fail("completion dropBpDepleted must be in [0, 10000], got " + table.DropBpDepleted);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string slot in table.Slots)
        {
            if (Array.IndexOf(GearSlotNames, slot) < 0)
                Fail("completion slot \"" + slot + "\" is not a gear slot");
            if (!seen.Add(slot))
                Fail("completion lists slot \"" + slot + "\" twice");
        }
        if (seen.Count != GearSlotNames.Length)
            Fail("completion must list all " + GearSlotNames.Length + " gear slots, got " + seen.Count);
    }
}
