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
        var sourceModBp = source is not null ? Effects.AggregateModifiers(source, defs).DamageDoneBp : 0;
        var targetModBp = Effects.AggregateModifiers(target, defs).DamageTakenBp;
        return Damage.ApplyDamage(tick, target, rawAmount, sourceModBp, targetModBp, defs, emit, sourceId, weighting);
    }

    /// <summary>sim/combat/mitigation.ts:38-41 creditResource: adds <paramref name="delta"/>, clamped to [0, maxResource].</summary>
    /// <param name="actor">The caster.</param>
    /// <param name="delta">Signed change.</param>
    public static void CreditResource(ActorState actor, long delta) =>
        actor.Resource = Math.Max(0, Math.Min(actor.MaxResource, actor.Resource + delta));
}
