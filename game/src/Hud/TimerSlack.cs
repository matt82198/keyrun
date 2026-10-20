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
/// The run clock as the HUD shows it (Godot-free). STATE.md D46 (Mythic+): deaths and wipes only add time to the run
/// clock (<see cref="SimTimer.ClockTicks"/>); the key is timed when that clock finishes within par. The Slack bank
/// fields stay for the log line, but the bank no longer drains or decides a result.
/// </summary>
/// <param name="SlackTicks">Slack left (<c>Encounter.SlackTicks</c>; no longer drains, D46).</param>
/// <param name="BankTicks">The full bank for this key (<see cref="SimTimer.InitialSlackTicks"/>).</param>
/// <param name="Depleted">The sim's depleted flag (an unrecovered full wipe ended the run).</param>
/// <param name="RunTimerTicks">Run time so far (<see cref="RunClock.ShownTicks"/>: frozen at the end tick once the run has ended).</param>
/// <param name="ParTicks">Par (<c>Encounter.ParTicks</c>).</param>
/// <param name="KeyLevel">The key level.</param>
/// <param name="PenaltyTicks">Time deaths and wipes have added to the run clock (<c>Encounter.PenaltyTicks</c>, D46).</param>
/// <param name="Deaths">Player deaths this run (<c>Encounter.Deaths</c>).</param>
public sealed record SlackReading(int SlackTicks, int BankTicks, bool Depleted, int RunTimerTicks, int ParTicks, int KeyLevel, int PenaltyTicks = 0, int Deaths = 0)
{
    /// <summary>The run clock: the run timer plus the time deaths and wipes added (<see cref="SimTimer.ClockTicks"/>).</summary>
    public long ClockTicks => (long)RunTimerTicks + Math.Max(0, PenaltyTicks);

    /// <summary>Fill fraction of the bar, [0, 1]: the share of par still left on the run clock.</summary>
    public float Fraction => ParTicks <= 0 ? 0f : Math.Clamp(1f - ((float)ClockTicks / ParTicks), 0f, 1f);

    /// <summary>True when the run clock is strictly past par (the key would not be timed).</summary>
    public bool OverPar => ClockTicks > ParTicks;

    /// <summary>The bar label: "Deaths 3 (+45s)", or "DEPLETED" once a lasting wipe ended the run.</summary>
    public string Label => Depleted
        ? "DEPLETED"
        : FormattableString.Invariant($"Deaths {Deaths} (+{Math.Max(0, PenaltyTicks) / Tick.TickHz}s)");

    /// <summary>The timer label, on the run clock: "+2  0:57 / par 12:00".</summary>
    public string TimerLabel => FormattableString.Invariant($"+{KeyLevel}  {Clock((int)Math.Min(int.MaxValue, ClockTicks))} / par {Clock(ParTicks)}");

    /// <summary>Fill fraction of the timer strip, [0, 1]: the run clock against par, so the strip fills at par.</summary>
    public float TimerFraction => Span <= 0 ? 0f : Math.Clamp((float)ClockTicks / Span, 0f, 1f);

    /// <summary>Where the +3 mark sits on the timer strip (60% of par, <see cref="SimTimer.UpgradeThreeParBp"/>).</summary>
    public float PlusThreeMark => Mark(SimTimer.UpgradeThreeParBp);

    /// <summary>Where the +2 mark sits on the timer strip (80% of par, <see cref="SimTimer.UpgradeTwoParBp"/>).</summary>
    public float PlusTwoMark => Mark(SimTimer.UpgradeTwoParBp);

    /// <summary>Where par sits on the timer strip (the end of the +1 band).</summary>
    public float ParMark => Mark(10000);

    /// <summary>
    /// The upgrade a finish on this tick would earn (<see cref="SimTimer.UpgradeTier"/> on the run clock): 3, 2 or 1
    /// while the clock is within par, 0 once it is not.
    /// </summary>
    public int PaceTier => !Depleted && ClockTicks <= ParTicks
        ? SimTimer.UpgradeTier(Keyrun.Sim.Encounter.RunResult.Timed, ClockTicks, ParTicks)
        : 0;

    private long Span => ParTicks;

    private float Mark(int parBp) => Span <= 0 ? 0f : Math.Clamp((float)((long)ParTicks * parBp / 10000) / Span, 0f, 1f);

    /// <summary>"HUD slack ticks=620 of=620 depleted=0 timer=0 par=14400 penalty=0 deaths=0".</summary>
    public string LogLine() => FormattableString.Invariant(
        $"HUD slack ticks={SlackTicks} of={BankTicks} depleted={(Depleted ? 1 : 0)} timer={RunTimerTicks} par={ParTicks} penalty={PenaltyTicks} deaths={Deaths}");

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
        return new SlackReading(e.SlackTicks, SimTimer.InitialSlackTicks(e.KeyLevel), e.Depleted, RunClock.ShownTicks(run), e.ParTicks, e.KeyLevel, e.PenaltyTicks, e.Deaths);
    }
}

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
        // Log on a Slack, death-time or depleted change only (the timer moves every tick).
        var key = FormattableString.Invariant($"{s.SlackTicks}:{s.PenaltyTicks}:{s.Depleted}");
        if (key != _logged)
        {
            _logged = key;
            HudNodes.Proof(_driver, s.LogLine());
        }
    }
}
