namespace Keyrun.Content;

/// <summary>
/// Run-level movement rules and arena bounds (game/content/data/run.json, logical path
/// <c>run.json</c>). The frozen TS client kept these as constants in
/// client/src/sim-host/run-setup.ts (BASE_SPEED_MM_PER_TICK :53, ACTOR_RADIUS_MM :54,
/// DUNGEON_BOUNDS :57); content/data had no field for them. Every member is required.
/// </summary>
public sealed record RunConfig
{
    /// <summary>Walk speed and actor radius shared by every actor.</summary>
    public required RunMovement Movement { get; init; }

    /// <summary>The XZ box every actor is clamped into, mm.</summary>
    public required ArenaBounds ArenaBounds { get; init; }
}

/// <summary>Shared actor movement values.</summary>
public sealed record RunMovement
{
    /// <summary>Base walk speed, mm/tick (positive).</summary>
    public required int SpeedMmPerTick { get; init; }

    /// <summary>Actor cylinder radius for push-out, mm (positive).</summary>
    public required int RadiusMm { get; init; }
}

/// <summary>Arena bounds, mm; MinX &lt; MaxX and MinZ &lt; MaxZ.</summary>
public sealed record ArenaBounds
{
    /// <summary>Lowest x.</summary>
    public required int MinX { get; init; }

    /// <summary>Highest x.</summary>
    public required int MaxX { get; init; }

    /// <summary>Lowest z.</summary>
    public required int MinZ { get; init; }

    /// <summary>Highest z.</summary>
    public required int MaxZ { get; init; }
}
