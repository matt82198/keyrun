using System;

namespace Keyrun.Sim.Encounter;

/// <summary>
/// The run-end panel title (BIBLE s8: "The run has a par time. Deaths and wipes cost Slack ... when Slack is gone the
/// key is depleted"). <see cref="Timer.FinishRun"/> decides timed against depleted by the Slack bank alone, so a
/// clean run that finishes past par is still <see cref="RunResult.Timed"/>; the title must read the run clock
/// against par (<see cref="Timer.IsOverPar"/>) before it claims "within par".
/// </summary>
public static class RunEndTitle
{
    /// <summary>A cleared run with Slack left that finished at or before par.</summary>
    public const string WithinPar = "Run cleared -- within par";

    /// <summary>A cleared run with Slack left that finished strictly past par.</summary>
    public const string OverPar = "Run cleared -- over par";

    /// <summary>A cleared run whose Slack bank ran out.</summary>
    public const string Depleted = "Run cleared -- key depleted";

    /// <summary>A run the party gave up on.</summary>
    public const string Abandoned = "Run abandoned";

    /// <summary>The title for a finished run.</summary>
    /// <param name="result">The sim's run result.</param>
    /// <param name="cleared">True when the pull index is past the last pull (a wipe is finished as depleted).</param>
    /// <param name="runTicks">The run clock at the end tick (the HUD's shown run time, not the live clock).</param>
    /// <param name="encounter">The encounter, for its par.</param>
    /// <param name="lostNumber">1-based pull lost on, used only when not cleared.</param>
    /// <param name="lostTotal">Pull count of the full dungeon, used only when not cleared.</param>
    /// <returns>The title line.</returns>
    public static string Of(RunResult result, bool cleared, int runTicks, EncounterState encounter, int lostNumber, int lostTotal)
    {
        ArgumentNullException.ThrowIfNull(encounter);
        return (result, cleared) switch
        {
            (RunResult.Abandoned, _) => Abandoned,
            (_, false) => FormattableString.Invariant($"Party wiped on pull {lostNumber} of {lostTotal} -- key depleted"),
            (RunResult.Timed, true) => Timer.IsOverPar(runTicks, encounter) ? OverPar : WithinPar,
            _ => Depleted,
        };
    }

    /// <summary>
    /// The run-end panel's title for <paramref name="run"/>, or null while it is live. RED STUB: mirrors the panel's
    /// current inline switch (every timed clear reads "within par").
    /// </summary>
    /// <param name="run">The run.</param>
    /// <param name="lostNumber">1-based pull lost on, used only when not cleared.</param>
    /// <param name="lostTotal">Pull count of the full dungeon, used only when not cleared.</param>
    /// <returns>The title line, or null.</returns>
    public static string? ForRun(Keyrun.Sim.Runs.Run run, int lostNumber, int lostTotal)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.RunEnd.Result is not { } result)
        {
            return null;
        }
        var cleared = run.Encounter.PullIndex >= run.Dungeon.Pulls.Count;
        return (result, cleared) switch
        {
            (RunResult.Abandoned, _) => Abandoned,
            (_, false) => FormattableString.Invariant($"Party wiped on pull {lostNumber} of {lostTotal} -- key depleted"),
            (RunResult.Timed, true) => WithinPar,
            _ => Depleted,
        };
    }
}
