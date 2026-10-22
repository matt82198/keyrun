using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Core;
using Keyrun.Sim.Runs;

namespace Keyrun.Hud;

/// <summary>
/// The objective line (ENGINE-PLAN 3C): the current pull's number and content name (never its designer teaches note),
/// and how many of its enemies remain, top left under the key help. Reads the sim state only.
/// </summary>
public partial class Objective : Control
{
    private const int TitleFont = 18;
    private const int TaskFont = 14;

    private SimDriver? _driver;
    private string? _logged;

    /// <summary>The driver to read the run from.</summary>
    [Export]
    public NodePath DriverPath { get; set; } = new("../../../SimDriver");

    /// <inheritdoc />
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        _driver = HudNodes.FindDriver(this, DriverPath);
        if (_driver is not null)
        {
            _driver.Stepped += OnStepped;
            _driver.RunStarted += OnRunStarted;
            OnRunStarted();
        }
    }

    /// <inheritdoc />
    public override void _ExitTree()
    {
        if (_driver is not null)
        {
            _driver.Stepped -= OnStepped;
            _driver.RunStarted -= OnRunStarted;
        }
    }

    /// <inheritdoc />
    public override void _Process(double delta) => QueueRedraw();

    /// <inheritdoc />
    public override void _Draw()
    {
        if (_driver?.Run is not { } run)
        {
            return;
        }
        var line = ObjectiveModel.Build(run, _driver.Registry);
        // HUD polish (ART-PLAN lane 8): the panel lives in its HudLayout.TopBar slot, left of the Slack bar, and its
        // lines are clipped with "..." to the slot instead of running under the bar. This node spans the HUD root, so
        // the slot is in viewport pixels.
        var bar = HudLayout.TopBar((int)Size.X, (int)Size.Y);
        var title = HudLayout.FitTitle(line.Title, bar.TitleMaxWidth, s => Measure(s, TitleFont));
        var task = HudLayout.FitTitle(line.Task, bar.TitleMaxWidth, s => Measure(s, TaskFont));
        var slot = bar.Objective;
        var width = HudLayout.PanelWidth(Measure(title, TitleFont), Measure(task, TaskFont), bar);
        DrawRect(new Rect2(slot.X, slot.Y, width, slot.Height), HudNodes.Back);
        HudNodes.Text(this, new Vector2(slot.X + HudLayout.PanelPad, slot.Y + 19), title, TitleFont, new Color(1f, 0.9f, 0.55f));
        HudNodes.Text(this, new Vector2(slot.X + HudLayout.PanelPad, slot.Y + 38), task, TaskFont, Colors.White);
    }

    private static int Measure(string text, int size) => (int)Math.Ceiling(HudNodes.Width(text, size));

    private void OnRunStarted()
    {
        _logged = null;
        Log();
    }

    private void OnStepped(int tick, IReadOnlyList<SimEvent> events) => Log();

    private void Log()
    {
        if (_driver?.Run is not { } run)
        {
            return;
        }
        var text = ObjectiveModel.Build(run, _driver.Registry).LogLine();
        if (text != _logged)
        {
            _logged = text;
            HudNodes.Proof(_driver, text);
        }
    }
}
