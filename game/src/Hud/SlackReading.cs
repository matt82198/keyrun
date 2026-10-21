using System;
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
/// <param name="End">How the run ended (<see cref="CauseOf"/>), or <see cref="RunEndCause.Live"/> while it runs.</param>
public sealed record SlackReading(int SlackTicks, int BankTicks, bool Depleted, int RunTimerTicks, int ParTicks, int KeyLevel, int PenaltyTicks = 0, int Deaths = 0, RunEndCause End = RunEndCause.Live)
{
    /// <summary>The run clock: the run timer plus the time deaths and wipes added (<see cref="SimTimer.ClockTicks"/>).</summary>
    public long ClockTicks => (long)RunTimerTicks + Math.Max(0, PenaltyTicks);

    /// <summary>Fill fraction of the bar, [0, 1]: the share of par still left on the run clock.</summary>
    public float Fraction => ParTicks <= 0 ? 0f : Math.Clamp(1f - ((float)ClockTicks / ParTicks), 0f, 1f);

    /// <summary>True when the run clock is strictly past par (the key would not be timed).</summary>
    public bool OverPar => ClockTicks > ParTicks;

    /// <summary>
    /// The bar label, named by the run's end cause (<see cref="End"/>, f-44796144), never by the Depleted flag alone (the
    /// sim sets Depleted for an over-par clear as well as a lasting wipe): "Deaths 3 (+45s)" while live or after a timed
    /// clear; "Over par -- key stays +7" for a clear past par (WoW Mythic+: the key was not timed); "Wiped -- key stays
    /// +7" for a lasting wipe; "Abandoned -- key stays +7" for a run the party left. An untimed key keeps its level
    /// (Matt 2026-10-20, f-7c7abdf4), and a WoW player reads "depleted" as the key dropping.
    /// </summary>
    public string Label => End switch
    {
        RunEndCause.OverPar => FormattableString.Invariant($"Over par -- key stays +{KeyLevel}"),
        RunEndCause.Wiped => FormattableString.Invariant($"Wiped -- key stays +{KeyLevel}"),
        RunEndCause.Abandoned => FormattableString.Invariant($"Abandoned -- key stays +{KeyLevel}"),
        _ => FormattableString.Invariant($"Deaths {Deaths} (+{Math.Max(0, PenaltyTicks) / Tick.TickHz}s)"),
    };

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
        return new SlackReading(e.SlackTicks, SimTimer.InitialSlackTicks(e.KeyLevel), e.Depleted, RunClock.ShownTicks(run), e.ParTicks, e.KeyLevel, e.PenaltyTicks, e.Deaths, CauseOf(run));
    }

    /// <summary>
    /// The run's end cause from its recorded end (<c>run.RunEnd.Result</c>, set with <c>run_end</c>) and whether every
    /// pull was cleared, the same split <see cref="Keyrun.Sim.Encounter.RunEndTitle.ForRun"/> makes: the sim finishes a
    /// lasting wipe as depleted with completed forced, so a depleted run with every pull cleared is an over-par clear
    /// and one with a pull left is a wipe. A new end reason (a timer cap) adds its case here.
    /// </summary>
    /// <param name="run">The run.</param>
    /// <returns>The end cause, or <see cref="RunEndCause.Live"/> while the run has no result.</returns>
    public static RunEndCause CauseOf(Run run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var cleared = run.Encounter.PullIndex >= run.Dungeon.Pulls.Count;
        return run.RunEnd.Result switch
        {
            null => RunEndCause.Live,
            Keyrun.Sim.Encounter.RunResult.Timed => RunEndCause.Timed,
            Keyrun.Sim.Encounter.RunResult.Abandoned => RunEndCause.Abandoned,
            _ => cleared ? RunEndCause.OverPar : RunEndCause.Wiped,
        };
    }
}

/// <summary>Why a run ended, as the run clock bar names it (<see cref="SlackReading.Label"/>, f-44796144).</summary>
public enum RunEndCause
{
    /// <summary>The run has not ended.</summary>
    Live,

    /// <summary>Every pull cleared with the run clock within par: the key was timed.</summary>
    Timed,

    /// <summary>Every pull cleared, but the run clock was past par: the key was not timed and stays at its level.</summary>
    OverPar,

    /// <summary>A lasting wipe ended the run before every pull was cleared; the key stays at its level.</summary>
    Wiped,

    /// <summary>The party left the run; the key stays at its level.</summary>
    Abandoned,
}
