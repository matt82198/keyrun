using System;
using System.Collections.Generic;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Combat;

/// <summary>The outcome of one hit (sim/combat/damage.ts:35-41 DamageResult).</summary>
/// <param name="RawAmount">The raw amount passed in.</param>
/// <param name="ModifiedAmount">After Pact weighting and bp modifiers (0 for a dead target).</param>
/// <param name="Absorbed">Taken by absorb effects.</param>
/// <param name="HealthDamage">Health actually lost.</param>
/// <param name="Died">True when this hit killed the target.</param>
public readonly record struct DamageResult(long RawAmount, long ModifiedAmount, long Absorbed, long HealthDamage, bool Died);

/// <summary>
/// Port of sim/combat/damage.ts: the integer damage pipeline base -&gt; Pact weighting -&gt; source bp -&gt;
/// target bp -&gt; absorb -&gt; health (floored at 0), emitting "damage" always and "death" the first time health
/// reaches 0; and the heal clamp. Dead actors are inert: no health change, no absorb consumed, no event.
/// </summary>
public static class Damage
{
    /// <summary>The only school class armor mitigates (L3 SIM-ARMOR-SCHOOL; WoW: armor is physical only).</summary>
    public const string PhysicalSchool = "physical";

    /// <summary>
    /// sim/combat/damage.ts:29-33 applyModifiers: <c>trunc(base * (10000 + src) / 10000)</c>, then the same with the
    /// target bp, then max 0. Both divisions are Math.trunc (ORACLE.md section 8), so TruncDiv, never FloorDiv.
    /// </summary>
    /// <param name="baseAmount">The amount before modifiers.</param>
    /// <param name="sourceModBp">Attacker damageDone bp.</param>
    /// <param name="targetModBp">Target damageTaken bp.</param>
    /// <returns>The modified amount, never negative.</returns>
    public static long ApplyModifiers(long baseAmount, long sourceModBp, long targetModBp)
    {
        var afterSource = IntMath.TruncDiv(baseAmount * (Effects.BpDenom + sourceModBp), Effects.BpDenom);
        var afterTarget = IntMath.TruncDiv(afterSource * (Effects.BpDenom + targetModBp), Effects.BpDenom);
        return Math.Max(0, afterTarget);
    }

    /// <summary>
    /// sim/combat/damage.ts:43-76 applyDamage. A dead target returns <c>{raw, 0, 0, 0, false}</c> with no event and
    /// no Pact lookup (:53). Otherwise the Pact weighting (:54-55; <paramref name="weighting"/> null = no active
    /// rules) and the modifiers (:56) produce the modified amount, and <see cref="ResolveModified"/> applies it.
    /// </summary>
    /// <param name="tick">Current tick (the TS reads only <c>state.tick</c>).</param>
    /// <param name="target">The actor hit.</param>
    /// <param name="rawAmount">Raw damage.</param>
    /// <param name="sourceModBp">Attacker damageDone bp.</param>
    /// <param name="targetModBp">Target damageTaken bp.</param>
    /// <param name="defs">Effect definitions (for absorb).</param>
    /// <param name="emit">Event sink.</param>
    /// <param name="sourceId">Attacker id.</param>
    /// <param name="weighting">The active Pact weighting, or null.</param>
    /// <param name="school">Damage school; armor applies only to "physical" (the default, so unannotated callers keep their armor).</param>
    /// <returns>The hit's outcome.</returns>
    public static DamageResult ApplyDamage(
        int tick,
        ActorState target,
        long rawAmount,
        long sourceModBp,
        long targetModBp,
        IReadOnlyDictionary<string, EffectDef> defs,
        Action<SimEvent> emit,
        int sourceId,
        DamageWeighting? weighting = null,
        string school = PhysicalSchool)
    {
        if (!target.Alive)
        {
            return new DamageResult(rawAmount, 0, 0, 0, false);
        }
        var pactAmount = weighting is null ? rawAmount : weighting(rawAmount);
        var modified = ApplyModifiers(pactAmount, sourceModBp, targetModBp);
        var modifiedAmount = school == PhysicalSchool ? ApplyArmor(modified, target.ArmorBp) : modified;
        return ResolveModified(tick, target, rawAmount, modifiedAmount, defs, emit, sourceId);
    }

    /// <summary>
    /// C#-only stage after <see cref="ApplyModifiers"/> (design/BIBLE.md section 3.1): the target keeps
    /// <c>trunc(amount * (10000 - armorBp) / 10000)</c>. It multiplies with the summed damageTakenBp (Brace, Vigil,
    /// Bulwark), so mitigation never stacks to immunity. 0 armor (every oracle run) returns the amount unchanged.
    /// </summary>
    /// <param name="amount">The amount after modifiers (non-negative).</param>
    /// <param name="armorBp">The target's armor, bp in [0, 10000).</param>
    /// <returns>The amount the target takes.</returns>
    public static long ApplyArmor(long amount, long armorBp) =>
        armorBp == 0 ? amount : Math.Max(0, IntMath.TruncDiv(amount * (Effects.BpDenom - armorBp), Effects.BpDenom));

    /// <summary>
    /// The post-modifier half of applyDamage, sim/combat/damage.ts:57-75, split out so oracle traces (which record
    /// raw and modified amounts but not the modifiers in force) can drive it: absorb (:57-59), health floored at 0
    /// (:60-61), death (:62-63), then the "damage" event and, on death, the "death" event (:64-74). A dead target is
    /// inert here too (:53).
    /// </summary>
    /// <param name="tick">Current tick.</param>
    /// <param name="target">The actor hit.</param>
    /// <param name="rawAmount">Raw damage (reported in the event).</param>
    /// <param name="modifiedAmount">Damage after Pact and modifiers.</param>
    /// <param name="defs">Effect definitions (for absorb).</param>
    /// <param name="emit">Event sink.</param>
    /// <param name="sourceId">Attacker id.</param>
    /// <returns>The hit's outcome.</returns>
    public static DamageResult ResolveModified(
        int tick,
        ActorState target,
        long rawAmount,
        long modifiedAmount,
        IReadOnlyDictionary<string, EffectDef> defs,
        Action<SimEvent> emit,
        int sourceId)
    {
        if (!target.Alive)
        {
            return new DamageResult(rawAmount, 0, 0, 0, false);
        }
        var pool = Effects.AbsorbPool(target, defs);
        var absorbed = pool > 0 ? Effects.ConsumeAbsorb(target, defs, Math.Min(pool, modifiedAmount)) : 0;
        var afterAbsorb = Math.Max(0, modifiedAmount - absorbed);
        var healthDamage = Math.Min(target.Health, afterAbsorb);
        target.Health = Math.Max(0, target.Health - healthDamage);
        var died = target.Health == 0;
        if (died)
        {
            target.Alive = false;
        }
        emit(new SimEvent(
            tick,
            "damage",
            ("sourceId", JsValue.Int(sourceId)),
            ("targetId", JsValue.Int(target.Id)),
            ("rawAmount", JsValue.Int(rawAmount)),
            ("modifiedAmount", JsValue.Int(modifiedAmount)),
            ("absorbed", JsValue.Int(absorbed)),
            ("healthDamage", JsValue.Int(healthDamage))));
        if (died)
        {
            emit(new SimEvent(tick, "death", ("actorId", JsValue.Int(target.Id)), ("sourceId", JsValue.Int(sourceId))));
            EndCastOnDeath(target, emit, tick);
        }
        return new DamageResult(rawAmount, modifiedAmount, absorbed, healthDamage, died);
    }

    /// <summary>
    /// DEATH-CANCELS-CAST (C#-only, finding f-42df9d4f; WoW: death cancels every cast in progress). Called on the tick an
    /// actor's health reaches 0, by every death path (this pipeline, and the boss-death falls of Twins and Echo adds):
    /// the dying actor's in-progress cast ends with the sim's existing <c>cast_stopped</c> event
    /// (<see cref="Casting.Stopcast"/>), so no <c>cast_complete</c> ever fires for it and its damage or heal never lands.
    /// A no-op when the actor is not casting. Casts a dead actor starts afterwards (the onDeath DEATH-CAST EXCEPTION) are
    /// untouched: only the cast held at the moment of death ends.
    /// </summary>
    /// <param name="dead">The actor that just died (modified in place).</param>
    /// <param name="emit">Event sink.</param>
    /// <param name="tick">The death tick.</param>
    public static void EndCastOnDeath(ActorState dead, Action<SimEvent> emit, int tick)
    {
        ArgumentNullException.ThrowIfNull(dead);
        Casting.Stopcast(dead, emit, tick);
    }

    /// <summary>
    /// sim/combat/damage.ts:78-90 applyHeal: a dead target returns 0 with no event (:85); otherwise
    /// <c>max(0, min(amount, maxHealth - health))</c> is applied and a "heal" event carries it (:86-88).
    /// </summary>
    /// <param name="tick">Current tick.</param>
    /// <param name="target">The actor healed.</param>
    /// <param name="amount">Requested heal.</param>
    /// <param name="sourceId">Healer id.</param>
    /// <param name="emit">Event sink.</param>
    /// <returns>The amount applied.</returns>
    public static long ApplyHeal(int tick, ActorState target, long amount, int sourceId, Action<SimEvent> emit)
    {
        if (!target.Alive)
        {
            return 0;
        }
        var applied = Math.Max(0, Math.Min(amount, target.MaxHealth - target.Health));
        target.Health += applied;
        emit(new SimEvent(
            tick,
            "heal",
            ("sourceId", JsValue.Int(sourceId)),
            ("targetId", JsValue.Int(target.Id)),
            ("amount", JsValue.Int(applied))));
        return applied;
    }
}
