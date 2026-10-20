using System;
using System.Collections.Generic;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Combat;

/// <summary>
/// Port of sim/combat/affinity.ts: the hostile-vs-friendly classification that filters circle/cone/line targets
/// (<see cref="Targeting.ResolveTargets"/>). "damage"/"interrupt" signal hostile, "heal" friendly, "applyEffect"
/// defers to <see cref="EffectsRemove.PolarityOf"/> for its effect; every other op carries no signal. Mixed or
/// absent signals resolve hostile (fail closed).
/// </summary>
/// <remarks>
/// C# deviation in mechanism only: the TS keeps a module-level effects registry that rules.ts re-points at the top
/// of every StepRules call (affinity.ts:51-57, configureAffinityEffects). C# passes the run's effect table
/// explicitly (<see cref="Of"/>), so there is no process-global state and no cross-run leak to guard against
/// (tests/sim/combat/rules-affinity-per-run.test.ts). Inside rules the table is the same RulesConfig.effects the
/// TS registry held, so the classification is identical.
/// </remarks>
public static class Affinities
{
    /// <summary>sim/combat/affinity.ts:64-79 abilityAffinity.</summary>
    /// <param name="def">The ability.</param>
    /// <param name="effects">The run's effect definitions (an unknown applyEffect id gives no signal).</param>
    /// <returns>The ability's affinity.</returns>
    public static Affinity Of(AbilityDef def, IReadOnlyDictionary<string, EffectDef> effects)
    {
        ArgumentNullException.ThrowIfNull(def);
        ArgumentNullException.ThrowIfNull(effects);
        var hostile = false;
        var friendly = false;
        foreach (var op in def.Effects)
        {
            switch (op)
            {
                case DamageOp or InterruptOp or DamagePctOp or PullOp:
                    hostile = true;
                    break;
                case HealOp:
                    friendly = true;
                    break;
                case ApplyEffectOp a when effects.TryGetValue(a.EffectId, out var target):
                    if (EffectsRemove.PolarityOf(target) == EffectPolarity.Beneficial)
                    {
                        friendly = true;
                    }
                    else
                    {
                        hostile = true;
                    }
                    break;
            }
        }
        return friendly && !hostile ? Affinity.Friendly : Affinity.Hostile;
    }

    /// <summary>
    /// sim/combat/affinity.ts:87-93 matchesAffinity: hostile keeps the other kind (so never the caster), friendly
    /// keeps the caster's own kind (so the caster too).
    /// </summary>
    /// <param name="affinity">The ability's affinity.</param>
    /// <param name="casterKind">The caster's kind.</param>
    /// <param name="candidateKind">The candidate's kind.</param>
    /// <returns>True when the candidate belongs in the target list.</returns>
    public static bool Matches(Affinity affinity, ActorKind casterKind, ActorKind candidateKind) =>
        affinity == Affinity.Hostile ? candidateKind != casterKind : candidateKind == casterKind;
}
