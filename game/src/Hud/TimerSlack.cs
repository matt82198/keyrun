using System;
using System.Collections.Generic;
using Godot;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Core;
using Keyrun.Sim.Runs;
using SimTimer = Keyrun.Sim.Encounter.Timer;

namespace Keyrun.Hud;

/// <summary>The run timer and the Slack bank as the HUD shows them (Godot-free).</summary>
/// <param name="SlackTicks">Slack left (<c>Encounter.SlackTicks</c>).</param>
/// <param name="BankTicks">The full bank for this key (<see cref="SimTimer.InitialSlackTicks"/>).</param>
/// <param name="Depleted">The sim's depleted flag (the bank hit 0 at least once).</param>
/// <param name="RunTimerTicks">Run time so far (<see cref="RunClock.ShownTicks"/>: frozen at the end tick once the run has ended).</param>
/// <param name="ParTicks">Par (<c>Encounter.ParTicks</c>).</param>
/// <param name="KeyLevel">The key level.</param>
public sealed record SlackReading(int SlackTicks, int BankTicks, bool Depleted, int RunTimerTicks, int ParTicks, int KeyLevel)
{
    /// <summary>Fill fraction of the Slack bar, [0, 1].</summary>
    public float Fraction => BankTicks <= 0 ? 0f : Math.Clamp((float)SlackTicks / BankTicks, 0f, 1f);

    /// <summary>True when the run timer is strictly past par (<see cref="SimTimer.IsOverPar"/>).</summary>
    public bool OverPar => RunTimerTicks > ParTicks;

    /// <summary>The bar label: "Slack 31.0s" or "Slack DEPLETED".</summary>
    public string Label => Depleted
        ? "Slack DEPLETED"
        : FormattableString.Invariant($"Slack {SlackTicks / (double)Tick.TickHz:0.0}s");

    /// <summary>The timer label: "+2  0:12 / par 12:00".</summary>
    public string TimerLabel => FormattableString.Invariant($"+{KeyLevel}  {Clock(RunTimerTicks)} / par {Clock(ParTicks)}");

    /// <summary>"HUD slack ticks=620 of=620 depleted=0 timer=0 par=14400".</summary>
    public string LogLine() => FormattableString.Invariant(
        $"HUD slack ticks={SlackTicks} of={BankTicks} depleted={(Depleted ? 1 : 0)} timer={RunTimerTicks} par={ParTicks}");

    private static string Clock(int ticks)
    {
        var seconds = Math.Max(0, ticks) / Tick.TickHz;
        return FormattableString.Invariant($"{seconds / 60}:{seconds % 60:00}");
    }

    /// <summary>Reads the run.</summary>
    /// <param name="run">The run.</param>
    /// <returns>The reading.</returns>
    public static SlackReading Of(Run run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var e = run.Encounter;
        // RUN-TIMER-STOP: RunClock, not State.RunTimerTicks, which StepRun keeps advancing after run_end.
        return new SlackReading(e.SlackTicks, SimTimer.InitialSlackTicks(e.KeyLevel), e.Depleted, RunClock.ShownTicks(run), e.ParTicks, e.KeyLevel);
    }
}

/// <summary>
/// The Slack bar (BIBLE s8): the soft time bank in seconds, which a death spends 15 s of and a wipe 25 s, read from the
/// sim's <c>Encounter.SlackTicks</c>, with the key level and the run timer against par beside it. Top right, beside the objective panel (HudLayout.TopBar).
/// Reads the sim state only.
/// </summary>
public partial class TimerSlack : Control
{
    private const int Font = 14;

    private static readonly Color SlackFill = new(0.35f, 0.7f, 0.95f, 1f);
    private static readonly Color DepletedFill = new(0.85f, 0.25f, 0.2f, 1f);

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
        var s = SlackReading.Of(run);
        // HUD polish (ART-PLAN lane 8): top right beside the objective panel (HudLayout.TopBar), no longer centred over
        // the pull title (at 1280 px it spanned x 460..820). This node spans the HUD root, so the slot is in viewport pixels.
        var slot = HudLayout.TopBar((int)Size.X, (int)Size.Y).Slack;
        var bar = new Rect2(slot.X, slot.Y, slot.Width, slot.Height);
        DrawRect(bar, HudNodes.Back);
        DrawRect(new Rect2(bar.Position, new Vector2(bar.Size.X * s.Fraction, bar.Size.Y)), s.Depleted ? DepletedFill : SlackFill);
        DrawRect(bar, HudNodes.Edge, filled: false, width: 1f);
        HudNodes.Text(this, new Vector2(bar.Position.X + 6, bar.End.Y - 3), s.Label, Font, Colors.White);
        var timer = s.TimerLabel;
        var color = s.OverPar ? new Color(1f, 0.5f, 0.4f) : new Color(0.85f, 0.88f, 0.95f);
        HudNodes.Text(this, new Vector2(bar.End.X - HudNodes.Width(timer, Font) - 6, bar.End.Y - 3), timer, Font, color);
    }

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
        var s = SlackReading.Of(run);
        // Log on a Slack change only (the timer moves every tick).
        var key = FormattableString.Invariant($"{s.SlackTicks}:{s.Depleted}");
        if (key != _logged)
        {
            _logged = key;
            HudNodes.Proof(_driver, s.LogLine());
        }
    }
}
