namespace Keyrun.Content;

/// <summary>An 8-bit RGBA colour (0-255 per channel), integer so Keyrun.Content stays float-free.</summary>
/// <param name="R">Red.</param>
/// <param name="G">Green.</param>
/// <param name="B">Blue.</param>
/// <param name="A">Alpha.</param>
public readonly record struct Rgba8(int R, int G, int B, int A);

/// <summary>
/// How an ability bar slot looks for its state (lane ABILITYBAR-RANGE-TINT). Godot-free so xUnit can pin the draw
/// decision; src/Hud/AbilityBar.cs applies it.
/// </summary>
public static class AbilitySlotLook
{
    /// <summary>The icon's normal modulate (a near-white over the white glyph).</summary>
    public static readonly Rgba8 IconTint = new(235, 237, 247, 255);

    /// <summary>The slot's normal edge.</summary>
    public static readonly Rgba8 SlotEdge = new(115, 120, 140, 255);

    /// <summary>The out-of-range icon modulate: WoW's red vertex tint (about 0.8, 0.1, 0.1) over the glyph.</summary>
    public static readonly Rgba8 OutOfRangeTint = new(204, 26, 26, 255);

    /// <summary>The icon modulate for a slot.</summary>
    /// <param name="outOfRange">The cast's target is out of range.</param>
    /// <returns>The modulate colour.</returns>
    public static Rgba8 IconModulate(bool outOfRange) => outOfRange ? OutOfRangeTint : IconTint;

    /// <summary>The slot edge colour.</summary>
    /// <param name="outOfRange">The cast's target is out of range.</param>
    /// <returns>The edge colour.</returns>
    public static Rgba8 Edge(bool outOfRange) => SlotEdge; // range reads on the icon, never as a red border

    /// <summary>The slot edge width in pixels.</summary>
    /// <param name="outOfRange">The cast's target is out of range.</param>
    /// <returns>The edge width.</returns>
    public static int EdgeWidth(bool outOfRange) => 2;

    /// <summary>The out-of-range caption, drawn in the slot's top-right corner.</summary>
    public const string OutOfRangeCaption = "RANGE";

    /// <summary>
    /// The slot's state caption (f-682aacb5). BIBLE s2: shape plus colour, never colour alone, so the red range tint
    /// comes with words that give it a reason; an in-range slot draws none.
    /// </summary>
    /// <param name="outOfRange">The cast's target is out of range.</param>
    /// <returns>The caption, or null for none.</returns>
    public static string? Caption(bool outOfRange) => outOfRange ? OutOfRangeCaption : null;

    /// <summary>
    /// Whether a slot shows the out-of-range tint (lane RANGE-TINT-NOTARGET). Like WoW, the tint only reads while the
    /// player has a living hostile target that is farther than the ability's range: never with no target, a dead or
    /// friendly target, a non-hostile or range-less ability, or once the run is over.
    /// </summary>
    /// <param name="target">The player's current target, or null when none is selected.</param>
    /// <param name="hostileAbility">The ability is a hostile single-target cast.</param>
    /// <param name="abilityRangeMm">The ability's range in mm; 0 or less means it has none.</param>
    /// <param name="runOver">The run has ended, for any result (false by default). The ability bar passes it (lane
    /// TINT-RUNOVER): a depleted run can end with the player's target alive and out of range.</param>
    /// <returns>True when the slot is tinted.</returns>
    public static bool ShowsOutOfRange(RangeTarget? target, bool hostileAbility, long abilityRangeMm, bool runOver = false) =>
        !runOver && hostileAbility && abilityRangeMm > 0 &&
        target is { Alive: true, Hostile: true } t && t.RangeMm > abilityRangeMm;
}

/// <summary>The player's current target as the ability bar's range tint sees it (Godot- and sim-free).</summary>
/// <param name="Alive">The target is alive.</param>
/// <param name="Hostile">The target is hostile to the player.</param>
/// <param name="RangeMm">The player's range to the target, in mm.</param>
public readonly record struct RangeTarget(bool Alive, bool Hostile, long RangeMm);
