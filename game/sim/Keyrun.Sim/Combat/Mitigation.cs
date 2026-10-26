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
    /// Blind rule (bible section 4, Steam): a living target hit by a source that <see cref="IsBlinded"/> takes
    /// nothing (no health, no absorb consumed) and a "miss" event (sourceId, targetId, rawAmount) replaces "damage".
    /// Ported to sim/combat/mitigation.ts with identical semantics (D-ORACLE-BLIND-PARITY): the healer press closes
    /// the Cinder's kindled window, so bot-oracle traces apply "blinded" and both sims must miss on the same hit.
    /// </summary>
    /// <param name="tick">Current tick.</param>
    /// <param name="source">The attacker, or null when it is gone.</param>
    /// <param name="target">The actor hit.</param>
    /// <param name="rawAmount">Raw damage.</param>
    /// <param name="defs">Effect definitions.</param>
    /// <param name="emit">Event sink.</param>
    /// <param name="sourceId">Attacker id (reported even when <paramref name="source"/> is null).</param>
    /// <param name="weighting">The active Pact weighting, or null (see <see cref="DamageWeighting"/>).</param>
    /// <param name="school">Damage school of the op; armor mitigates only "physical".</param>
    /// <returns>The hit's outcome.</returns>
    public static DamageResult DealDamage(
        int tick,
        ActorState? source,
        ActorState target,
        long rawAmount,
        IReadOnlyDictionary<string, EffectDef> defs,
        Action<SimEvent> emit,
        int sourceId,
        DamageWeighting? weighting = null,
        string school = Damage.PhysicalSchool)
    {
        if (source is not null && target.Alive && TryGetBlinded(source, tick, out var blindedEndsTick))
        {
            emit(new SimEvent(
                tick,
                "miss",
                ("sourceId", JsValue.Int(sourceId)),
                ("targetId", JsValue.Int(target.Id)),
                ("rawAmount", JsValue.Int(rawAmount)),
                ("reason", JsValue.Str(BlindedEffectId)),
                ("endsTick", JsValue.Int(blindedEndsTick))));
            return new DamageResult(rawAmount, 0, 0, 0, false);
        }
        // C#-only gear stats (BIBLE s7 "Tier is power", Runs.GearPower) add to the effect bp; 0 on every oracle run.
        var sourceModBp = source is not null ? Effects.AggregateModifiers(source, defs).DamageDoneBp + source.GearDamageDoneBp : 0;
        var targetModBp = Effects.AggregateModifiers(target, defs).DamageTakenBp + target.GearDamageTakenBp;
        // R-DEFCAP-1: the player's total damageTakenBp (effects plus gear) is floored at -7500, never invulnerability;
        // f-fe32ef35: only a major-cooldown effect's bp may go past it.
        if (target.Kind == ActorKind.Player)
        {
            targetModBp = Effects.FloorPlayerDamageTakenBp(target, defs, targetModBp);
        }
        return Damage.ApplyDamage(tick, target, rawAmount, sourceModBp, targetModBp, defs, emit, sourceId, weighting, school);
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
    public static bool IsBlinded(ActorState actor, int now) => TryGetBlinded(actor, now, out _);

    /// <summary>
    /// Finds the unexpired "blinded" instance on <paramref name="actor"/> (same rule as <see cref="IsBlinded"/>) and
    /// reports the tick it ends, so a "miss" event can say until when the attacker is blinded.
    /// </summary>
    /// <param name="actor">The attacker.</param>
    /// <param name="now">Current tick.</param>
    /// <param name="endsTick">The blinding instance's EndsTick when found, otherwise 0.</param>
    /// <returns>Whether the actor is blinded.</returns>
    public static bool TryGetBlinded(ActorState actor, int now, out int endsTick)
    {
        foreach (var e in actor.Effects)
        {
            if (e.EffectId == BlindedEffectId && e.EndsTick > now)
            {
                endsTick = e.EndsTick;
                return true;
            }
        }
        endsTick = 0;
        return false;
    }

    /// <summary>sim/combat/mitigation.ts:38-41 creditResource: adds <paramref name="delta"/>, clamped to [0, maxResource].</summary>
    /// <param name="actor">The caster.</param>
    /// <param name="delta">Signed change.</param>
    public static void CreditResource(ActorState actor, long delta) =>
        actor.Resource = Math.Max(0, Math.Min(actor.MaxResource, actor.Resource + delta));
}
