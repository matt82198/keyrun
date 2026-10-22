using System;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Combat;

public static partial class Casting
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

    /// <summary>
    /// CAST-KEEPS-START-TARGET (finding f-bd8fbbb1, the WoW rule: a cast resolves on the target it started on;
    /// sim/combat/targeting.ts setTarget). The caster's own retarget (a TargetCommand, Rules) goes through here. A
    /// retarget while a cast is in flight first pins the target that cast started on, so the cast still resolves there;
    /// the new target is for the next press. The pin is taken only on a real change mid-cast. The sim's own dead-target
    /// auto-retarget (Runs.RunWiring.FixupTargeting) pins through here too, so a cast whose start target died mid-cast
    /// fizzles rather than hopping (RULING R-RETARGET-1, finding f-f1ab879b).
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

    /// <summary>
    /// The target a cast resolves on once it has ended (<see cref="End"/> returned it): its pinned start target, else
    /// the caster's current target. This is how a completed cast's Windows still resolve on the start target after the
    /// actor's cast (pin included) is already cleared.
    /// </summary>
    /// <param name="actor">The caster.</param>
    /// <param name="cast">The ended cast, or null for none (resolves on the current target).</param>
    /// <returns>The cast's target id, or null.</returns>
    public static int? TargetOfEnded(ActorState actor, CastInFlight? cast)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return cast is { TargetPinned: true } c ? c.PinnedTargetId : actor.TargetId;
    }

    /// <summary>
    /// Drops the pin of the cast in flight and keeps the cast. No production caller since R2 (<see cref="End"/> clears
    /// the pin with the cast); kept for one release with the legacy cast shims.
    /// </summary>
    /// <param name="actor">The caster.</param>
    public static void ReleaseCastTarget(ActorState actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        actor.CastTargetPinned = false;
        actor.CastTargetId = null;
    }
}
