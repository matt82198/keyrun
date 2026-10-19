using System;
using System.Collections.Generic;

namespace Keyrun.Content;

/// <summary>An axis-aligned screen rectangle in whole pixels (x right, y down).</summary>
/// <param name="X">Left edge.</param>
/// <param name="Y">Top edge.</param>
/// <param name="Width">Width, at least 0.</param>
/// <param name="Height">Height, at least 0.</param>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    /// <summary>One past the right edge.</summary>
    public int Right => X + Width;

    /// <summary>One past the bottom edge.</summary>
    public int Bottom => Y + Height;

    /// <summary>True when the two rectangles share any pixel (touching edges do not intersect).</summary>
    /// <param name="other">The other rectangle.</param>
    /// <returns>True on overlap.</returns>
    public bool Intersects(PixelRect other) =>
        X < other.Right && other.X < Right && Y < other.Bottom && other.Y < Bottom;

    /// <summary>This rectangle moved by (<paramref name="dx"/>, <paramref name="dy"/>).</summary>
    /// <param name="dx">Pixels right.</param>
    /// <param name="dy">Pixels down.</param>
    /// <returns>The moved rectangle.</returns>
    public PixelRect Offset(int dx, int dy) => this with { X = X + dx, Y = Y + dy };
}

/// <summary>How far one name plate moves from where its body projects, in pixels.</summary>
/// <param name="Dx">Pixels right (only to keep the plate inside the viewport).</param>
/// <param name="Dy">Pixels down (negative stacks the plate upward).</param>
public readonly record struct PlateOffset(int Dx, int Dy);

/// <summary>The top bar: the objective panel's slot, the Slack bar, and the widest the title text may draw.</summary>
/// <param name="Objective">The largest rectangle the objective panel may fill (it shrinks to its text).</param>
/// <param name="Slack">The Slack bar.</param>
/// <param name="TitleMaxWidth">The widest the pull title (and the task line) may draw inside the panel.</param>
public sealed record TopBarLayout(PixelRect Objective, PixelRect Slack, int TitleMaxWidth)
{
    /// <summary>The full-width band from the top of the viewport to the bottom of the top bar (help line included).</summary>
    /// <param name="viewportWidth">Viewport width in pixels.</param>
    /// <returns>The band name plates keep out of.</returns>
    public PixelRect Band(int viewportWidth) => new(0, 0, Math.Max(0, viewportWidth), Math.Max(Objective.Bottom, Slack.Bottom));
}

/// <summary>
/// HUD layout as pure integer pixel math (design/ART-PLAN.md section 8 lane 8, HUD polish). Godot-free, so the HUD's
/// rules are unit-tested here and src/Hud/{Objective,TimerSlack}.cs and src/View/LabelView.cs only apply them.
/// <list type="bullet">
/// <item>Top bar: the objective panel sits top left under the help line and the Slack bar top right on the same row, so
/// the pull title never runs under the bar (at 1280 px the bar used to be centred at x 460..820, over the title). On a
/// viewport too narrow for both on one row the bar drops under the panel.</item>
/// <item>Name plates: <c>Separate</c> moves each plate up or down (and inside the viewport) so no two plates and
/// no plate and HUD obstacle share a pixel.</item>
/// </list>
/// </summary>
public static class HudLayout
{
    /// <summary>Viewport edge margin (the help line's and party frames' left inset).</summary>
    public const int Margin = 16;

    /// <summary>Top of the objective panel (the help line ends at 48).</summary>
    public const int ObjectiveTop = 54;

    /// <summary>Objective panel height: the title line and the task line.</summary>
    public const int ObjectiveHeight = 44;

    /// <summary>Text inset inside the objective panel, each side.</summary>
    public const int PanelPad = 10;

    /// <summary>Slack bar width.</summary>
    public const int SlackWidth = 360;

    /// <summary>Slack bar height.</summary>
    public const int SlackHeight = 18;

    /// <summary>Top of the Slack bar when it shares the objective's row.</summary>
    public const int SlackTop = 60;

    /// <summary>Horizontal gap between the objective slot and the Slack bar.</summary>
    public const int Gap = 16;

    /// <summary>The narrowest objective slot worth keeping on the Slack bar's row.</summary>
    public const int MinObjectiveWidth = 240;

    /// <summary>Vertical gap between stacked name plates.</summary>
    public const int PlateGap = 2;

    /// <summary>The trailing mark of a clipped title.</summary>
    public const string Ellipsis = "...";

    /// <summary>The top bar for a viewport.</summary>
    /// <param name="viewportWidth">Viewport width in pixels.</param>
    /// <param name="viewportHeight">Viewport height in pixels (the bar is top anchored; kept for callers and tests).</param>
    /// <returns>The layout; the objective slot and the Slack bar never intersect.</returns>
    public static TopBarLayout TopBar(int viewportWidth, int viewportHeight)
    {
        _ = viewportHeight;
        var width = Math.Max(0, viewportWidth);
        var oneRow = width - (2 * Margin) - SlackWidth - Gap;
        PixelRect objective;
        PixelRect slack;
        if (oneRow >= MinObjectiveWidth)
        {
            slack = new PixelRect(width - Margin - SlackWidth, SlackTop, SlackWidth, SlackHeight);
            objective = new PixelRect(Margin, ObjectiveTop, slack.X - Gap - Margin, ObjectiveHeight);
        }
        else
        {
            var inner = Math.Max(0, width - (2 * Margin));
            objective = new PixelRect(Margin, ObjectiveTop, inner, ObjectiveHeight);
            slack = new PixelRect(Margin, objective.Bottom + 6, Math.Min(SlackWidth, inner), SlackHeight);
        }
        return new TopBarLayout(objective, slack, Math.Max(0, objective.Width - (2 * PanelPad)));
    }

    /// <summary>The objective panel's drawn width: its widest line plus the inset, never wider than its slot.</summary>
    /// <param name="titleWidth">The fitted title's width in pixels.</param>
    /// <param name="taskWidth">The fitted task line's width in pixels.</param>
    /// <param name="bar">The top bar.</param>
    /// <returns>Pixels.</returns>
    public static int PanelWidth(int titleWidth, int taskWidth, TopBarLayout bar)
    {
        ArgumentNullException.ThrowIfNull(bar);
        return Math.Min(Math.Max(titleWidth, taskWidth) + (2 * PanelPad), bar.Objective.Width);
    }

    /// <summary>
    /// <paramref name="text"/> unchanged when it fits <paramref name="maxWidth"/>, else its longest prefix that fits with
    /// <see cref="Ellipsis"/> appended (empty when not even the ellipsis fits).
    /// </summary>
    /// <param name="text">The line.</param>
    /// <param name="maxWidth">The widest it may draw, in pixels.</param>
    /// <param name="measure">The HUD font's width of a string, in pixels.</param>
    /// <returns>The fitted line; <paramref name="measure"/> of it is at most <paramref name="maxWidth"/>.</returns>
    public static string FitTitle(string text, int maxWidth, Func<string, int> measure)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(measure);
        if (measure(text) <= maxWidth)
        {
            return text;
        }
        // Binary search the longest prefix p with measure(p + "...") <= maxWidth (width grows with length).
        int lo = 0, hi = text.Length - 1;
        var best = -1;
        while (lo <= hi)
        {
            var mid = lo + ((hi - lo) / 2);
            if (measure(text[..mid].TrimEnd() + Ellipsis) <= maxWidth)
            {
                best = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }
        return best < 0 ? string.Empty : text[..best].TrimEnd() + Ellipsis;
    }

    /// <summary>Name plate de-overlap with no HUD obstacles.</summary>
    /// <param name="plates">Plate rectangles where their bodies project, nearest the camera first.</param>
    /// <param name="viewportWidth">Viewport width in pixels.</param>
    /// <param name="viewportHeight">Viewport height in pixels.</param>
    /// <returns>One offset per plate, in input order.</returns>
    public static IReadOnlyList<PlateOffset> Separate(IReadOnlyList<PixelRect> plates, int viewportWidth, int viewportHeight) =>
        Separate(plates, viewportWidth, viewportHeight, Array.Empty<PixelRect>());

    /// <summary>
    /// Name plate de-overlap. Plates are placed in order (nearest the camera first, so the nearest keeps its spot):
    /// each is pulled inside the viewport horizontally, then moved the least distance up or down (up on a tie) to the
    /// nearest position where it shares no pixel with a plate already placed or an <paramref name="obstacles"/>
    /// rectangle and stays inside the viewport. A plate with no such position keeps its clamped spot.
    /// </summary>
    /// <param name="plates">Plate rectangles where their bodies project, nearest the camera first.</param>
    /// <param name="viewportWidth">Viewport width in pixels.</param>
    /// <param name="viewportHeight">Viewport height in pixels.</param>
    /// <param name="obstacles">HUD rectangles plates keep out of (the top bar band, the party frames).</param>
    /// <returns>One offset per plate, in input order.</returns>
    public static IReadOnlyList<PlateOffset> Separate(
        IReadOnlyList<PixelRect> plates, int viewportWidth, int viewportHeight, IReadOnlyList<PixelRect> obstacles)
    {
        ArgumentNullException.ThrowIfNull(plates);
        ArgumentNullException.ThrowIfNull(obstacles);
        var blockers = new List<PixelRect>(obstacles);
        var offsets = new PlateOffset[plates.Count];
        for (var i = 0; i < plates.Count; i++)
        {
            var p = plates[i];
            var x = Math.Clamp(p.X, 0, Math.Max(0, viewportWidth - p.Width));
            var maxY = Math.Max(0, viewportHeight - p.Height);
            var desired = Math.Clamp(p.Y, 0, maxY);
            var y = Place(p with { X = x }, desired, maxY, blockers);
            var placed = new PixelRect(x, y, p.Width, p.Height);
            blockers.Add(placed);
            offsets[i] = new PlateOffset(x - p.X, y - p.Y);
        }
        return offsets;
    }

    private static int Place(PixelRect plate, int desired, int maxY, List<PixelRect> blockers)
    {
        bool Free(int y)
        {
            if (y < 0 || y > maxY)
            {
                return false;
            }
            var r = plate with { Y = y };
            foreach (var b in blockers)
            {
                if (r.Intersects(b))
                {
                    return false;
                }
            }
            return true;
        }

        if (Free(desired))
        {
            return desired;
        }
        // The free spot nearest above (below) the desired one is flush against some blocker's top (bottom) edge.
        int? up = null, down = null;
        foreach (var b in blockers)
        {
            if (b.Right <= plate.X || plate.Right <= b.X)
            {
                continue;
            }
            var above = b.Y - PlateGap - plate.Height;
            if (above < desired && (up is null || above > up) && Free(above))
            {
                up = above;
            }
            var below = b.Bottom + PlateGap;
            if (below > desired && (down is null || below < down) && Free(below))
            {
                down = below;
            }
        }
        if (up is null && down is null)
        {
            return desired;
        }
        if (down is null)
        {
            return up!.Value;
        }
        if (up is null)
        {
            return down.Value;
        }
        return desired - up.Value <= down.Value - desired ? up.Value : down.Value;
    }
}
