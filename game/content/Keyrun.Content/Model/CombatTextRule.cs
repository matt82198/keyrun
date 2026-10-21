using System;
using System.Collections.Generic;

namespace Keyrun.Content;

/// <summary>
/// Which sim "miss" events float a "MISS" text in the world (lane MISS-VIEW, finding f-8a1e4403). PR #223 made a
/// blinded source's hit emit "miss" (sourceId, targetId, rawAmount) in place of "damage"
/// (game/sim/Keyrun.Sim/Combat/Mitigation.cs DealDamage). HUD-DECLUTTER (Matt 2026-10-19: "only show the played
/// characters damage numbers in fight") limits floating text to the human's seat, so only the human's own miss shows;
/// a bot's or an enemy's miss shows nothing. Pure and Godot-free: src/Hud/CombatText.cs reads it, so what is drawn and
/// the proof line it prints come from the same decision. Display only; never a rule.
/// </summary>
public static class CombatTextRule
{
    /// <summary>The sim event type a blinded source's hit emits in place of "damage".</summary>
    public const string MissEventType = "miss";

    /// <summary>The text a human miss floats over its target.</summary>
    public const string MissText = "MISS";

    /// <summary>
    /// True when one sim event floats a <see cref="MissText"/> over its target: a <see cref="MissEventType"/> event
    /// whose source is the human's seat and that names a target. Any other event type, a bot's or an enemy's miss, and
    /// a miss with no source or target id spawn nothing.
    /// </summary>
    /// <param name="eventType">The event's type.</param>
    /// <param name="sourceId">The event's sourceId, or null when it carries none.</param>
    /// <param name="targetId">The event's targetId, or null when it carries none.</param>
    /// <param name="humanActorId">The played character's actor id.</param>
    /// <param name="missedTargetId">The actor the text floats over (0 when no text).</param>
    /// <returns>True when a MISS text is shown.</returns>
    public static bool HumanMiss(string eventType, long? sourceId, long? targetId, int humanActorId, out int missedTargetId)
    {
        missedTargetId = 0;
        if (eventType != MissEventType || sourceId != humanActorId || targetId is not { } target)
        {
            return false;
        }
        missedTargetId = (int)target;
        return true;
    }

    /// <summary>The sim event type a stun-immune actor's refused stun emits (ControlStops.RefuseImmuneStuns, #429).</summary>
    public const string ImmuneEventType = "immune";

    /// <summary>The text a human's refused effect floats over its target (WoW floats "Immune" over the target).</summary>
    public const string ImmuneText = "IMMUNE";

    /// <summary>
    /// Finding f-04dba988 (WoW floats "Immune" over a target that refuses an effect): true when one sim event floats an
    /// <see cref="ImmuneText"/> over its target: an <see cref="ImmuneEventType"/> event (#429: a stun-immune actor
    /// refused a stun; sourceId is who applied it, targetId the immune actor) whose source is the human's seat and that
    /// names a target. HUD-DECLUTTER limits floating text to the human's seat, so a bot's or an enemy's refused effect
    /// shows nothing, as does any other event type (a stun that lands emits no immune event) and an immune event with
    /// no source or target id.
    /// </summary>
    /// <param name="eventType">The event's type.</param>
    /// <param name="sourceId">The event's sourceId, or null when it carries none.</param>
    /// <param name="targetId">The event's targetId, or null when it carries none.</param>
    /// <param name="humanActorId">The played character's actor id.</param>
    /// <param name="immuneTargetId">The actor the text floats over (0 when no text).</param>
    /// <returns>True when an IMMUNE text is shown.</returns>
    public static bool HumanImmune(string eventType, long? sourceId, long? targetId, int humanActorId, out int immuneTargetId)
    {
        immuneTargetId = 0;
        if (eventType != ImmuneEventType || sourceId != humanActorId || targetId is not { } target)
        {
            return false;
        }
        immuneTargetId = (int)target;
        return true;
    }

    // Lane DMG-NUMBERS (finding f-07a02dc5): where a floating number draws.

    /// <summary>Pixels between the target's plate edge and a number placed beside it.</summary>
    public const int SideGap = 8;

    /// <summary>Pixels a number keeps from every rectangle it avoids (plates, cast bars, HUD blocks, other numbers).</summary>
    public const int Clearance = 3;

    /// <summary>How far a number drifts sideways, away from the plate, over its life.</summary>
    public const int DriftSidePx = 24;

    /// <summary>How far a number drifts down, away from the plate and the cast bar over it, over its life.</summary>
    public const int DriftDownPx = 16;

    /// <summary>The most a number is moved up or down off its spot beside the plate before that side counts as blocked.</summary>
    public const int MaxShiftPx = 48;

    /// <summary>A number's whole life in permille: <see cref="PlaceNumber"/> takes age as an integer 0 to this (purity rule, integer-only).</summary>
    public const int AgeScale = 1000;

    /// <summary>
    /// Where one floating number (a damage number or <see cref="MissText"/>) draws, given its target's plate as it is
    /// drawn (after LabelView's separation). The number never spawns on the plate or the cast bar over the target's
    /// head: it starts level with the plate, <see cref="SideGap"/> past its right edge, and drifts
    /// <see cref="DriftSidePx"/> further sideways and <see cref="DriftDownPx"/> down over its life, away from the plate.
    /// It is then moved the least distance up or down (<see cref="HudLayout.Separate(IReadOnlyList{PixelRect}, int, int, IReadOnlyList{PixelRect})"/>'s
    /// rule, at most <see cref="MaxShiftPx"/>) until it is <see cref="Clearance"/> clear of every
    /// <paramref name="avoid"/> rectangle and of <paramref name="beside"/>. When the right side leaves the viewport or
    /// has no clear spot, the mirror spot on the plate's left is tried; with neither, the number is not drawn (null)
    /// rather than drawn over a plate or a bar. Pure, display only; never a rule.
    /// </summary>
    /// <param name="beside">The target's drawn plate (or, with no plate on screen, the box the number would float in).</param>
    /// <param name="width">The number's box width (text plus outline).</param>
    /// <param name="height">The number's box height (font plus outline).</param>
    /// <param name="agePermille">The number's age in permille of its life, 0 to <see cref="AgeScale"/> (clamped).</param>
    /// <param name="viewportWidth">Viewport width in pixels.</param>
    /// <param name="viewportHeight">Viewport height in pixels.</param>
    /// <param name="avoid">The plates, cast bars, HUD blocks and numbers already placed this frame.</param>
    /// <returns>The number's box, or null when it has no clear spot this frame.</returns>
    public static PixelRect? PlaceNumber(
        PixelRect beside, int width, int height, int agePermille, int viewportWidth, int viewportHeight, IReadOnlyList<PixelRect> avoid)
    {
        ArgumentNullException.ThrowIfNull(avoid);
        var p = Math.Clamp(agePermille, 0, AgeScale);
        var side = SideGap + Drift(DriftSidePx, p);
        var y = beside.Y + ((beside.Height - height) / 2) + Drift(DriftDownPx, p);
        var grown = new List<PixelRect>(avoid.Count + 1) { Grow(beside) };
        foreach (var r in avoid)
        {
            grown.Add(Grow(r));
        }
        foreach (var x in new[] { beside.Right + side, beside.X - side - width })
        {
            var box = new PixelRect(x, y, width, height);
            if (box.X < 0 || box.Right > viewportWidth)
            {
                continue;
            }
            var offset = HudLayout.Separate(new[] { box }, viewportWidth, viewportHeight, grown)[0];
            var placed = box.Offset(offset.Dx, offset.Dy);
            if (offset.Dx == 0 && Math.Abs(offset.Dy) <= MaxShiftPx && Clear(placed, grown, viewportHeight))
            {
                return placed;
            }
        }
        return null;
    }

    // Integer drift for an age in permille, rounded half up (p is clamped to 0..AgeScale, so it never goes negative).
    private static int Drift(int px, int p) => ((px * p) + (AgeScale / 2)) / AgeScale;

    private static PixelRect Grow(PixelRect r) =>
        new(r.X - Clearance, r.Y - Clearance, r.Width + (2 * Clearance), r.Height + (2 * Clearance));

    private static bool Clear(PixelRect box, List<PixelRect> grown, int viewportHeight)
    {
        if (box.Y < 0 || box.Bottom > viewportHeight)
        {
            return false;
        }
        foreach (var r in grown)
        {
            if (box.Intersects(r))
            {
                return false;
            }
        }
        return true;
    }
}
