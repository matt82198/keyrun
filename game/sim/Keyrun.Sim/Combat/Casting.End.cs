using System;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Combat;

/// <summary>Why a cast in flight ended (<see cref="Casting.End"/>).</summary>
public enum CastEnd
{
    /// <summary>It resolved (<see cref="Casting.CompleteCastOn"/>, or an unknown ability completing on nothing).</summary>
    Completed,

    /// <summary>The caster cancelled it (<see cref="Casting.Stopcast"/>).</summary>
    Stopped,

    /// <summary>A kick (<see cref="Casting.InterruptCast"/>) or a BIBLE 9.3 control stop (Runs.ControlStops) ended it.</summary>
    Interrupted,

    /// <summary>The seat was released and the actor respawned with no cast (Runs.Release).</summary>
    Released,
}

public static partial class Casting
{
    /// <summary>
    /// The one clearer of a cast in flight (R2): clears <see cref="ActorState.Cast"/> whole, its pin included, and
    /// returns the cast that ended so a caller that still resolves on it (the completing cast's Windows) reads the
    /// pin from the returned value instead of a half-cleared actor. A no-op returning null on an idle actor.
    /// </summary>
    /// <param name="actor">The caster.</param>
    /// <param name="why">Why the cast ended.</param>
    /// <returns>The ended cast, or null when there was none.</returns>
    public static CastInFlight? End(ActorState actor, CastEnd why)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!Enum.IsDefined(why))
        {
            throw new ArgumentOutOfRangeException(nameof(why), why, "unknown CastEnd");
        }
        // A test that wrote an incomplete cast through the legacy set shims has no Cast; it ends from the shims.
        var ended = actor.Cast ?? (actor.CastId is { } id
            ? new CastInFlight(id, actor.CastStartTick ?? 0, actor.CastEndsTick ?? 0, actor.CastIsOverheat, actor.CastTargetId, actor.CastTargetPinned)
            : null);
        actor.Cast = null;
        return ended;
    }
}
