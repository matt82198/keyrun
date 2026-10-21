namespace Keyrun.Content;

/// <summary>How an ability slot shows its range state without colour (lane RANGE-READOUT).</summary>
/// <param name="Glyph">The slot draws the range glyph (a double-headed distance arrow in its bottom-left corner).</param>
/// <param name="IconAlphaPct">The icon's alpha, percent of normal: a dimmed icon reads as "cannot cast now".</param>
public readonly record struct SlotRangeCue(bool Glyph, int IconAlphaPct);

/// <summary>
/// Lane RANGE-READOUT (finding f-682aacb5): the ability bar's out-of-range state must not read by colour alone
/// (BIBLE s3, research/06: shape plus colour, never colour alone). <see cref="AbilitySlotLook"/> keeps the WoW red icon
/// tint; this adds the non-colour cues: the icon dims and a small range glyph draws in the slot while (and only while)
/// it is out of range, and a press the sim would reject as <c>out_of_range</c> (Casting.StartCast) shows a short
/// "Out of range" error line above the bar. Nothing here is permanent text. Godot-free so xUnit pins the decisions;
/// src/Hud/AbilityBar.cs applies them.
/// </summary>
public static class RangeReadout
{
    /// <summary>The error line a press that fails for range shows (WoW's own wording).</summary>
    public const string ErrorText = "Out of range";

    /// <summary>Sim ticks the error line stays up after the press: 1.5 s at 20 Hz.</summary>
    public const int ErrorTicks = 30;

    /// <summary>The out-of-range icon alpha, percent of normal.</summary>
    public const int OutOfRangeIconAlphaPct = 50;

    /// <summary>The non-colour range cue for a slot.</summary>
    /// <param name="outOfRange">The slot reads out of range (<see cref="AbilitySlotLook.ShowsOutOfRange"/>).</param>
    /// <returns>The glyph and icon alpha to draw.</returns>
    public static SlotRangeCue Cue(bool outOfRange) =>
        outOfRange ? new SlotRangeCue(true, OutOfRangeIconAlphaPct) : new SlotRangeCue(false, 100);

    /// <summary>
    /// Whether a key press of a slot fails for range, mirroring Casting.StartCast's order: a press the sim rejects
    /// earlier (on cooldown, short of resource) never reads as range; otherwise a target-shape ability whose cast target
    /// is alive and farther than its range (strictly, as the sim's <c>RangeMm &gt; def.Range</c>) fails for range.
    /// </summary>
    /// <param name="castTarget">The target the press is sent at (CastTargeting.For), or null when none.</param>
    /// <param name="targetShape">The ability is a target-shape cast (only those check range).</param>
    /// <param name="abilityRangeMm">The ability's range in mm.</param>
    /// <param name="blockedEarlier">The sim rejects the press before its range check.</param>
    /// <returns>True when the "Out of range" error line should show.</returns>
    public static bool PressFailsForRange(RangeTarget? castTarget, bool targetShape, long abilityRangeMm, bool blockedEarlier) =>
        !blockedEarlier && targetShape && castTarget is { Alive: true } t && t.RangeMm > abilityRangeMm;

    /// <summary>Whether the error line shows at <paramref name="now"/>.</summary>
    /// <param name="shownAtTick">The tick of the last press that failed for range, or null for none.</param>
    /// <param name="now">The current sim tick.</param>
    /// <returns>True for <see cref="ErrorTicks"/> ticks after the press.</returns>
    public static bool ErrorVisible(int? shownAtTick, int now) =>
        shownAtTick is { } at && now >= at && now - at < ErrorTicks;
}
