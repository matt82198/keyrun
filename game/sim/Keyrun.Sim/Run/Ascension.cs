using System;
using System.Linq;
using Keyrun.Sim.Encounter;

namespace Keyrun.Sim.Runs;

/// <summary>
/// BIBLE s7 "After the cap: Ascension. Higher keys, cosmetics, the key ladder, and Ascension marks on the character.
/// Power never moves again." A pure, read-only query over <see cref="Progression"/>: it reads the equipped gear and
/// returns a verdict, never a new <see cref="Progression"/>, so asking can neither reset the character nor change a
/// stat (Ascension grants no power, ever).
/// </summary>
/// <remarks>
/// Trigger (orchestrator decision, reversible): every one of the five <see cref="Rewards.GearSlots"/> holds an item at
/// <see cref="Progression.MaxTier"/>. An empty slot counts as below the cap. Ascension marks are not exposed: the
/// progression model does not record key levels, so "timed keys above key 10" cannot be counted from it yet.
/// Whether Ascension is visible to other players is BIBLE s12 [DECIDE] and is not modelled here.
/// </remarks>
public static class Ascension
{
    /// <summary>True once every gear slot is filled at <see cref="Progression.MaxTier"/>.</summary>
    /// <param name="progression">The character.</param>
    /// <returns>Whether the character has ascended.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="progression"/> is null.</exception>
    public static bool IsAscended(Progression progression)
    {
        ArgumentNullException.ThrowIfNull(progression);
        return Rewards.GearSlots.All(slot =>
            progression.Equipped.TryGetValue(slot, out var item) && item.Tier >= Progression.MaxTier);
    }
}
