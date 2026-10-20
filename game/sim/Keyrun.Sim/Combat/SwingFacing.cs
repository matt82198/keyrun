using System.Collections.Generic;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Combat;

/// <summary>
/// The actor a swinger stays drawn facing after an instant swing (lane KF1-FACING), and where it stood when it swung.
/// Render only: the sim never reads it.
/// </summary>
/// <param name="TargetId">The actor the resolved ability was aimed at.</param>
/// <param name="From">The swinger's position on the resolve tick; any move releases the hold.</param>
public readonly record struct FacingHold(int TargetId, Vec3 From);

/// <summary>
/// The facing a view DRAWS a swinger with (lane KF1-FACING, demo verdict KF1: "they face a random direction and swing
/// the air"). An instant ability starts and resolves inside one step, so <see cref="ActorState.CastId"/> is never set
/// on a state the view draws and <see cref="CastFacing"/> keeps the movement facing for the whole swing. This keeps
/// the struck target as a <see cref="FacingHold"/>: from the resolve tick the swinger is drawn facing it until it moves,
/// the target dies or is gone, or the swinger resolves a "self", "cone" or "line" ability (those resolve along
/// <see cref="ActorState.Yaw"/>, so drawing any other facing would misreport where they land). Render only: the sim
/// never reads it, so <see cref="ActorState.Yaw"/> and every oracle trace are unchanged. C#-only, no TS twin.
/// </summary>
public static class SwingFacing
{
    /// <summary>
    /// The hold after one step. A <c>cast_complete</c> for <paramref name="actor"/> in <paramref name="events"/> of a
    /// "target" or "circle" ability whose <see cref="ActorState.TargetId"/> is another live actor starts a new hold at
    /// the actor's position; a <c>cast_complete</c> of any other ability (or one with no valid target) clears it;
    /// otherwise <paramref name="previous"/> is kept while it is still valid (see <see cref="RenderYaw"/>).
    /// </summary>
    /// <param name="state">The state after the step.</param>
    /// <param name="actor">The actor drawn, in <paramref name="state"/>.</param>
    /// <param name="events">That step's events.</param>
    /// <param name="abilities">The run's abilities by id, or null when no run is loaded.</param>
    /// <param name="previous">The hold before the step, or null.</param>
    /// <returns>The hold to draw <paramref name="state"/> with, or null.</returns>
    public static FacingHold? After(
        SimState state, ActorState actor, IReadOnlyList<SimEvent>? events, IReadOnlyDictionary<string, AbilityDef>? abilities, FacingHold? previous)
    {
        if (events is not null)
        {
            for (var i = events.Count - 1; i >= 0; i--)
            {
                var e = events[i];
                if (e.Type != "cast_complete" || e.Get("actorId") is not JsInt id || id.Value != actor.Id)
                {
                    continue;
                }
                if (e.Get("abilityId") is JsString ability &&
                    abilities is not null &&
                    abilities.TryGetValue(ability.Value, out var def) &&
                    def.Shape.Kind is ShapeKind.Target or ShapeKind.Circle &&
                    actor.TargetId is int targetId)
                {
                    _ = targetId; // MUTANT (implementation removed): a swing never starts a hold
                    return null;
                }
                return null;
            }
        }
        return previous is { } p && Valid(state, actor, p) ? p : null;
    }

    /// <summary>
    /// The yaw to draw <paramref name="actor"/> with, 1/1024 turn. Mid-cast it is
    /// <see cref="CastFacing.RenderYaw"/> (an aimed cast faces its cast target, a cone keeps its sim facing). Otherwise a
    /// valid <paramref name="hold"/> (the actor has not moved since the swing, the target is another live actor not on
    /// the same spot) faces the held target, and anything else keeps the movement facing.
    /// </summary>
    /// <param name="state">The sim state the actor is in.</param>
    /// <param name="actor">The actor drawn.</param>
    /// <param name="abilities">The run's abilities by id, or null when no run is loaded.</param>
    /// <param name="hold">The actor's hold for <paramref name="state"/> (<see cref="After"/>), or null.</param>
    /// <returns>The yaw to draw.</returns>
    public static int RenderYaw(SimState state, ActorState actor, IReadOnlyDictionary<string, AbilityDef>? abilities, FacingHold? hold)
    {
        if (actor.CastId is not null || hold is not { } h || !Valid(state, actor, h))
        {
            return CastFacing.RenderYaw(state, actor, abilities);
        }
        return Fixed.YawBetween(actor.Pos, state.Actors[h.TargetId].Pos);
    }

    private static bool Valid(SimState state, ActorState actor, FacingHold hold) =>
        actor.Alive &&
        hold.TargetId != actor.Id &&
        actor.Pos.X == hold.From.X && actor.Pos.Z == hold.From.Z &&
        state.Actors.TryGetValue(hold.TargetId, out var target) &&
        target.Alive &&
        (target.Pos.X != actor.Pos.X || target.Pos.Z != actor.Pos.Z);
}
