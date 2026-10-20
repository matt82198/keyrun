namespace Keyrun.Content;

/// <summary>
/// How wide a floor telegraph's fan is drawn (lane TELEGRAPH-CIRCLE). src/View/TelegraphView.cs draws every pending
/// telegraph as a flat fan centred on the caster's facing, apex at the origin, out to the telegraph's radius.
/// A circle carries a radius and no angle (hollowWarden.slam, hollow-warden-boss.trace.jsonl:286), so it spans the
/// full turn; before this lane the view took <c>Angle1024 ?? 0</c> and a circle built zero-area triangles that drew
/// nothing. A cone keeps its own angle; a line still draws as its angle (none, so empty) until it gets its own mesh.
/// Pure and Godot-free so the decision is unit-tested. Display only; never a rule.
/// </summary>
public static class TelegraphFan
{
    /// <summary>One whole turn in the sim's 1/1024-turn angle unit.</summary>
    public const int FullTurn = 1024;

    /// <summary>The fan's full span, 1/1024 turn, for one telegraph.</summary>
    /// <param name="shape">The telegraph's shape: "circle", "cone" or "line".</param>
    /// <param name="angle1024">The telegraph's full cone angle, or null when it carries none.</param>
    /// <returns>The span to draw.</returns>
    public static int Span1024(string shape, int? angle1024) => shape == "circle" ? FullTurn : angle1024 ?? 0;
}
