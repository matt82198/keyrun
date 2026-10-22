using System;
using System.Collections.Generic;

namespace Keyrun.Content;

/// <summary>
/// The pure pixel rules for the ability bar's tooltip (Matt 2026-10-22): when it shows (a slot hovered with the mouse,
/// or its key held longer than <see cref="HoldMs"/>), how its text wraps, and where its box goes: anchored above the
/// bar over the slot it describes, kept inside the viewport and clear of the party frames and the top bar (objective,
/// timer, forces). Integer pixels, x right, y down; display only, never a rule.
/// </summary>
public static class AbilityTooltipLayout
{
    /// <summary>A key held this long (milliseconds) shows its slot's tooltip.</summary>
    public const int HoldMs = 300;

    /// <summary>The box width, pixels.</summary>
    public const int Width = 380;

    /// <summary>Padding inside the box, pixels.</summary>
    public const int Pad = 10;

    /// <summary>The gap kept between the box and the bar (and every rect it avoids), pixels.</summary>
    public const int Gap = 12;

    /// <summary>
    /// The band above the bar the box stays above: the range error line (AbilityBar draws it in the 34 px over the
    /// meters) so the tooltip never hides a failed press's reason.
    /// </summary>
    public const int ErrorBand = 34;

    /// <summary>The title's font size; the other lines use <see cref="BodyFont"/>.</summary>
    public const int TitleFont = 18;

    /// <summary>The role, body, stats and flavor lines' font size.</summary>
    public const int BodyFont = 14;

    /// <summary>Extra pixels between lines.</summary>
    public const int LineGap = 4;

    /// <summary>True when a key held for <paramref name="heldMs"/> shows its tooltip.</summary>
    /// <param name="heldMs">How long the key has been down, milliseconds.</param>
    /// <returns>True past the hold threshold.</returns>
    public static bool HoldShows(int heldMs) => heldMs > HoldMs;

    /// <summary>Greedy word wrap of <paramref name="text"/> to <paramref name="maxWidth"/> pixels.</summary>
    /// <param name="text">The text.</param>
    /// <param name="maxWidth">The widest a line may be.</param>
    /// <param name="measure">A line's width in pixels.</param>
    /// <returns>The lines (a single word wider than the box stays on its own line).</returns>
    public static IReadOnlyList<string> Wrap(string text, int maxWidth, Func<string, int> measure)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(measure);
        var lines = new List<string>();
        var line = string.Empty;
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = line.Length == 0 ? word : line + " " + word;
            if (line.Length > 0 && measure(candidate) > maxWidth)
            {
                lines.Add(line);
                line = word;
            }
            else
            {
                line = candidate;
            }
        }
        if (line.Length > 0)
        {
            lines.Add(line);
        }
        return lines;
    }

    /// <summary>The box height for a title line and <paramref name="bodyLines"/> body lines.</summary>
    /// <param name="bodyLines">Lines after the title.</param>
    /// <returns>Pixels.</returns>
    public static int Height(int bodyLines) =>
        (2 * Pad) + TitleFont + LineGap + (Math.Max(0, bodyLines) * (BodyFont + LineGap));

    /// <summary>
    /// The box: centred over <paramref name="slot"/>, its bottom <see cref="Gap"/> above the error band over
    /// <paramref name="bar"/>, clamped into the viewport with a <see cref="HudLayout.Margin"/> gutter, then pushed right of
    /// (or, failing that, left of) any <paramref name="avoid"/> rect it would overlap.
    /// </summary>
    /// <param name="viewportWidth">Viewport width.</param>
    /// <param name="viewportHeight">Viewport height.</param>
    /// <param name="slot">The described slot.</param>
    /// <param name="bar">The bar's footprint (meters and slots).</param>
    /// <param name="height">The box height (<see cref="Height"/>).</param>
    /// <param name="avoid">Rects the box must not cover (party frames, the top bar).</param>
    /// <returns>The box.</returns>
    public static PixelRect Place(int viewportWidth, int viewportHeight, PixelRect slot, PixelRect bar, int height, IReadOnlyList<PixelRect> avoid)
    {
        ArgumentNullException.ThrowIfNull(avoid);
        var width = Math.Min(Width, Math.Max(0, viewportWidth - (2 * HudLayout.Margin)));
        var x = Clamp(slot.X + (slot.Width / 2) - (width / 2), viewportWidth, width);
        var y = Math.Max(HudLayout.Margin, bar.Y - ErrorBand - Gap - height);
        var box = new PixelRect(x, y, width, height);
        foreach (var r in avoid)
        {
            if (!box.Intersects(r))
            {
                continue;
            }
            var right = r.Right + Gap;
            var left = r.X - Gap - width;
            if (right + width <= viewportWidth - HudLayout.Margin)
            {
                box = box with { X = right };
            }
            else if (left >= HudLayout.Margin)
            {
                box = box with { X = left };
            }
        }
        _ = viewportHeight;
        return box;
    }

    /// <summary>True when <paramref name="box"/> lies inside the viewport and clear of every <paramref name="avoid"/> rect.</summary>
    /// <param name="box">The box.</param>
    /// <param name="viewportWidth">Viewport width.</param>
    /// <param name="viewportHeight">Viewport height.</param>
    /// <param name="avoid">Rects it must not cover.</param>
    /// <returns>True when it fits.</returns>
    public static bool Fits(PixelRect box, int viewportWidth, int viewportHeight, IReadOnlyList<PixelRect> avoid)
    {
        ArgumentNullException.ThrowIfNull(avoid);
        if (box.X < 0 || box.Y < 0 || box.Right > viewportWidth || box.Bottom > viewportHeight)
        {
            return false;
        }
        foreach (var r in avoid)
        {
            if (box.Intersects(r))
            {
                return false;
            }
        }
        return true;
    }

    private static int Clamp(int x, int viewportWidth, int width) =>
        Math.Max(HudLayout.Margin, Math.Min(x, viewportWidth - HudLayout.Margin - width));
}
