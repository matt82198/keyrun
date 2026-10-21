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

/// <summary>A screen point in whole pixels (x right, y down), such as a world anchor projected by the camera.</summary>
/// <param name="X">Pixels from the left edge.</param>
/// <param name="Y">Pixels from the top edge.</param>
public readonly record struct PixelPoint(int X, int Y);

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

    /// <summary>Enemy Forces bar height (HUD-FORCES).</summary>
    public const int ForcesHeight = 16;

    /// <summary>Gap between the Slack bar and the Enemy Forces bar under it.</summary>
    public const int ForcesGap = 4;

    /// <summary>
    /// The Enemy Forces bar (HUD-FORCES, BIBLE s9.2): directly under the Slack/timer bar, same left edge and width, the
    /// way WoW's tracker lists Enemy Forces under the key timer.
    /// </summary>
    /// <param name="bar">The top bar.</param>
    /// <returns>The forces bar slot.</returns>
    public static PixelRect ForcesBar(TopBarLayout bar)
    {
        ArgumentNullException.ThrowIfNull(bar);
        return new PixelRect(bar.Slack.X, bar.Slack.Bottom + ForcesGap, bar.Slack.Width, ForcesHeight);
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

    // HUD-POLISH-2: the over-head cast bars (src/View/CastBarView.cs) and the SPREAD/STACK world labels (src/Hud/Pings.cs).

    /// <summary>Over-head cast bar width (the label above it is fitted to this width).</summary>
    public const int CastBarWidth = 180;

    /// <summary>Over-head cast bar height.</summary>
    public const int CastBarHeight = 16;

    /// <summary>The band above a cast bar its 14 px label draws in.</summary>
    public const int CastLabelHeight = 18;

    /// <summary>The SPREAD/STACK world label font size.</summary>
    public const int PingFont = 16;

    /// <summary>A world label's box: its font size plus the descender.</summary>
    public const int PingLabelHeight = PingFont + 5;

    /// <summary>
    /// How far (each axis) a SPREAD/STACK label may move from its ping when five pile on one body with nothing else in
    /// the way: two label rows up or down, about one body width at run C's camera distance.
    /// </summary>
    public const int PingLabelReach = 48;

    /// <summary>The block a cast bar and its label fill when the bar's top centre sits at <paramref name="anchor"/>.</summary>
    /// <param name="anchor">The caster's head (3 m over the body) projected to the screen.</param>
    /// <returns>The label band plus the bar, <see cref="CastBarWidth"/> wide; the bar's top is at Y + <see cref="CastLabelHeight"/>.</returns>
    public static PixelRect CastBarRect(PixelPoint anchor) =>
        new(anchor.X - (CastBarWidth / 2), anchor.Y - CastLabelHeight, CastBarWidth, CastLabelHeight + CastBarHeight);

    /// <summary>The box a SPREAD/STACK world label fills when its baseline centre sits at <paramref name="anchor"/>.</summary>
    /// <param name="anchor">The ping's world spot projected to the screen.</param>
    /// <param name="textWidth">The label's drawn width in pixels.</param>
    /// <returns>The label's box.</returns>
    public static PixelRect PingLabelRect(PixelPoint anchor, int textWidth) =>
        new(anchor.X - (textWidth / 2), anchor.Y - PingFont, Math.Max(0, textWidth), PingLabelHeight);

    /// <summary>True when any pixel of <paramref name="rect"/> is inside the viewport (else the caller skips it).</summary>
    /// <param name="rect">The bar or label where its anchor projects.</param>
    /// <param name="viewportWidth">Viewport width in pixels.</param>
    /// <param name="viewportHeight">Viewport height in pixels.</param>
    /// <returns>True when part of it would draw on screen.</returns>
    public static bool Shows(PixelRect rect, int viewportWidth, int viewportHeight) =>
        rect.Right > 0 && rect.X < viewportWidth && rect.Bottom > 0 && rect.Y < viewportHeight;

    /// <summary>
    /// Over-head cast bars for <paramref name="anchors"/> (nearest the camera first): each <see cref="CastBarRect"/> is
    /// pulled inside the viewport with the top bar's <see cref="Margin"/> side gutter, then moved the least distance up
    /// or down until it shares no pixel with a bar already placed or an <paramref name="obstacles"/> rectangle
    /// (<see cref="Separate(IReadOnlyList{PixelRect}, int, int, IReadOnlyList{PixelRect})"/>'s rule).
    /// </summary>
    /// <param name="anchors">Each caster's head projected to the screen, nearest first.</param>
    /// <param name="viewportWidth">Viewport width in pixels.</param>
    /// <param name="viewportHeight">Viewport height in pixels.</param>
    /// <param name="obstacles">HUD rectangles the bars keep out of (the top bar band, the party frames).</param>
    /// <returns>One bar block per anchor, in input order.</returns>
    public static IReadOnlyList<PixelRect> CastBars(
        IReadOnlyList<PixelPoint> anchors, int viewportWidth, int viewportHeight, IReadOnlyList<PixelRect> obstacles)
    {
        ArgumentNullException.ThrowIfNull(anchors);
        var rects = new PixelRect[anchors.Count];
        for (var i = 0; i < anchors.Count; i++)
        {
            rects[i] = CastBarRect(anchors[i]);
        }
        return SeparateInGutter(rects, viewportWidth, viewportHeight, obstacles);
    }

    /// <summary>
    /// SPREAD/STACK world labels (in call order): each is pulled inside the viewport with the <see cref="Margin"/> side
    /// gutter and stacked up or down off labels already placed and <paramref name="obstacles"/> (the top bar band, the
    /// party frames, the callout banners, the cast bars), so simultaneous pings on nearby bodies read one per row next to
    /// their spot instead of one pile. Name plates are not an obstacle here: LabelView places plates after these labels
    /// and keeps them clear, so plates and labels still never share a pixel.
    /// </summary>
    /// <param name="labels">Each label's <see cref="PingLabelRect"/> where its ping projects.</param>
    /// <param name="viewportWidth">Viewport width in pixels.</param>
    /// <param name="viewportHeight">Viewport height in pixels.</param>
    /// <param name="obstacles">HUD rectangles the labels keep out of.</param>
    /// <returns>One placed label box per input, in input order.</returns>
    public static IReadOnlyList<PixelRect> PingLabels(
        IReadOnlyList<PixelRect> labels, int viewportWidth, int viewportHeight, IReadOnlyList<PixelRect> obstacles) =>
        SeparateInGutter(labels, viewportWidth, viewportHeight, obstacles);

    /// <summary>
    /// STACK-PING-LABEL: which SPREAD/STACK world labels to draw, as input indices in input order. A label is dropped
    /// when an earlier kept label with the same text sits within <see cref="PingLabelReach"/> of it on both axes: the
    /// Pair's casters each raise their own STACK telegraph at one tick, a few pixels apart, which drew one row per
    /// caster for a single call. One call on one spot draws once; different calls, or the same call on spots further
    /// apart, each keep their label.
    /// </summary>
    /// <param name="texts">Each label's text ("STACK: group up").</param>
    /// <param name="labels">Each label's <see cref="PingLabelRect"/> where its ping projects, in call order.</param>
    /// <returns>The indices of the labels to draw, ascending.</returns>
    public static IReadOnlyList<int> DistinctPings(IReadOnlyList<string> texts, IReadOnlyList<PixelRect> labels)
    {
        ArgumentNullException.ThrowIfNull(texts);
        ArgumentNullException.ThrowIfNull(labels);
        if (texts.Count != labels.Count)
        {
            throw new ArgumentException("one text per label", nameof(texts));
        }
        var kept = new List<int>();
        for (var i = 0; i < labels.Count; i++)
        {
            var same = false;
            foreach (var k in kept)
            {
                if (string.Equals(texts[k], texts[i], StringComparison.Ordinal)
                    && SpanGap(labels[k].X, labels[k].Right, labels[i].X, labels[i].Right) <= PingLabelReach
                    && SpanGap(labels[k].Y, labels[k].Bottom, labels[i].Y, labels[i].Bottom) <= PingLabelReach)
                {
                    same = true;
                    break;
                }
            }
            if (!same)
            {
                kept.Add(i);
            }
        }
        return kept;
    }

    // The empty space between spans [a0, a1) and [b0, b1); 0 when they overlap.
    private static int SpanGap(int a0, int a1, int b0, int b1) => Math.Max(0, Math.Max(a0, b0) - Math.Min(a1, b1));

    /// <summary>
    /// A cast bar's label (f-b78d1567, BIBLE s3 "Cast bars are the enemy's language"): the ability is what the player must
    /// read, so when the caster's name plate is drawn the label is the ability alone (the plate already names the caster);
    /// with no plate it is "caster: spell" so the caster stays identifiable. Too wide for the bar, the caster's name is cut
    /// first (<see cref="FitTitle"/>) and dropped when not one character of it fits beside the whole ability; only then is
    /// the ability itself cut. <paramref name="suffix"/> (the seconds left) is always kept whole.
    /// </summary>
    /// <param name="caster">The caster's display name.</param>
    /// <param name="spell">The ability's display name.</param>
    /// <param name="suffix">Text kept whole after the title ("  2.5s"), or empty.</param>
    /// <param name="barWidth">The bar's width in pixels.</param>
    /// <param name="measure">The bar font's width of a string, in pixels.</param>
    /// <param name="casterPlateShown">True when the caster's name plate is drawn this frame (LabelView.ShowsPlate).</param>
    /// <returns>The label; <paramref name="measure"/> of it is at most <paramref name="barWidth"/> when the suffix fits.</returns>
    public static string FitCastLabel(string caster, string spell, string suffix, int barWidth, Func<string, int> measure,
        bool casterPlateShown)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(spell);
        ArgumentNullException.ThrowIfNull(suffix);
        ArgumentNullException.ThrowIfNull(measure);
        var room = barWidth - measure(suffix);
        if (casterPlateShown || caster.Length == 0)
        {
            return FitTitle(spell, room, measure) + suffix;
        }
        var whole = caster + ": " + spell;
        if (measure(whole) <= room)
        {
            return whole + suffix;
        }
        // Cut the caster, never the ability, while at least one character of the name fits beside the whole ability.
        var tail = ": " + spell;
        var cut = FitTitle(caster, room - measure(tail), measure);
        if (cut.Length > Ellipsis.Length)
        {
            return cut + tail + suffix;
        }
        return FitTitle(spell, room, measure) + suffix;
    }

    // Separate inside [Margin, width - Margin]: shift into a viewport Margin narrower on each side, separate, shift back.
    private static IReadOnlyList<PixelRect> SeparateInGutter(
        IReadOnlyList<PixelRect> rects, int viewportWidth, int viewportHeight, IReadOnlyList<PixelRect> obstacles)
    {
        ArgumentNullException.ThrowIfNull(rects);
        ArgumentNullException.ThrowIfNull(obstacles);
        var shifted = new PixelRect[rects.Count];
        for (var i = 0; i < rects.Count; i++)
        {
            shifted[i] = rects[i].Offset(-Margin, 0);
        }
        var blockers = new PixelRect[obstacles.Count];
        for (var i = 0; i < obstacles.Count; i++)
        {
            blockers[i] = obstacles[i].Offset(-Margin, 0);
        }
        var offsets = Separate(shifted, Math.Max(0, viewportWidth - (2 * Margin)), viewportHeight, blockers);
        var placed = new PixelRect[rects.Count];
        for (var i = 0; i < rects.Count; i++)
        {
            placed[i] = rects[i].Offset(offsets[i].Dx, offsets[i].Dy);
        }
        return placed;
    }
}
