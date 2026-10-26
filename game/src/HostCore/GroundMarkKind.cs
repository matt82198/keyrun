namespace Keyrun.Host;

/// <summary>
/// The kinds of ground mark the 3D view can draw at a point or around an actor (see <see cref="GroundMarkRule"/>).
/// </summary>
public enum GroundMarkKind
{
    /// <summary>A target or self Window ring at an actor's feet (the persistent Window state).</summary>
    ActorWindow,

    /// <summary>A floor-patch Window ring (e.g. Bulwark) drawn at its centre.</summary>
    FloorWindow,

    /// <summary>A cast telegraph on the floor: a hazard to step out of.</summary>
    Telegraph,

    /// <summary>The Pair mark Spread ring.</summary>
    SpreadMark,

    /// <summary>The Pair mark Stack ring.</summary>
    StackMark,

    /// <summary>A Steam cloud disc (a combo hazard).</summary>
    SteamDisc,

    /// <summary>The boss Lock ring.</summary>
    LockRing,

    /// <summary>The boss Enrage ring.</summary>
    EnrageRing,
}
