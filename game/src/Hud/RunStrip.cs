using System.Collections.Generic;
using Godot;
using Keyrun.Sim.Runs;

namespace Keyrun.Hud;

/// <summary>
/// The private last-three-runs strip under the Key band (BIBLE s10: "a collapsed, private strip ... no one else can see
/// it"). It draws only <see cref="RunStripText.Lines"/> over the character's saved history, so nothing here reads a
/// run from anywhere else.
/// </summary>
public static class RunStrip
{
    private const int HeaderFont = 12;
    private const int BodyFont = 12;
    private const float LineHeight = 16;

    private static readonly Color Header = new(0.7f, 0.85f, 1f);
    private static readonly Color Body = new(0.88f, 0.83f, 0.72f);

    /// <summary>Draws the strip with its top-left at <paramref name="r"/>'s position.</summary>
    /// <param name="canvas">The node to draw on.</param>
    /// <param name="r">The strip's rectangle; only its position and width are used.</param>
    /// <param name="history">The saved runs, oldest first (<see cref="Progression.History"/>).</param>
    public static void Draw(CanvasItem canvas, Rect2 r, IReadOnlyList<RunRecord> history)
    {
        HudNodes.Text(canvas, r.Position, "LAST RUNS (PRIVATE)", HeaderFont, Header);
        var lines = RunStripText.Lines(history);
        if (lines.Count == 0)
        {
            HudNodes.Text(canvas, r.Position + new Vector2(0, LineHeight), "No runs yet", BodyFont, Body);
            return;
        }
        for (var i = 0; i < lines.Count; i++)
        {
            HudNodes.Text(canvas, r.Position + new Vector2(0, LineHeight * (i + 1)), lines[i], BodyFont, Body);
        }
    }
}
