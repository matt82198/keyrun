using System;
using System.Collections.Generic;
using Godot;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;

namespace Keyrun.Hud;

/// <summary>
/// HUD-FORCES (BIBLE s9.2, ledger f-228202e1): the Mythic+ Enemy Forces bar, directly under the Slack/timer bar
/// (<see cref="HudLayout.ForcesBar"/>). It reads the forces table from <c>SimDriver.Registry.Forces</c> (forces.json)
/// and the run state only (<see cref="Forces.Read"/>): the fill is the banked share, the label is WoW's
/// "Enemy Forces 16.00%", and it turns green with "done" once the requirement is met. Logs
/// <c>HUD forces pts=P/R bp=B full=F</c> whenever the points change.
/// </summary>
public partial class ForcesBar : Control
{
    private const int Font = 13;

    private static readonly Color Fill = new(0.78f, 0.55f, 0.95f, 1f);
    private static readonly Color FullFill = new(0.35f, 0.8f, 0.4f, 1f);

    private SimDriver? _driver;
    private IReadOnlyDictionary<string, int>? _table;
    private int _required;
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
        if (Reading() is not { } r)
        {
            return;
        }
        var slot = HudLayout.ForcesBar(HudLayout.TopBar((int)Size.X, (int)Size.Y));
        var bar = new Rect2(slot.X, slot.Y, slot.Width, slot.Height);
        DrawRect(bar, HudNodes.Back);
        var frac = Math.Clamp(r.PercentBp / 10000f, 0f, 1f);
        DrawRect(new Rect2(bar.Position, new Vector2(bar.Size.X * frac, bar.Size.Y)), r.Full ? FullFill : Fill);
        DrawRect(bar, HudNodes.Edge, filled: false, width: 1f);
        HudNodes.Text(this, new Vector2(bar.Position.X + 6, bar.End.Y - 3), r.Label, Font, Colors.White);
        // HUD-SCOREBOARD-LIVE (Matt 2026-10-21 "the kill count"): kills of counted enemies, right-aligned in the bar.
        if (_driver?.Run is { } run && _table is not null)
        {
            var kills = KillCount.Of(run, _table).Label;
            HudNodes.Text(this, new Vector2(bar.End.X - HudNodes.Width(kills, Font) - 6, bar.End.Y - 3), kills, Font, Colors.White);
        }
    }

    private ForcesReading? Reading()
    {
        if (_driver?.Run is not { } run || _table is null)
        {
            return null;
        }
        return Forces.Read(run.State, _table, _required);
    }

    private void OnRunStarted()
    {
        _logged = null;
        _table = null;
        if (_driver?.Registry?.Forces is not null)
        {
            (_table, _required) = ScoreboardReading.ForcesOf(_driver.Registry);
        }
        Log();
        LogScoreboard();
    }

    private void OnStepped(int tick, IReadOnlyList<SimEvent> events)
    {
        Log();
        LogScoreboard();
    }

    // HUD-SCOREBOARD-LIVE: one scoreboard line every 60 ticks to the run log (SimDriver.HostLine: console and the --log
    // file a capture keeps). The proof lines above go to stdout only (HudNodes.Proof), which a capture does not keep.
    private void LogScoreboard()
    {
        if (_driver?.Run is not { } run || !ScoreboardReading.Due(run.State.Tick))
        {
            return;
        }
        var (table, required) = _table is null ? ScoreboardReading.ForcesOf(null) : (_table, _required);
        _driver.HostLine(ScoreboardReading.Of(run, _driver.Registry, table, required).LogLine());
    }

    private void Log()
    {
        if (_driver is null || Reading() is not { } r)
        {
            return;
        }
        var line = r.LogLine();
        if (line != _logged)
        {
            _logged = line;
            HudNodes.Proof(_driver, line);
        }
    }
}
