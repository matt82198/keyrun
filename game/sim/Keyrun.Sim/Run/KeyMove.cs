using System;
using Keyrun.Sim.Encounter;

namespace Keyrun.Sim.Runs;

/// <summary>
/// The key move a finished run shows on the end-of-run panel (BIBLE s8; Matt 2026-10-20: a timed run upgrades the key
/// by its chest tier, an untimed run keeps it at the same level with no depletion drop). The tier is
/// <see cref="Timer.UpgradeTier"/> and the raise is <see cref="Timer.NextKeyLevel"/>; <see cref="To"/> is then held
/// inside the screen's band (<see cref="ScreenState.MinKey"/>-<see cref="ScreenState.MaxKey"/>), so the level the
/// panel shows is exactly the level the between-runs screen opens at next.
/// </summary>
/// <param name="From">The key the run was played at.</param>
/// <param name="To">The key the next run opens at.</param>
/// <param name="Tier">The run's upgrade tier, 0-3 (0 = not timed).</param>
public sealed record KeyMove(int From, int To, int Tier)
{
    /// <summary>
    /// The panel line: "Key +7 -> +10  (timed +3)", "Key +19 -> +20  (timed +2, key cap)",
    /// "Key +20 holds  (timed +1, key cap)" or "Key +7 holds  (not timed -- no drop)".
    /// </summary>
    public string Line
    {
        get
        {
            if (Tier == 0)
            {
                return FormattableString.Invariant($"Key +{From} holds  (not timed -- no drop)");
            }
            var capped = To < Timer.NextKeyLevel(From, Tier) ? ", key cap" : string.Empty;
            return To == From
                ? FormattableString.Invariant($"Key +{From} holds  (timed +{Tier}{capped})")
                : FormattableString.Invariant($"Key +{From} -> +{To}  (timed +{Tier}{capped})");
        }
    }

    /// <summary>The autorun proof line, "HUD runend key from=7 to=10 tier=3".</summary>
    /// <returns>The line.</returns>
    public string LogLine() => FormattableString.Invariant($"HUD runend key from={From} to={To} tier={Tier}");

    /// <summary>The move for a finished run.</summary>
    /// <param name="result">The run result.</param>
    /// <param name="keyLevel">The key the run was played at.</param>
    /// <param name="finishTick">The finish tick (the run timer).</param>
    /// <param name="parTicks">Par.</param>
    /// <returns>The move.</returns>
    public static KeyMove Of(RunResult result, int keyLevel, int finishTick, int parTicks)
    {
        var tier = Timer.UpgradeTier(result, finishTick, parTicks);
        // Timer.KeyAfterRun is the one sim rule for the key after a run (f-7c7abdf4: an untimed key keeps its level);
        // a timed raise is then held inside the screen's band. Tier 0 is exactly the untimed case (a timed run is tier 1+).
        var after = Timer.KeyAfterRun(keyLevel, result, finishTick, parTicks);
        var to = tier == 0 ? after : Math.Clamp(after, ScreenState.MinKey, ScreenState.MaxKey);
        return new KeyMove(keyLevel, Math.Max(keyLevel, to), tier);
    }

    /// <summary>
    /// The move for <paramref name="run"/>, or null while it is live. The finish tick is the run end's tick (the run
    /// clock keeps advancing after the end, RUN-END-FREEZE), the same read the screen's bank uses.
    /// </summary>
    /// <param name="run">The run.</param>
    /// <returns>The move, or null.</returns>
    public static KeyMove? ForRun(Run run)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.RunEnd.Result is not { } result)
        {
            return null;
        }
        return Of(result, run.Encounter.KeyLevel, run.RunEnd.ResultTick ?? run.State.RunTimerTicks, run.Encounter.ParTicks);
    }
}
