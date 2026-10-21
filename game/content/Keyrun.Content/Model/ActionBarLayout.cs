using System;
using System.Collections.Generic;

namespace Keyrun.Content;

/// <summary>
/// The bottom HUD as pure integer pixel math (x right, y down): the ability bar's slot row (src/Hud/AbilityBar.cs
/// draws exactly these rectangles) and the queue readout line (src/Hud/Queue.cs places its Label on
/// <see cref="QueueReadout"/>). Godot-free so the no-overlap rule is unit-tested in Keyrun.Content.Tests.
/// </summary>
public static class ActionBarLayout
{
    /// <summary>One ability slot's side, pixels.</summary>
    public const int Slot = 78;

    /// <summary>Gap between two slots, pixels.</summary>
    public const int SlotGap = 8;

    /// <summary>Space under the slot row, pixels.</summary>
    public const int BottomMargin = 24;

    /// <summary>The meters band above the slot row (the Warden's health and resource bars), pixels.</summary>
    public const int MetersHeight = 44;

    /// <summary>The queue readout line's top edge offset from the viewport bottom when no run is drawn, pixels.</summary>
    public const int ReadoutBottomOffset = 64;

    /// <summary>The queue readout line's height in its bottom-centre place, pixels.</summary>
    public const int ReadoutHeight = 28;

    /// <summary>The bar's footprint: the meters band on top, the slot row below, centred at the bottom.</summary>
    /// <param name="viewportWidth">Viewport width in pixels.</param>
    /// <param name="viewportHeight">Viewport height in pixels.</param>
    /// <param name="count">Slots drawn, the G ping included.</param>
    /// <returns>The footprint.</returns>
    public static PixelRect Footprint(int viewportWidth, int viewportHeight, int count)
    {
        var width = (count * Slot) + (Math.Max(0, count - 1) * SlotGap);
        var top = viewportHeight - BottomMargin - Slot - MetersHeight;
        return new PixelRect((viewportWidth - width) / 2, top, width, MetersHeight + Slot);
    }

    /// <summary>Every slot rectangle of the bar, left to right (the G ping last).</summary>
    /// <param name="viewportWidth">Viewport width in pixels.</param>
    /// <param name="viewportHeight">Viewport height in pixels.</param>
    /// <param name="count">Slots drawn, the G ping included.</param>
    /// <returns>The slot rectangles.</returns>
    public static IReadOnlyList<PixelRect> SlotRects(int viewportWidth, int viewportHeight, int count)
    {
        var bar = Footprint(viewportWidth, viewportHeight, count);
        var top = bar.Bottom - Slot;
        var rects = new PixelRect[Math.Max(0, count)];
        for (var i = 0; i < rects.Length; i++)
        {
            rects[i] = new PixelRect(bar.X + (i * (Slot + SlotGap)), top, Slot, Slot);
        }
        return rects;
    }

    /// <summary>The party frames' top edge (game/scenes/Hud.tscn PartyFrames offset_top), pixels.</summary>
    public const int PartyTop = 124;

    /// <summary>One party frame's width (src/Hud/PartyFrames.cs FrameWidth), pixels.</summary>
    public const int PartyWidth = 240;

    /// <summary>One party frame plus the gap under it (PartyFrames.cs FrameHeight 68 + Gap 8), pixels.</summary>
    public const int PartyStride = 76;

    /// <summary>The readout's height in the party panel: two wrapped lines of the default Label font, pixels.</summary>
    public const int PartyReadoutHeight = 52;

    /// <summary>
    /// The queue readout line ("Seats filled N of N.", the ready check, the bot fill, the role scarcity). Finding
    /// f-3c1f4542 (the line sat over ability slots 5 and Q): group information lives with the party frames, never over
    /// the action bar (WoW), so in a live run the line is the party panel's text row, directly under the last frame,
    /// at the frames' left inset and width. With no run there is no ability bar or party panel, and the line keeps its
    /// bottom-centre place on the queue screen.
    /// </summary>
    /// <param name="viewportWidth">Viewport width in pixels.</param>
    /// <param name="viewportHeight">Viewport height in pixels.</param>
    /// <param name="partySeats">Player seats in the live run (0: no run, so no ability bar or party frames drawn).</param>
    /// <returns>The readout's rectangle.</returns>
    public static PixelRect QueueReadout(int viewportWidth, int viewportHeight, int partySeats)
    {
        if (partySeats > 0)
        {
            return new PixelRect(HudLayout.Margin, PartyTop + (partySeats * PartyStride), PartyWidth, PartyReadoutHeight);
        }
        return new PixelRect(0, viewportHeight - ReadoutBottomOffset, Math.Max(0, viewportWidth), ReadoutHeight);
    }
}
