using System;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Combat;

public static partial class Casting
{
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
