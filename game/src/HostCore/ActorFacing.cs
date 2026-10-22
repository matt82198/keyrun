using System.Collections.Generic;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;

namespace Keyrun.Host;

/// <summary>
/// The facing a view DRAWS an actor with (finding f-e0650754, Matt 2026-10-22: "casters only face the aggrod target ...
/// they often just look backwards in enemy packs and friendly bots"). WoW rule: an actor with a target faces it every
/// frame while casting, attacking or merely aggroed, the yaw taken from the actor's position to the target's position
/// in the sim's integer coordinates. Before this, <see cref="CastFacing"/> and <see cref="SwingFacing"/> only turned an
/// actor for the cast itself or until its first step after a swing, so an aggroed pack member that chased, waited on a
/// cooldown or finished an attack clip drew at its stale movement <see cref="ActorState.Yaw"/>. Render only: the sim
/// never reads it, so <see cref="ActorState.Yaw"/> and every oracle trace are unchanged. Godot-free so tests drive it.
/// </summary>
public static class ActorFacing
{
    /// <summary>
    /// <paramref name="to"/> as seen from <paramref name="from"/>, 1/1024 turn, on the floor plane. When both stand on the
    /// same spot there is no direction, so <paramref name="lastYaw"/> is kept.
    /// </summary>
    /// <param name="from">The facing actor's position.</param>
    /// <param name="to">The position faced.</param>
    /// <param name="lastYaw">The facing to keep when there is no direction.</param>
    /// <returns>The yaw to draw.</returns>
    public static int YawToward(Vec3 from, Vec3 to, int lastYaw) =>
        from.X == to.X && from.Z == to.Z ? lastYaw : Fixed.YawBetween(from, to);

    /// <summary>
    /// The yaw to draw <paramref name="actor"/> with in <paramref name="state"/>, 1/1024 turn. A cone, line or self cast
    /// keeps the sim facing (they resolve along <see cref="ActorState.Yaw"/>, so any other facing would misreport where
    /// they land). A target or circle cast faces its live cast target (<see cref="CastFacing"/>). Otherwise a seat the
    /// player steers (<paramref name="followsInput"/>) keeps <see cref="SwingFacing"/>'s rule (its facing is the player's
    /// camera), and every other actor with a live target other than itself faces it, whether it is attacking, between
    /// attacks or just aggroed. An actor with no usable target keeps <see cref="ActorState.Yaw"/>, which is its movement
    /// facing while it moves and its last facing while it stands still.
    /// </summary>
    /// <param name="state">The sim state the actor is in.</param>
    /// <param name="actor">The actor drawn.</param>
    /// <param name="abilities">The run's abilities by id, or null when no run is loaded.</param>
    /// <param name="hold">The actor's <see cref="SwingFacing"/> hold for <paramref name="state"/>, or null.</param>
    /// <param name="followsInput">True for the seat the human steers.</param>
    /// <returns>The yaw to draw.</returns>
    public static int RenderYaw(
        SimState state, ActorState actor, IReadOnlyDictionary<string, AbilityDef>? abilities, FacingHold? hold, bool followsInput)
    {
        if (!actor.Alive)
        {
            return actor.Yaw;
        }
        if (actor.CastId is not null)
        {
            return CastFacing.RenderYaw(state, actor, abilities);
        }
        if (followsInput)
        {
            return SwingFacing.RenderYaw(state, actor, abilities, hold);
        }
        if (actor.TargetId is int targetId &&
            targetId != actor.Id &&
            state.Actors.TryGetValue(targetId, out var target) &&
            target.Alive)
        {
            return YawToward(actor.Pos, target.Pos, actor.Yaw);
        }
        return actor.Yaw;
    }
}
