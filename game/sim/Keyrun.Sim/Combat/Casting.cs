using System;
using System.Collections.Generic;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Combat;

/// <summary>What a cast resolves against (sim/combat/casting.ts:33-37 CastContext), plus the step's Clause hooks.</summary>
/// <param name="Abilities">Abilities by id.</param>
/// <param name="Effects">Effect definitions by id.</param>
/// <param name="LockoutTicks">School lockout length after an interrupt.</param>
public sealed record CastContext(
    IReadOnlyDictionary<string, AbilityDef> Abilities,
    IReadOnlyDictionary<string, EffectDef> Effects,
    int LockoutTicks)
{
    /// <summary>The step's Clause hooks (the TS reads them from clause-hooks.ts module state). Default: no Clause.</summary>
    public ClauseHooks Hooks { get; init; } = new(null);
}

/// <summary>
/// Port of sim/combat/casting.ts: the cast lifecycle. startCast validates silence, GCD/charges, cost, school lockout
/// and (target shape) target and range; completeCast applies the ability's ops to its shape-resolved targets at
/// castEndsTick (cones and lines use the caster's facing at completion); stopcast cancels with no effects;
/// interruptCast cancels an interruptible cast and locks its school. Rules.cs completes due casts before a tick's
/// commands, so an interrupt landing exactly at castEndsTick is too late (casting.ts:8-13).
/// </summary>
public static class Casting
{
    /// <summary>sim/combat/casting.ts:39-43 rangeMm: XZ distance, isqrt of the squared delta.</summary>
    /// <param name="a">One actor.</param>
    /// <param name="b">The other.</param>
    /// <returns>Distance, mm.</returns>
    public static long RangeMm(ActorState a, ActorState b)
    {
        var dx = a.Pos.X - b.Pos.X;
        var dz = a.Pos.Z - b.Pos.Z;
        return Movement.Isqrt((dx * dx) + (dz * dz));
    }

    /// <summary>sim/combat/casting.ts:45-47: the synthetic effect id of a school lockout.</summary>
    /// <param name="school">The school.</param>
    /// <returns><c>lockout:&lt;school&gt;</c>.</returns>
    public static string LockoutEffectId(string school) => "lockout:" + school;

    private static bool IsSchoolLocked(ActorState actor, string school, int now)
    {
        var id = LockoutEffectId(school);
        return actor.Effects.Exists(e => e.EffectId == id && e.EndsTick > now);
    }

    /// <summary>
    /// sim/combat/casting.ts:54-81 startCast. Checks in order: silenced (an active Hush pulse), not_ready,
    /// insufficient_resource, school_locked, then for a target shape target_required / invalid_target (missing or
    /// dead) / out_of_range. On success spends the cost, consumes a charge, starts the GCD, sets the cast fields and,
    /// when a target was given, the actor's target.
    /// </summary>
    /// <param name="state">Sim state.</param>
    /// <param name="actor">The caster.</param>
    /// <param name="def">The ability.</param>
    /// <param name="targetId">The command's target, or null.</param>
    /// <param name="point">The command's ground point, or null (unused, as in the TS).</param>
    /// <param name="now">Current tick.</param>
    /// <param name="hooks">The step's Clause hooks, or null for none.</param>
    /// <returns>Null on success, else the error code.</returns>
    public static string? StartCast(SimState state, ActorState actor, AbilityDef def, int? targetId, Vec3? point, int now, ClauseHooks? hooks = null)
    {
        _ = point;
        if (hooks is not null && hooks.IsSilenced(now))
        {
            return "silenced";
        }
        if (!Cooldowns.IsAbilityReady(actor, def, now))
        {
            return "not_ready";
        }
        if (actor.Resource < def.Cost.Amount)
        {
            return "insufficient_resource";
        }
        if (IsSchoolLocked(actor, Abilities.AbilitySchool(def), now))
        {
            return "school_locked";
        }
        if (def.Shape.Kind == ShapeKind.Target)
        {
            if (targetId is null)
            {
                return "target_required";
            }
            if (!state.Actors.TryGetValue(targetId.Value, out var target) || !target.Alive)
            {
                return "invalid_target";
            }
            if (RangeMm(actor, target) > def.Range)
            {
                return "out_of_range";
            }
        }
        actor.Resource -= def.Cost.Amount;
        Cooldowns.ConsumeCharge(actor, def, now);
        Cooldowns.StartGcd(actor, now, def.GcdTicks);
        actor.CastId = def.Id;
        actor.CastStartTick = now;
        actor.CastEndsTick = now + def.CastTicks;
        if (targetId is not null)
        {
            actor.TargetId = targetId;
        }
        return null;
    }

    private static void ClearCast(ActorState actor)
    {
        actor.CastId = null;
        actor.CastStartTick = null;
        actor.CastEndsTick = null;
    }

    /// <summary>sim/combat/casting.ts:89-93 stopcast: emits "cast_stopped" and clears the cast; no-op when idle.</summary>
    /// <param name="actor">The caster.</param>
    /// <param name="emit">Event sink.</param>
    /// <param name="tick">Current tick.</param>
    public static void Stopcast(ActorState actor, Action<SimEvent> emit, int tick)
    {
        if (actor.CastId is null)
        {
            return;
        }
        emit(new SimEvent(tick, "cast_stopped", ("actorId", JsValue.Int(actor.Id)), ("abilityId", JsValue.Str(actor.CastId))));
        ClearCast(actor);
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
    public static void CompleteCast(SimState state, ActorState actor, CastContext ctx, Action<SimEvent> emit)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (actor.CastId is null || !ctx.Abilities.TryGetValue(actor.CastId, out var def))
        {
            ClearCast(actor);
            return;
        }
        CompleteCastOn(state, actor, def, Targeting.ResolveTargets(state, actor, def, Affinities.Of(def, ctx.Effects)), ctx, emit);
    }

    /// <summary>
    /// The targets-resolved half of completeCast (casting.ts:101-110): every op on each target in order, then each
    /// "resource" op credited to the CASTER once per cast (not per target), then "cast_complete", then the cast is
    /// cleared. Split out so the oracle cast ledger can drive it with traced targets (as 2A split ResolveModified).
    /// </summary>
    /// <param name="state">Sim state.</param>
    /// <param name="actor">The caster.</param>
    /// <param name="def">The ability being completed.</param>
    /// <param name="targets">Its resolved targets, in order.</param>
    /// <param name="ctx">Abilities, effects and hooks.</param>
    /// <param name="emit">Event sink.</param>
    public static void CompleteCastOn(SimState state, ActorState actor, AbilityDef def, IReadOnlyList<ActorState> targets, CastContext ctx, Action<SimEvent> emit)
    {
        ArgumentNullException.ThrowIfNull(def);
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(ctx);
        foreach (var target in targets)
        {
            ApplyEffects(state, actor, target, def.Effects, ctx, emit);
        }
        foreach (var op in def.Effects)
        {
            if (op is ResourceOp r)
            {
                Mitigation.CreditResource(actor, r.Delta);
            }
        }
        emit(new SimEvent(state.Tick, "cast_complete", ("actorId", JsValue.Int(actor.Id)), ("abilityId", JsValue.Str(def.Id))));
        ClearCast(actor);
    }

    /// <summary>
    /// sim/combat/casting.ts:113-138 applyEffects: damage (through mitigation, with the Pact weighting), heal,
    /// applyEffect (an unknown effect id is skipped), removeEffect ("*" = dispel), threat, interrupt. window, payoff,
    /// combo and resource ops do nothing per target (Windows.cs and <see cref="CompleteCastOn"/> run them).
    /// </summary>
    private static void ApplyEffects(SimState state, ActorState source, ActorState target, IReadOnlyList<EffectOp> ops, CastContext ctx, Action<SimEvent> emit)
    {
        foreach (var op in ops)
        {
            switch (op)
            {
                case DamageOp d:
                    Mitigation.DealDamage(state.Tick, source, target, d.Amount, ctx.Effects, emit, source.Id, ctx.Hooks.Weighting);
                    break;
                case HealOp h:
                    Damage.ApplyHeal(state.Tick, target, h.Amount, source.Id, emit);
                    break;
                case ApplyEffectOp a:
                    if (ctx.Effects.TryGetValue(a.EffectId, out var effDef))
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
                        Effects.RemoveEffectInstances(target, r.EffectId);
                    }
                    break;
                case ThreatOp t:
                    Threat.AddThreat(target, source.Id, t.Amount ?? 0, t.MultiplierBp);
                    break;
                case InterruptOp:
                    InterruptCast(state, target, source.Id, ctx, emit);
                    break;
            }
        }
    }

    /// <summary>
    /// sim/combat/casting.ts:141-162 interruptCast: a no-op when the target is idle, casting something unknown, or
    /// casting something not interruptible. Otherwise clears the cast, pushes a <c>lockout:&lt;school&gt;</c> instance
    /// ending at tick + lockoutTicks, and emits "interrupt".
    /// </summary>
    /// <param name="state">Sim state.</param>
    /// <param name="target">The actor being kicked.</param>
    /// <param name="sourceId">The kicker.</param>
    /// <param name="ctx">Abilities and lockout length.</param>
    /// <param name="emit">Event sink.</param>
    public static void InterruptCast(SimState state, ActorState target, int sourceId, CastContext ctx, Action<SimEvent> emit)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (target.CastId is null || !ctx.Abilities.TryGetValue(target.CastId, out var castDef) || !castDef.Interruptible)
        {
            return;
        }
        var school = Abilities.AbilitySchool(castDef);
        var abilityId = target.CastId;
        ClearCast(target);
        target.Effects.Add(new EffectInstance
        {
            EffectId = LockoutEffectId(school),
            SourceId = sourceId,
            Stacks = 1,
            EndsTick = state.Tick + ctx.LockoutTicks,
            NextPeriodicTick = null,
        });
        emit(new SimEvent(
            state.Tick,
            "interrupt",
            ("sourceId", JsValue.Int(sourceId)),
            ("targetId", JsValue.Int(target.Id)),
            ("abilityId", JsValue.Str(abilityId)),
            ("school", JsValue.Str(school))));
    }
}
