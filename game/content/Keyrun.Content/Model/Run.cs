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

    /// <summary>The Cinder Heat/Overheat rule's Chill (BIBLE section 5); not in the frozen content/data.</summary>
    public required HeatOverheatConfig HeatOverheat { get; init; }
}

/// <summary>
/// BIBLE section 5 (Cinder): "At 100 the next cast is a free Overheat that dumps Heat to 0 and applies Chill (slower
/// casts for 3 s)." The threshold is the heat-overheat class loop max (classes/cinder.json loop.max); this names the
/// Chill the Overheat applies to the caster.
/// </summary>
public sealed record HeatOverheatConfig
{
    /// <summary>The effect id the Overheat applies (must exist in the effects; "chill", hasteBp -3000).</summary>
    public required string ChillEffectId { get; init; }

    /// <summary>Chill duration, ticks (positive; 60 = 3 s at 20 Hz).</summary>
    public required int ChillTicks { get; init; }
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
