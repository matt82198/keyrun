namespace Keyrun.Host;

/// <summary>
/// Which ground marks the 3D view draws (Matt 2026-10-26: "players will learn their aggro range and stuff, they dont
/// need circles around them"). Persistent Window rings, both on an actor's feet and on the floor, are not drawn: the
/// Window state already shows on the party frames. Hazards to step out of, cast telegraphs, Pair marks, Steam discs
/// and the boss Lock and Enrage rings stay.
/// </summary>
public static class GroundMarkRule
{
    /// <summary>Whether the view draws a ground mark of this kind.</summary>
    /// <param name="kind">The mark kind.</param>
    /// <returns>False for the persistent Window rings, true for every other kind.</returns>
    public static bool Draws(GroundMarkKind kind) => kind switch
    {
        GroundMarkKind.ActorWindow => false,
        GroundMarkKind.FloorWindow => false,
        _ => true,
    };
}
