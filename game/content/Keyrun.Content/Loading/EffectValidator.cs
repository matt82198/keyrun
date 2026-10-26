using System;

namespace Keyrun.Content;

/// <summary>
/// Status-effect invariants: no effect's <c>modifiers.damageTakenBp</c> may sit below <see cref="MinDamageTakenBp"/>
/// (-100%), so two stacked reductions can never multiply into a heal. Runs inside <see cref="ContentValidator.Validate"/>.
/// Mirrors the gear floor in <see cref="GearValidator"/>.
/// </summary>
public static class EffectValidator
{
    /// <summary>The floor: an effect may remove all incoming damage but never heal through a negative reduction.</summary>
    public const int MinDamageTakenBp = -10000;

    /// <summary>Checks every effect definition.</summary>
    /// <param name="reg">The registry.</param>
    /// <exception cref="ContentLoadException">The first violated invariant.</exception>
    public static void Check(ContentRegistry reg)
    {
        ArgumentNullException.ThrowIfNull(reg);
        foreach (EffectDef effect in reg.Effects.Values)
        {
            int? bp = effect.Modifiers.DamageTakenBp;
            if (bp is int value && value < MinDamageTakenBp)
                throw new ContentLoadException("content: effect \"" + effect.Id + "\" damageTakenBp " + value
                    + " is below the floor " + MinDamageTakenBp);
        }
    }
}
