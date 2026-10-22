using System;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Combat;

public static partial class Casting
{
    /// <summary>sim/combat/casting.ts:45-47: the synthetic effect id of a school lockout.</summary>
    /// <param name="school">The school.</param>
    /// <returns><c>lockout:&lt;school&gt;</c>.</returns>
    public static string LockoutEffectId(string school) => "lockout:" + school;

    /// <summary>L5 caster-los: the refusal code of an enemy target-shaped cast whose ray to its target crosses an obstacle.</summary>
    public const string NoSightError = "no_line_of_sight";

    // L5 caster-los (R-SYNERGY-2): an enemy target-shaped cast starts only with a clear integer ray to its target, so a
    // tank pulling a pack around a pillar makes its casters walk in. Players are exempt (their casts are commands the
    // human or a bot chose), and a context with no Sight (every run without layout obstacles) never withholds.
    // Checked after StartError, so no cost, charge or GCD is spent on a withheld cast.
    private static bool NoSight(SimState state, ActorState actor, AbilityDef def, int? targetId, CastContext? ctx)
    {
        if (ctx?.Sight is not { } sight || actor.Kind != ActorKind.Enemy || def.Shape.Kind != ShapeKind.Target ||
            targetId is not { } tid || !state.Actors.TryGetValue(tid, out var target))
        {
            return false;
        }
        return !sight(actor.Pos, target.Pos);
    }

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
        if (NoSight(state, actor, def, targetId, ctx))
        {
            return NoSightError;
        }
        if (!overheat)
        {
            actor.Resource -= def.Cost.Amount;
        }
        Cooldowns.ConsumeCharge(actor, def, now);
        Cooldowns.StartGcd(actor, now, Riposte.GcdTicksFor(actor, def, now));
        // CAST-KEEPS-START-TARGET: a new cast starts unpinned; it resolves on the target set here unless a mid-cast
        // SetTarget pins it. Mirrors casting.ts startCast. Written whole (R2), so no field of a replaced cast survives.
        actor.Cast = new CastInFlight(def.Id, now, now + HastedCastTicks(actor, def.CastTicks, rule, ctx, now), overheat, null, false);
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
        // G2.5 (f-1c94a912): an evader walking home casts nothing (Leash also stops its cast when it starts home).
        if (actor.Evading)
        {
            return "evading";
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
            // G2.5 (f-1c94a912): an evader is immune from the moment it starts walking home, not only at target resolution.
            if (!state.Actors.TryGetValue(targetId.Value, out var target) || !target.Alive || target.Evading)
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
}
