using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Combat;

/// <summary>Buff or debuff, derived from an effect's own fields (sim/combat/effects-remove.ts:24).</summary>
public enum EffectPolarity
{
    /// <summary>Helps the holder.</summary>
    Beneficial,

    /// <summary>Hurts the holder, or carries no signal (fail closed).</summary>
    Hostile,
}

/// <summary>
/// Port of sim/combat/effects-remove.ts: the wildcard <c>removeEffect "*"</c> dispel (Cleanse on an ally strips
/// hostile effects, Purge on an enemy strips beneficial ones), only for effects with a non-null dispellableTag,
/// in insertion order.
/// </summary>
public static class EffectsRemove
{
    /// <summary>
    /// sim/combat/effects-remove.ts:32-48 effectPolarity: beneficial only when some modifier helps the holder (or
    /// the periodic op is a heal) and none hurts it (or the periodic op is damage); otherwise hostile, which is
    /// also the default for effects with no signal at all (stunned, silenced).
    /// </summary>
    /// <param name="def">Effect definition.</param>
    /// <returns>Its polarity.</returns>
    public static EffectPolarity PolarityOf(EffectDef def)
    {
        var m = def.Modifiers;
        var op = def.Periodic?.Op;
        var beneficial =
            (m.DamageTakenBp ?? 0) < 0 ||
            (m.MovementBp ?? 0) > 0 ||
            (m.HasteBp ?? 0) > 0 ||
            (m.DamageDoneBp ?? 0) > 0 ||
            (m.Absorb ?? 0) > 0 ||
            op is HealOp;
        var hostile =
            (m.DamageTakenBp ?? 0) > 0 ||
            (m.MovementBp ?? 0) < 0 ||
            (m.HasteBp ?? 0) < 0 ||
            (m.DamageDoneBp ?? 0) < 0 ||
            op is DamageOp or DamagePctOp;
        return beneficial && !hostile ? EffectPolarity.Beneficial : EffectPolarity.Hostile;
    }

    /// <summary>
    /// sim/combat/effects-remove.ts:56-68 removeDispellableEffects: same kind = ally, removes hostile effects;
    /// different kind = enemy, removes beneficial ones (:61-62). Instances without a def or with a null
    /// dispellableTag always stay (:65).
    /// </summary>
    /// <param name="sourceKind">The dispeller's kind (the TS takes <c>Pick&lt;ActorState, "kind"&gt;</c>).</param>
    /// <param name="target">The actor being dispelled.</param>
    /// <param name="defs">Effect definitions by id.</param>
    public static void RemoveDispellableEffects(ActorKind sourceKind, ActorState target, IReadOnlyDictionary<string, EffectDef> defs)
    {
        var removePolarity = sourceKind == target.Kind ? EffectPolarity.Hostile : EffectPolarity.Beneficial;
        target.Effects = target.Effects.Where(inst =>
        {
            if (!defs.TryGetValue(inst.EffectId, out var def) || def.DispellableTag is null)
            {
                return true;
            }
            return PolarityOf(def) != removePolarity;
        }).ToList();
    }
}
