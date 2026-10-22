using System;
using System.Collections.Generic;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Encounter;

/// <summary>
/// TELEGRAPH-DRAW (review R1): one telegraph a script draws this tick, typed end to end. <see cref="Scripts.StepEnemyScripts"/>
/// produces these, <see cref="Telegraphs.Register(in TelegraphDraw, int)"/> turns one into a registry
/// <see cref="Telegraph"/> with no field-bag parse, and <see cref="ToEvent"/> is the "telegraph" event the encounter step
/// emits (scripts.ts:99 telegraphEventAt, same fields in the same order). A new telegraph field is added here and in
/// <see cref="TelegraphOrigin"/> only, never at the draw sites.
/// </summary>
/// <param name="ActorId">The caster.</param>
/// <param name="Cast">The ability the telegraph warns of.</param>
/// <param name="Spec">The spec as drawn (lead already adjusted for a spanned cast).</param>
/// <param name="DrawTick">The tick it is drawn on.</param>
/// <param name="Origin">Its origin, mm (<see cref="TelegraphOrigin.TryResolve"/>).</param>
/// <param name="Yaw">The caster's yaw on <paramref name="DrawTick"/>.</param>
/// <param name="CastStartTick">TELEGRAPH-CANCEL: the tick its cast is due to start when it spans that cast, else null.</param>
public readonly record struct TelegraphDraw(int ActorId, string Cast, TelegraphSpec Spec, int DrawTick, Vec3 Origin, int Yaw, int? CastStartTick)
{
    /// <summary>The tick it resolves on: drawn <see cref="TelegraphSpec.LeadTicks"/> ahead.</summary>
    public int ResolvesTick => DrawTick + Spec.LeadTicks;

    /// <summary>
    /// The "telegraph" event: {tick, type, actorId, cast, shape, leadTicks, resolvesTick, origin, yaw, radius?,
    /// angle1024?, length?, width?} (scripts.ts:99, field order is part of the trace hash).
    /// </summary>
    /// <returns>The event.</returns>
    public SimEvent ToEvent()
    {
        var fields = new List<(string, JsValue)>
        {
            ("actorId", JsValue.Int(ActorId)),
            ("cast", JsValue.Str(Cast)),
            ("shape", JsValue.Str(Spec.Shape)),
            ("leadTicks", JsValue.Int(Spec.LeadTicks)),
            ("resolvesTick", JsValue.Int(ResolvesTick)),
            ("origin", JsValue.Vec(Origin)),
            ("yaw", JsValue.Int(Yaw)),
        };
        if (Spec.Radius is { } r)
        {
            fields.Add(("radius", JsValue.Int(r)));
        }
        if (Spec.Angle1024 is { } a)
        {
            fields.Add(("angle1024", JsValue.Int(a)));
        }
        if (Spec.Length is { } l)
        {
            fields.Add(("length", JsValue.Int(l)));
        }
        if (Spec.Width is { } w)
        {
            fields.Add(("width", JsValue.Int(w)));
        }
        return new SimEvent(DrawTick, "telegraph", fields.ToArray());
    }

    /// <summary>
    /// The draw for <paramref name="spec"/> by <paramref name="caster"/> on <paramref name="tick"/>, or null when it is
    /// not drawn (<see cref="TelegraphOrigin.TryResolve"/> found no origin).
    /// </summary>
    /// <param name="state">The tick's state.</param>
    /// <param name="tick">The draw tick.</param>
    /// <param name="actorId">The caster's id.</param>
    /// <param name="caster">The caster.</param>
    /// <param name="cast">The ability.</param>
    /// <param name="spec">The spec as drawn.</param>
    /// <param name="castStartTick">The spanned cast's start tick, else null.</param>
    /// <returns>The draw, or null.</returns>
    public static TelegraphDraw? At(SimState state, int tick, int actorId, ActorState caster, string cast, TelegraphSpec spec, int? castStartTick)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(spec);
        return TelegraphOrigin.TryResolve(state, caster, spec, out var origin)
            ? new TelegraphDraw(actorId, cast, spec, tick, origin, caster.Yaw, castStartTick)
            : null;
    }
}

/// <summary>The one telegraph anchor rule.</summary>
public static class TelegraphOrigin
{
    /// <summary>
    /// TELEGRAPH-ANCHOR (finding f-ebb50027, BIBLE s3 "never a lie about where"): a telegraph's origin is its caster's
    /// position, or, for a <see cref="TelegraphSpec.AnchorTarget"/> spec, the caster's current target's position
    /// (integer mm). With no living target on this tick it has no origin and is not drawn: a shape at the caster would
    /// tell the party the wrong ground.
    /// </summary>
    /// <param name="state">The tick's state.</param>
    /// <param name="caster">The caster.</param>
    /// <param name="spec">The spec.</param>
    /// <param name="origin">The origin, when true.</param>
    /// <returns>False when the spec anchors on a target that is missing or dead.</returns>
    public static bool TryResolve(SimState state, ActorState caster, TelegraphSpec spec, out Vec3 origin)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(spec);
        origin = caster.Pos;
        if (!spec.AnchorTarget)
        {
            return true;
        }
        if (caster.TargetId is not { } targetId || !state.Actors.TryGetValue(targetId, out var target) || !target.Alive)
        {
            origin = default;
            return false;
        }
        origin = target.Pos;
        return true;
    }
}
