using System.Collections.Generic;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Combat;

/// <summary>
/// The facing a view DRAWS an actor with (lane CAST-FACING, D10: "chanter stands backwards while casting"). Render
/// only: the sim never reads it, so <see cref="ActorState.Yaw"/> (the facing cone and line casts aim along, and the
/// state every oracle trace hashes) is unchanged. C#-only, no TS twin.
/// </summary>
public static class CastFacing
{
    /// <summary>
    /// The yaw to draw <paramref name="actor"/> with in <paramref name="state"/>, 1/1024 turn. Mid-cast of a "target"
    /// or "circle" ability (an enemy-target nuke, an ally-target heal, a ground pulse cast at someone) the actor faces
    /// its live cast target (<see cref="ActorState.TargetId"/>, which StartCast sets). Otherwise it keeps its
    /// movement facing: idle, a "self" cast, a "cone" or "line" cast (they resolve along Yaw, so drawing any other
    /// facing would misreport where they land), a target that is itself, dead, gone or on the same spot, or no run.
    /// The sim has no channelled casts; a channel would be a cast here and follow the same rule.
    /// </summary>
    /// <param name="state">The sim state the actor is in (its cast target is looked up here).</param>
    /// <param name="actor">The actor drawn.</param>
    /// <param name="abilities">The run's abilities by id, or null when no run is loaded.</param>
    /// <returns>The yaw to draw.</returns>
    public static int RenderYaw(SimState state, ActorState actor, IReadOnlyDictionary<string, AbilityDef>? abilities)
    {
        if (actor.CastId is not { } castId ||
            abilities is null ||
            !abilities.TryGetValue(castId, out var def) ||
            def.Shape.Kind is not (ShapeKind.Target or ShapeKind.Circle) ||
            actor.TargetId is not int targetId ||
            targetId == actor.Id ||
            !state.Actors.TryGetValue(targetId, out var target) ||
            !target.Alive ||
            (target.Pos.X == actor.Pos.X && target.Pos.Z == actor.Pos.Z))
        {
            return actor.Yaw;
        }
        return Fixed.YawBetween(actor.Pos, target.Pos);
    }
}
