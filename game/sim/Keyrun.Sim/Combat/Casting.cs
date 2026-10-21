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

    /// <summary>
    /// Class id to its Heat/Overheat rule (BIBLE section 5 Cinder), or null for none. Null on every oracle run: the
    /// Overheat is new behaviour with no TS oracle and runs only when a host sets it (<see cref="RulesConfig.Overheat"/>).
    /// </summary>
    public IReadOnlyDictionary<string, OverheatRule>? Overheat { get; init; }
}

/// <summary>
/// BIBLE section 5 (Cinder): "Heat rises per cast. At 100 the next cast is a free Overheat that dumps Heat to 0 and
/// applies Chill (slower casts for 3 s)." Heat is the class's loop resource; each ability's own resource ops raise it.
/// </summary>
/// <param name="Threshold">Heat at or above which the next cast is the Overheat (the class loop max, 100).</param>
/// <param name="ChillEffectId">The effect the Overheat applies to the caster (content effects "chill", hasteBp -3000).</param>
/// <param name="ChillTicks">Chill duration, ticks (60 = 3 s at 20 Hz).</param>
public sealed record OverheatRule(long Threshold, string ChillEffectId, int ChillTicks);

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
    /// sim/combat/casting.ts:54-81 startCast. Checks in order: casting (a cast already in flight is never replaced,
    /// BIBLE section 3; Stopcast is the only cancel), silenced (an active Hush pulse), casting again for a player
    /// (<see cref="IsCastingPlayer"/>, so the side-effect-free <see cref="StartError"/> preview also refuses a press
    /// mid-cast), not_ready,
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
    /// <param name="ctx">The step's cast context (its <see cref="CastContext.Overheat"/> rule), or null for none.</param>
    /// <returns>Null on success, else the error code.</returns>
    public static string? StartCast(SimState state, ActorState actor, AbilityDef def, int? targetId, Vec3? point, int now, ClauseHooks? hooks = null, CastContext? ctx = null)
    {
        _ = point;
        hooks ??= ctx?.Hooks;
        var rule = OverheatRuleOf(ctx, actor);
        // BIBLE section 5: at the threshold the next cast is the Overheat, and the Overheat is free.
        var overheat = rule is not null && actor.Resource >= rule.Threshold;
        // BIBLE section 3 (cast bars are the enemy's language; a kick counts inside the cast): a busy caster rejects
        // the new cast, so an in-flight cast is never silently replaced. A dead caster is exempt: its only casts are
        // onDeath script steps (Rules' DEATH-CAST EXCEPTION), and BIBLE section 9 needs the Seed's on-death Window
        // even when it died mid-cast.
        if (actor.Alive && actor.CastId is not null)
        {
            return "casting";
        }
        if (StartError(state, actor, def, targetId, now, hooks, overheat) is { } err)
        {
            return err;
        }
        if (!overheat)
        {
            actor.Resource -= def.Cost.Amount;
        }
        Cooldowns.ConsumeCharge(actor, def, now);
        Cooldowns.StartGcd(actor, now, Riposte.GcdTicksFor(actor, def, now));
        actor.CastId = def.Id;
        actor.CastStartTick = now;
        actor.CastEndsTick = now + ChilledCastTicks(actor, def.CastTicks, rule, ctx, now);
        actor.CastIsOverheat = overheat;
        // CAST-KEEPS-START-TARGET: a new cast starts unpinned; it resolves on the target set here unless a mid-cast
        // SetTarget pins it. Mirrors casting.ts startCast.
        ReleaseCastTarget(actor);
        if (targetId is not null)
        {
            actor.TargetId = targetId;
        }
        if (!overheat)
        {
            Threat.AddMomentumSpentThreat(state, actor, def.Cost);
        }
        return null;
    }

    /// <summary>
    /// The checks <see cref="StartCast"/> makes, in its order, with no side effects: null when a cast of
    /// <paramref name="def"/> on <paramref name="targetId"/> would start on tick <paramref name="now"/>, else
    /// StartCast's error code. SLAM-TELEGRAPH: a spansCast script telegraph is drawn only when this says its cast starts.
    /// StartCast's all-caster busy check ("casting") stays out: a scripted step whose caster is busy waits for the cast
    /// to land (CAST-FINISH, <c>EncounterState.PendingSteps</c>), not dropped, and a pre-combat busy test cannot see a
    /// cast that completes this same tick. Only the player busy check (<see cref="IsCastingPlayer"/>, PR #370) is
    /// here, so bot readiness and the spell queue read "casting" for a player mid-cast; enemies are exempt from it.
    /// </summary>
    /// <param name="state">Sim state.</param>
    /// <param name="actor">The caster.</param>
    /// <param name="def">The ability.</param>
    /// <param name="targetId">The target, or null.</param>
    /// <param name="now">The tick the cast would start.</param>
    /// <param name="hooks">Clause hooks (null for an enemy: enemies are never silenced).</param>
    /// <param name="overheat">True when the cast is a free Overheat (players only).</param>
    /// <returns>Null when the cast would start, else the error code.</returns>
    public static string? StartError(SimState state, ActorState actor, AbilityDef def, int? targetId, int now, ClauseHooks? hooks = null, bool overheat = false)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(def);
        if (hooks is not null && hooks.IsSilenced(now))
        {
            return "silenced";
        }
        if (IsCastingPlayer(actor))
        {
            return "casting";
        }
        if (!Cooldowns.IsAbilityReady(actor, def, now))
        {
            return "not_ready";
        }
        if (!overheat && actor.Resource < def.Cost.Amount)
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
        return null;
    }

    /// <summary>
    /// Finding f-80b5cf16 follow-up (orchestrator decision): a player's ability is never usable while that player is
    /// already casting. A press mid-cast (a gcdTicks-0 instant included, now that it skips the GCD) is refused and the
    /// cast under way continues: no silent replacement, no cast_stopped. Enemies are exempt here: a busy enemy's scripted
    /// step waits for its cast to land (CAST-FINISH, Scripts.CastHeldAt) and StartCast's all-caster guard refuses it. A cast ending on
    /// this tick was already completed (and its CastId cleared) before commands run.
    /// </summary>
    /// <param name="actor">The caster.</param>
    /// <returns>True when the press must be refused.</returns>
    private static bool IsCastingPlayer(ActorState actor) => actor.Kind == ActorKind.Player && actor.CastId is not null;

    private static OverheatRule? OverheatRuleOf(CastContext? ctx, ActorState actor) =>
        ctx?.Overheat is { } rules && rules.TryGetValue(actor.ClassId, out var rule) ? rule : null;

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
    /// BIBLE section 5 Chill, "slower casts": while the caster holds its rule's Chill, a cast takes
    /// castTicks * 10000 / (10000 + hasteBp) ticks (floor; Chill's hasteBp -3000 makes 20 ticks 28). An instant cast
    /// stays instant. No rule, no Chill, or a Chill with no haste penalty leaves the content cast time unchanged.
    /// </summary>
    private static int ChilledCastTicks(ActorState actor, int castTicks, OverheatRule? rule, CastContext? ctx, int now)
    {
        if (rule is null || ctx is null || castTicks <= 0 || !ctx.Effects.TryGetValue(rule.ChillEffectId, out var chill))
        {
            return castTicks;
        }
        var haste = chill.Modifiers.HasteBp ?? 0;
        var chilled = actor.Effects.Exists(e => e.EffectId == rule.ChillEffectId && e.EndsTick > now);
        if (!chilled || haste >= 0 || Effects.BpDenom + haste <= 0)
        {
            return castTicks;
        }
        return (int)IntMath.FloorDiv(castTicks * Effects.BpDenom, Effects.BpDenom + haste);
    }

    /// <summary>
    /// The Overheat's completion (BIBLE section 5): Heat dumps to 0 (after the cast's own resource ops, so its Heat
    /// gain is dumped too), the caster gains its rule's Chill for ChillTicks, and an "overheat" event is emitted.
    /// </summary>
    private static void CompleteOverheat(SimState state, ActorState actor, AbilityDef def, CastContext ctx, Action<SimEvent> emit)
    {
        var rule = OverheatRuleOf(ctx, actor);
        if (rule is null)
        {
            return;
        }
        actor.Resource = 0;
        if (ctx.Effects.TryGetValue(rule.ChillEffectId, out var chill))
        {
            Effects.ApplyEffectInstance(actor, chill, actor.Id, rule.ChillTicks, 1, state.Tick);
        }
        emit(new SimEvent(state.Tick, "overheat", ("actorId", JsValue.Int(actor.Id)), ("abilityId", JsValue.Str(def.Id))));
    }

    private static void ClearCast(ActorState actor)
    {
        actor.CastId = null;
        actor.CastStartTick = null;
        actor.CastEndsTick = null;
        actor.CastIsOverheat = false;
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
        ReleaseCastTarget(actor);
    }

    /// <summary>
    /// CAST-KEEPS-START-TARGET (finding f-bd8fbbb1, the WoW rule: a cast resolves on the target it started on;
    /// sim/combat/targeting.ts setTarget). The caster's own retarget (a TargetCommand, Rules) goes through here. A
    /// retarget while a cast is in flight first pins the target that cast started on, so the cast still resolves there;
    /// the new target is for the next press. The pin is taken only on a real change mid-cast. The sim's own dead-target
    /// auto-retarget (Runs.RunWiring.FixupTargeting) deliberately does not pin.
    /// </summary>
    /// <param name="actor">The actor retargeting.</param>
    /// <param name="targetId">Its new current target, or null.</param>
    public static void SetTarget(ActorState actor, int? targetId)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (actor.CastId is not null && !actor.CastTargetPinned && targetId != actor.TargetId)
        {
            actor.CastTargetPinned = true;
            actor.CastTargetId = actor.TargetId;
        }
        actor.TargetId = targetId;
    }

    /// <summary>The target the cast in flight resolves on: its pinned start target, else the current target.</summary>
    /// <param name="actor">The caster.</param>
    /// <returns>The cast's target id, or null.</returns>
    public static int? CastTargetOf(ActorState actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return actor.CastTargetPinned ? actor.CastTargetId : actor.TargetId;
    }

    /// <summary>Drops the pin once the cast is over (completed with its Windows, stopped, interrupted, or replaced).</summary>
    /// <param name="actor">The caster.</param>
    public static void ReleaseCastTarget(ActorState actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        actor.CastTargetPinned = false;
        actor.CastTargetId = null;
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
        // BIBLE section 5: a verse ability plays the caster's verse variant (no variants = unchanged, every oracle def).
        def = Abilities.ResolveVerse(def, actor);
        CompleteCastOn(state, actor, def, Targeting.ResolveTargets(state, actor, def, Affinities.Of(def, ctx.Effects)), ctx, emit);
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
    public static void CompleteCastOn(SimState state, ActorState actor, AbilityDef def, IReadOnlyList<ActorState> targets, CastContext ctx, Action<SimEvent> emit)
    {
        ArgumentNullException.ThrowIfNull(def);
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(ctx);
        var landed = false;
        var isKick = false;
        foreach (var target in targets)
        {
            landed |= ApplyEffects(state, actor, target, def.Effects, ctx, emit);
        }
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
        }
        if (actor.CastIsOverheat)
        {
            CompleteOverheat(state, actor, def, ctx, emit);
        }
        emit(new SimEvent(state.Tick, "cast_complete", ("actorId", JsValue.Int(actor.Id)), ("abilityId", JsValue.Str(def.Id))));
        ClearCast(actor);
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
        foreach (var op in ops)
        {
            switch (op)
            {
                case DamageOp d:
                    Mitigation.DealDamage(state.Tick, source, target, d.Amount, ctx.Effects, emit, source.Id, ctx.Hooks.Weighting);
                    break;
                case HealOp h:
                    if (target.Kind == source.Kind)
                    {
                        Damage.ApplyHeal(state.Tick, target, h.Amount, source.Id, emit);
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
                case ThreatOp t:
                    Threat.AddThreat(target, source.Id, t.Amount ?? 0, t.MultiplierBp);
                    break;
                case InterruptOp:
                    interrupted |= InterruptCast(state, target, source.Id, ctx, emit);
                    break;
            }
        }
        return interrupted;
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
    /// <returns>True when the kick landed (a cast was cleared and "interrupt" emitted), false on a no-op.</returns>
    public static bool InterruptCast(SimState state, ActorState target, int sourceId, CastContext ctx, Action<SimEvent> emit)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        if (target.CastId is null || !ctx.Abilities.TryGetValue(target.CastId, out var castDef) || !castDef.Interruptible)
        {
            return false;
        }
        var school = Abilities.AbilitySchool(castDef);
        var abilityId = target.CastId;
        ClearCast(target);
        ReleaseCastTarget(target);
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
        return true;
    }
}
