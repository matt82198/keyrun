using System;
using System.Collections.Generic;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Combat;

public static partial class Casting
{
    /// <summary>
    /// Who a non-dispel removeEffect op clears (finding f-9efbee75): the caster when the effect is the caster's own
    /// Overheat Chill (BIBLE section 5 Cold Snap "dumps Heat, clears Chill"; section 4 Flashfire "refunds Cinder 30 Heat
    /// and clears Chill"), else the target. C#-only: with no Overheat rule (every oracle run) it is always the target,
    /// as the TS.
    /// </summary>
    /// <param name="source">The caster (or the Payoff actor of a combo).</param>
    /// <param name="target">The op's target.</param>
    /// <param name="effectId">The effect being removed.</param>
    /// <param name="overheat">The step's Overheat rules, or null.</param>
    /// <returns>The actor whose instances of <paramref name="effectId"/> are removed.</returns>
    public static ActorState RemoveEffectHolder(ActorState source, ActorState target, string effectId, IReadOnlyDictionary<string, OverheatRule>? overheat)
    {
        ArgumentNullException.ThrowIfNull(source);
        return overheat is not null && overheat.TryGetValue(source.ClassId, out var rule)
            && string.Equals(rule.ChillEffectId, effectId, StringComparison.Ordinal) ? source : target;
    }

    /// <summary>
    /// sim/combat/casting.ts:95-111 completeCast: an unknown castId just clears the cast (no event); otherwise the
    /// ability's targets are resolved now (<see cref="Targeting.ResolveTargets"/> with its <see cref="Affinities.Of"/>)
    /// and <see cref="CompleteCastOn"/> applies it.
    /// </summary>
    /// <param name="state">Sim state.</param>
    /// <param name="actor">The caster.</param>
    /// <param name="ctx">Abilities, effects and hooks.</param>
    /// <param name="emit">Event sink.</param>
    /// <returns>The ended cast (<see cref="End"/>), carrying the pin its Windows resolve on, or null when there was none.</returns>
    public static CastInFlight? CompleteCast(SimState state, ActorState actor, CastContext ctx, Action<SimEvent> emit)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (actor.CastId is null || !ctx.Abilities.TryGetValue(actor.CastId, out var def))
        {
            return End(actor, CastEnd.Completed);
        }
        // BIBLE section 5: a verse ability plays the caster's verse variant (no variants = unchanged, every oracle def).
        def = Abilities.ResolveVerse(def, actor);
        return CompleteCastOn(state, actor, def, Targeting.ResolveTargets(state, actor, def, Affinities.Of(def, ctx.Effects)), ctx, emit);
    }

    /// <summary>
    /// The targets-resolved half of completeCast (casting.ts:101-110): every op on each target in order, then each
    /// "resource" op credited to the CASTER once per cast (not per target), then "cast_complete", then the cast is
    /// cleared. Split out so the oracle cast ledger can drive it with traced targets (as 2A split ResolveModified).
    /// BIBLE section 5 (Quench "refunds 20 Heat on a successful kick"): on an ability that carries an "interrupt"
    /// op, its resource ops are a kick refund and credit only when at least one interrupt landed; a whiff refunds
    /// nothing. Abilities with no interrupt op credit unconditionally, as before.
    /// </summary>
    /// <param name="state">Sim state.</param>
    /// <param name="actor">The caster.</param>
    /// <param name="def">The ability being completed.</param>
    /// <param name="targets">Its resolved targets, in order.</param>
    /// <param name="ctx">Abilities, effects and hooks.</param>
    /// <param name="emit">Event sink.</param>
    /// <returns>
    /// The ended cast (<see cref="End"/>): the actor holds no cast and no pin once this returns, and the returned value
    /// carries the pin the cast's Windows resolve on (<see cref="TargetOfEnded"/>).
    /// </returns>
    public static CastInFlight? CompleteCastOn(SimState state, ActorState actor, AbilityDef def, IReadOnlyList<ActorState> targets, CastContext ctx, Action<SimEvent> emit)
    {
        ArgumentNullException.ThrowIfNull(def);
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(ctx);
        var taunt = Holder.IsTaunt(def);
        var landed = false;
        var isKick = false;
        foreach (var target in targets)
        {
            landed |= ApplyEffects(state, actor, target, def.Effects, ctx, emit);
            if (taunt)
            {
                var holderBefore = target.HolderId;
                Holder.ApplyTaunt(actor, target); // HOLDER-LOCK (STATE.md D30): only a taunt moves a holder
                if (target.Kind == ActorKind.Enemy && target.HolderId != holderBefore)
                {
                    emit(HolderChangeLog.Event(state.Tick, target.Id, target.HolderId));
                }
            }
        }
        // C#-only (null on every oracle run): the Pair's mark_applied receipt, once per completed mark cast.
        PairMarks.EmitApplied(state, def, targets, actor.Id, ctx.Marks, emit);
        foreach (var op in def.Effects)
        {
            isKick |= op is InterruptOp;
        }
        foreach (var op in def.Effects)
        {
            if (op is ResourceOp r && (landed || !isKick))
            {
                Mitigation.CreditResource(actor, r.Delta);
            }
            else if (op is DisplaceOp displace)
            {
                // TOOLKIT-CONTRACT L2: moves the CASTER, once per cast (not per target); the bounds and walkable clamps run after.
                Displace.Apply(actor, targets.Count > 0 ? targets[0] : null, displace);
            }
        }
        if (actor.CastIsOverheat)
        {
            CompleteOverheat(state, actor, def, ctx, emit);
        }
        emit(new SimEvent(state.Tick, "cast_complete", ("actorId", JsValue.Int(actor.Id)), ("abilityId", JsValue.Str(def.Id))));
        return End(actor, CastEnd.Completed);
    }

    /// <summary>
    /// sim/combat/casting.ts:113-138 applyEffects: damage (through mitigation, with the Pact weighting), heal,
    /// applyEffect (an unknown effect id is skipped), removeEffect ("*" = dispel), threat, interrupt. window, payoff,
    /// combo and resource ops do nothing per target (Windows.cs and <see cref="CompleteCastOn"/> run them).
    /// Returns true when an interrupt op landed on this target.
    /// C# deviation in behaviour (HEAL-NEVER-HOSTILE, D28 the C# sim is the oracle): a heal op, or an applyEffect whose
    /// effect is beneficial (<see cref="EffectsRemove.PolarityOf"/>: a HoT, a shield, a haste), lands only on the
    /// caster's own faction. The TS "target" shape keeps whatever the caster targets when the cast completes and a
    /// mixed area resolves hostile, so the Chanter's Cadence (Mend) HoT ticked on the Choir Acolyte and the Pair in
    /// policy playtests. Every other op on the same ability (damage, the Attuned Window, a slow) still lands.
    /// </summary>
    private static bool ApplyEffects(SimState state, ActorState source, ActorState target, IReadOnlyList<EffectOp> ops, CastContext ctx, Action<SimEvent> emit)
    {
        var interrupted = false;
        var abilityBp = Threat.AbilityCoefficientBp(ops);
        foreach (var op in ops)
        {
            switch (op)
            {
                case DamageOp d:
                    Threat.AddDamageThreat(target, source, Mitigation.DealDamage(state.Tick, source, target, d.Amount, ctx.Effects, emit, source.Id, ctx.Hooks.Weighting, d.School).ModifiedAmount, abilityBp);
                    break;
                case DamagePctOp p:
                    Threat.AddDamageThreat(target, source, Mitigation.DealDamage(state.Tick, source, target, Effects.PctOfMaxHealth(target, p.MaxHealthBp), ctx.Effects, emit, source.Id, ctx.Hooks.Weighting, p.School).ModifiedAmount, abilityBp);
                    break;
                case PullOp pull:
                    Movement.PullToward(target, source, pull.DistanceMm);
                    break;
                case HealOp h:
                    if (target.Kind == source.Kind)
                    {
                        Threat.AddHealingThreat(state, source, target, Damage.ApplyHeal(state.Tick, target, h.Amount, source.Id, emit));
                    }
                    break;
                case ApplyEffectOp a:
                    if (ctx.Effects.TryGetValue(a.EffectId, out var effDef) &&
                        (target.Kind == source.Kind || EffectsRemove.PolarityOf(effDef) != EffectPolarity.Beneficial))
                    {
                        Effects.ApplyEffectInstance(target, effDef, source.Id, a.DurationTicks, a.Stacks, state.Tick);
                    }
                    break;
                case RemoveEffectOp r:
                    if (r.EffectId == "*")
                    {
                        EffectsRemove.RemoveDispellableEffects(source.Kind, target, ctx.Effects);
                    }
                    else
                    {
                        Effects.RemoveEffectInstances(RemoveEffectHolder(source, target, r.EffectId, ctx.Overheat), r.EffectId);
                    }
                    break;
                case ThreatOp t when t.Amount is not null:
                    Threat.AddThreat(target, source.Id, t.Amount.Value, t.MultiplierBp);
                    break;
                case InterruptOp:
                    interrupted |= InterruptCast(state, target, source.Id, ctx, emit);
                    break;
            }
        }
        return interrupted;
    }
}
