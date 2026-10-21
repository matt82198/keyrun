using System.Collections.Generic;

namespace Keyrun.Content;

/// <summary>
/// The floor telegraph shapes src/View/TelegraphView.cs can draw for an enemy (BIBLE s3: a floor telegraph, circle,
/// cone or line, drawn ahead of resolution, shape plus colour). Pure and Godot-free so the content guard reads it:
/// an enemy script step may use a telegraph shape only when <see cref="ViewDrawable"/> holds it, so a shape the view
/// would draw as nothing on the floor cannot ship. Display only; never a rule.
/// </summary>
public static class TelegraphDrawability
{
    /// <summary>
    /// The shapes the view draws today. A shape joins this set in the same change that teaches TelegraphView to draw it.
    /// </summary>
    public static IReadOnlySet<TelegraphShape> ViewDrawable { get; } = new HashSet<TelegraphShape>
    {
        TelegraphShape.Circle,
        TelegraphShape.Cone,
        TelegraphShape.Line,
    };

    /// <summary>Whether the floor view can draw <paramref name="shape"/> for an enemy telegraph.</summary>
    /// <param name="shape">The telegraph's geometry kind.</param>
    /// <returns>True when <see cref="ViewDrawable"/> holds the shape.</returns>
    public static bool CanDraw(TelegraphShape shape) => ViewDrawable.Contains(shape);

    /// <summary>
    /// The four corners of a line telegraph's rectangle, each (forward, lateral) in mm from the caster: forward runs
    /// along the caster's facing from 0 to <paramref name="length"/>, lateral is across it at half the width either side
    /// (<c>Telegraphs.PointInLine</c>: |lateral| * 2 &lt;= width). Corners are in drawing order (back-left, front-left,
    /// front-right, back-right). Content widths are even (facets 1200); an odd width is floored to a whole mm, sub-pixel.
    /// src/View/TelegraphView.cs builds the line mesh from these corners. Pure and Godot-free; display only.
    /// </summary>
    /// <param name="length">Line length, mm.</param>
    /// <param name="width">Line full width, mm.</param>
    /// <returns>The four corners as (forward, lateral) mm.</returns>
    public static (long Forward, long Lateral)[] LineCorners(long length, long width)
    {
        var half = width / 2;
        return new[]
        {
            (0L, -half),
            (length, -half),
            (length, half),
            (0L, half),
        };
    }
}
