using System;
using System.Collections.Generic;
using Godot;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Core;
using Keyrun.Sim.Runs;
using SimTimer = Keyrun.Sim.Encounter.Timer;

namespace Keyrun.Hud;

/// <summary>
/// The run clock bar (STATE.md D46, Mythic+): the share of par left on the run clock and the deaths readout (a death adds
/// 15 s to the clock, a wipe 25 s more), read from the sim's <c>Encounter.PenaltyTicks</c>/<c>Deaths</c>, with the key
/// level and the run clock against par beside it. Top right, beside the objective panel (HudLayout.TopBar).
/// Reads the sim state only.
/// </summary>
public partial class TimerSlack : Control
{
    private const int Font = 14;

    private static readonly Color SlackFill = new(0.35f, 0.7f, 0.95f, 1f);
    private static readonly Color DepletedFill = new(0.85f, 0.25f, 0.2f, 1f);

    private SimDriver? _driver;
    private string? _logged;
    private string? _layoutLogged;

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
        var s = SlackReading.Of(run);
        var timer = s.TimerLabel;
        var (parts, _) = Layout(s);
        var bar = new Rect2(parts.Bar.X, parts.Bar.Y, parts.Bar.Width, parts.Bar.Height);
        var box = new Rect2(parts.Timer.X, parts.Timer.Y, parts.Timer.Width, parts.Timer.Height);
        DrawRect(bar, HudNodes.Back);
        DrawRect(new Rect2(bar.Position, new Vector2(bar.Size.X * s.Fraction, bar.Size.Y)), s.Depleted ? DepletedFill : SlackFill);
        DrawRect(bar, HudNodes.Edge, filled: false, width: 1f);
        HudNodes.Text(this, new Vector2(bar.Position.X + 6, bar.End.Y - 3), s.Label, Font, Colors.White);
        var color = s.OverPar ? new Color(1f, 0.5f, 0.4f) : new Color(0.85f, 0.88f, 0.95f);
        DrawRect(box, HudNodes.Back);
        DrawRect(box, HudNodes.Edge, filled: false, width: 1f);
        HudNodes.Text(this, new Vector2(box.End.X - HudNodes.Width(timer, Font) - HudLayout.TimerPad, box.End.Y - 3), timer, Font, color);

        // M1 TIMER-TIERS: a 3 px timer strip along the bar's bottom edge (inside the slot, so HudClearance is unchanged)
        // with the +3 (60% of par), +2 (80% of par) and par marks, on the run clock (D46: past par the key is not timed).
        var strip = new Rect2(bar.Position.X, bar.End.Y - 3, bar.Size.X, 3);
        DrawRect(new Rect2(strip.Position, new Vector2(strip.Size.X * s.TimerFraction, strip.Size.Y)), color);
        foreach (var mark in new[] { s.PlusThreeMark, s.PlusTwoMark, s.ParMark })
        {
            var x = strip.Position.X + (strip.Size.X * mark);
            DrawLine(new Vector2(x, strip.Position.Y - 3), new Vector2(x, strip.End.Y), Colors.White, 1f);
        }
    }

    // HUD polish (ART-PLAN lane 8): top right beside the objective panel (HudLayout.TopBar), no longer centred over the pull
    // title (at 1280 px it spanned x 460..820). This node spans the HUD root, so the slot is in viewport pixels.
    // f-1bf2dace: the timer text gets its own box right of the fill bar (HudLayout.SlackSplit), sized to a fixed-width
    // template so it never jitters; the fill edge and the strip's tier marks used to cross it ("3:42 /,par 12:00").
    private (SlackParts Parts, int TextWidth) Layout(SlackReading s)
    {
        var slot = HudLayout.TopBar((int)Size.X, (int)Size.Y).Slack;
        var textWidth = (int)Math.Ceiling(Math.Max(HudNodes.Width(s.TimerTemplate, Font), HudNodes.Width(s.TimerLabel, Font)));
        return (HudLayout.SlackSplit(slot, textWidth), textWidth);
    }

    // "HUD timer bar=X,Y,W,H box=X,Y,W,H text=W viewport=WxH label=+2  0:14 / par 12:00", on a layout change only
    // (the scene test reads it).
    private void LogLayout(SlackReading s)
    {
        if (_driver is null)
        {
            return;
        }
        var (parts, textWidth) = Layout(s);
        var key = FormattableString.Invariant($"{HudClearance.Format(parts.Bar)}:{HudClearance.Format(parts.Timer)}:{textWidth}");
        if (key == _layoutLogged)
        {
            return;
        }
        _layoutLogged = key;
        HudNodes.Proof(_driver, FormattableString.Invariant(
            $"HUD timer bar={HudClearance.Format(parts.Bar)} box={HudClearance.Format(parts.Timer)} text={textWidth} viewport={(int)Size.X}x{(int)Size.Y} label={s.TimerLabel}"));
    }

    private void OnRunStarted()
    {
        _logged = null;
        _layoutLogged = null;
        Log();
    }

    private void OnStepped(int tick, IReadOnlyList<SimEvent> events) => Log();

    private void Log()
    {
        if (_driver?.Run is not { } run)
        {
            return;
        }
        var s = SlackReading.Of(run);
        // Log on a Slack, death-time or depleted change only (the timer moves every tick).
        var key = FormattableString.Invariant($"{s.SlackTicks}:{s.PenaltyTicks}:{s.Depleted}");
        if (key != _logged)
        {
            _logged = key;
            HudNodes.Proof(_driver, s.LogLine());
        }
        LogLayout(s);
    }
}
