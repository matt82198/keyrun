using System;
using System.Collections.Generic;
using HudLayout = Keyrun.Content.HudLayout;
using PixelPoint = Keyrun.Content.PixelPoint;
using PixelRect = Keyrun.Content.PixelRect;

namespace Keyrun.Hud;

/// <summary>
/// Lane COMBATTEXT-OVERLAP (finding f-caae8653, Matt: "reduce screen clutter"). The Godot-free pixel rules that keep
/// the player's floating damage numbers off the nameplates and HUD panels, and the run-end panel off the player's
/// meters, the ability bar and the party frames. Pure integer pixels (x right, y down), display only; never a rule.
/// </summary>
public static class HudClearance
{
    /// <summary>The side gutter and the gap kept between the run-end panel and the HUD it avoids, pixels.</summary>
    public const int Gutter = 16;

    /// <summary>The name plate's font size (LabelView.NameFont; NAMEPLATE-DECLUTTER owns LabelView).</summary>
    public const int PlateNameFont = 15;

    /// <summary>The name plate's padding around its name (LabelView.PlatePad).</summary>
    public const int PlatePad = 4;

    /// <summary>
    /// The rectangle a name plate takes before plate separation, exactly as LabelView.DrawPlates builds it: the name
    /// plus <see cref="PlatePad"/> on every side, centred on the projected plate anchor, with its bottom edge on it.
    /// </summary>
    /// <param name="anchor">The plate anchor (body plus PlateRule height) projected to the screen.</param>
    /// <param name="nameWidth">The drawn name's width at <see cref="PlateNameFont"/>, rounded up.</param>
    /// <returns>The plate's box.</returns>
    public static PixelRect PlateRect(PixelPoint anchor, int nameWidth)
    {
        var w = nameWidth + (2 * PlatePad);
        var h = PlateNameFont + (2 * PlatePad);
        return new PixelRect(anchor.X - (w / 2), anchor.Y - h, w, h);
    }

    /// <summary>
    /// The box a floating number's glyphs and outline cover, for text drawn left-aligned so that it is centred on
    /// <paramref name="baselineCentre"/> with its baseline there.
    /// </summary>
    /// <param name="baselineCentre">The text's horizontal centre and baseline.</param>
    /// <param name="textWidth">The text's drawn width, rounded up.</param>
    /// <param name="fontSize">The font size (the glyph height above the baseline, digits have no descent).</param>
    /// <param name="outline">The outline width drawn around every glyph.</param>
    /// <returns>The number's box.</returns>
    public static PixelRect NumberRect(PixelPoint baselineCentre, int textWidth, int fontSize, int outline) =>
        new(baselineCentre.X - (textWidth / 2) - outline, baselineCentre.Y - fontSize - outline, textWidth + (2 * outline), fontSize + (2 * outline));

    /// <summary>
    /// Where one floating damage number goes: pulled inside the viewport and moved the least distance up or down (up
    /// on a tie) until it shares no pixel with any <paramref name="avoid"/> rectangle (the nameplates and the HUD
    /// panels) - <see cref="HudLayout.Separate(IReadOnlyList{PixelRect}, int, int, IReadOnlyList{PixelRect})"/>'s rule.
    /// </summary>
    /// <param name="number">The number's box where it would float with no clearance.</param>
    /// <param name="viewportWidth">Viewport width in pixels.</param>
    /// <param name="viewportHeight">Viewport height in pixels.</param>
    /// <param name="avoid">Rectangles the number keeps out of.</param>
    /// <returns>The placed box.</returns>
    public static PixelRect PlaceNumber(PixelRect number, int viewportWidth, int viewportHeight, IReadOnlyList<PixelRect> avoid)
    {
        ArgumentNullException.ThrowIfNull(avoid);
        var offset = HudLayout.Separate(new[] { number }, viewportWidth, viewportHeight, avoid)[0];
        return number.Offset(offset.Dx, offset.Dy);
    }

    /// <summary>
    /// The run-end panel's rectangle: centred in the free area the <paramref name="avoid"/> HUD leaves, a
    /// <see cref="Gutter"/> away from it. A rectangle that spans the viewport's vertical centre line bounds the area
    /// from above (when it sits in the top half: the top bar band) or from below (the ability bar with the player's
    /// health and resource meters); any other bounds it from its side (the party frames on the left). When the
    /// panel does not fit the free width (or height) it is centred in the viewport on that axis (or set on the lower
    /// bound), clamped inside the viewport.
    /// </summary>
    /// <param name="viewportWidth">Viewport width in pixels.</param>
    /// <param name="viewportHeight">Viewport height in pixels.</param>
    /// <param name="panelWidth">The panel's width.</param>
    /// <param name="panelHeight">The panel's height.</param>
    /// <param name="avoid">The HUD rectangles the panel keeps clear of.</param>
    /// <returns>The panel's rectangle.</returns>
    public static PixelRect RunEndPanel(int viewportWidth, int viewportHeight, int panelWidth, int panelHeight, IReadOnlyList<PixelRect> avoid)
    {
        ArgumentNullException.ThrowIfNull(avoid);
        var cx = viewportWidth / 2;
        var cy = viewportHeight / 2;
        int left = Gutter, right = viewportWidth - Gutter, top = Gutter, bottom = viewportHeight - Gutter;
        foreach (var r in avoid)
        {
            if (r.Width <= 0 || r.Height <= 0)
            {
                continue;
            }
            if (r.X <= cx && r.Right > cx)
            {
                if (r.Y + (r.Height / 2) < cy)
                {
                    top = Math.Max(top, r.Bottom + Gutter);
                }
                else
                {
                    bottom = Math.Min(bottom, r.Y - Gutter);
                }
            }
            else if (r.Right <= cx)
            {
                left = Math.Max(left, r.Right + Gutter);
            }
            else
            {
                right = Math.Min(right, r.X - Gutter);
            }
        }
        var maxX = Math.Max(0, viewportWidth - panelWidth);
        var maxY = Math.Max(0, viewportHeight - panelHeight);
        var x = right - left >= panelWidth ? left + ((right - left - panelWidth) / 2) : (viewportWidth - panelWidth) / 2;
        var y = bottom - top >= panelHeight ? top + ((bottom - top - panelHeight) / 2) : bottom - panelHeight;
        return new PixelRect(Math.Clamp(x, 0, maxX), Math.Clamp(y, 0, maxY), panelWidth, panelHeight);
    }

    /// <summary>"x,y,w,h", as the proof lines print a rectangle.</summary>
    /// <param name="r">The rectangle.</param>
    /// <returns>The text.</returns>
    public static string Format(PixelRect r) => FormattableString.Invariant($"{r.X},{r.Y},{r.Width},{r.Height}");
}
