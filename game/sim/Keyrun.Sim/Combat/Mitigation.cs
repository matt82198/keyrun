using System;
using System.Collections.Generic;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Combat;

/// <summary>
/// Port of sim/combat/mitigation.ts: the single place a damage op picks up its bp modifiers (from
/// <see cref="Effects.AggregateModifiers"/>) and the single place a "resource" op is credited (to the caster).
/// </summary>
public static class Mitigation
{
    /// <summary>
    /// sim/combat/mitigation.ts:24-36 dealDamage: the attacker's damageDoneBp when it still exists (a removed DoT
    /// source contributes 0, :33), the target's damageTakenBp always (:34), then <see cref="Damage.ApplyDamage"/>.
    /// C#-only rule (bible section 4, Steam): a living target hit by a source that <see cref="IsBlinded"/> takes
    /// nothing (no health, no absorb consumed) and a "miss" event (sourceId, targetId, rawAmount) replaces "damage".
    /// No oracle trace applies "blinded", so parity is unaffected.
    /// </summary>
    /// <param name="tick">Current tick.</param>
    /// <param name="source">The attacker, or null when it is gone.</param>
    /// <param name="target">The actor hit.</param>
    /// <param name="rawAmount">Raw damage.</param>
    /// <param name="defs">Effect definitions.</param>
    /// <param name="emit">Event sink.</param>
    /// <param name="sourceId">Attacker id (reported even when <paramref name="source"/> is null).</param>
    /// <param name="weighting">The active Pact weighting, or null (see <see cref="DamageWeighting"/>).</param>
    /// <returns>The hit's outcome.</returns>
    public static DamageResult DealDamage(
        int tick,
        ActorState? source,
        ActorState target,
        long rawAmount,
        IReadOnlyDictionary<string, EffectDef> defs,
        Action<SimEvent> emit,
        int sourceId,
        DamageWeighting? weighting = null)
    {
        if (source is not null && target.Alive && IsBlinded(source, tick))
        {
            emit(new SimEvent(
                tick,
                "miss",
                ("sourceId", JsValue.Int(sourceId)),
                ("targetId", JsValue.Int(target.Id)),
                ("rawAmount", JsValue.Int(rawAmount))));
            return new DamageResult(rawAmount, 0, 0, 0, false);
        }
        var sourceModBp = source is not null ? Effects.AggregateModifiers(source, defs).DamageDoneBp : 0;
        var targetModBp = Effects.AggregateModifiers(target, defs).DamageTakenBp;
        return Damage.ApplyDamage(tick, target, rawAmount, sourceModBp, targetModBp, defs, emit, sourceId, weighting);
    }

    /// <summary>The status id that makes its carrier miss (design/BIBLE.md section 4, Steam: "they miss").</summary>
    public const string BlindedEffectId = "blinded";

    /// <summary>
    /// True while <paramref name="actor"/> carries an unexpired "blinded" instance (<c>endsTick &gt; now</c>, the
    /// same rule as <see cref="Effects.ExpireEffects"/>). The content def has empty modifiers; the sim decides by id.
    /// </summary>
    /// <param name="actor">The attacker.</param>
    /// <param name="now">Current tick.</param>
    /// <returns>Whether its hits miss.</returns>
    public static bool IsBlinded(ActorState actor, int now)
    {
        foreach (var e in actor.Effects)
        {
            if (e.EffectId == BlindedEffectId && e.EndsTick > now)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>sim/combat/mitigation.ts:38-41 creditResource: adds <paramref name="delta"/>, clamped to [0, maxResource].</summary>
    /// <param name="actor">The caster.</param>
    /// <param name="delta">Signed change.</param>
    public static void CreditResource(ActorState actor, long delta) =>
        actor.Resource = Math.Max(0, Math.Min(actor.MaxResource, actor.Resource + delta));
}
