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
}
