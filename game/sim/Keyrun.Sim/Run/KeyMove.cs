using System;
using Keyrun.Sim.Encounter;

namespace Keyrun.Sim.Runs;

/// <summary>The key move a finished run shows on the end-of-run panel. Red-commit stub: not implemented yet.</summary>
/// <param name="From">The key the run was played at.</param>
/// <param name="To">The key the next run opens at.</param>
/// <param name="Tier">The run's upgrade tier, 0-3.</param>
public sealed record KeyMove(int From, int To, int Tier)
{
    /// <summary>The panel line.</summary>
    public string Line => string.Empty;

    /// <summary>The autorun proof line.</summary>
    /// <returns>The line.</returns>
    public string LogLine() => string.Empty;

    /// <summary>The move for a finished run.</summary>
    /// <param name="result">The run result.</param>
    /// <param name="keyLevel">The key the run was played at.</param>
    /// <param name="finishTick">The finish tick (the run timer).</param>
    /// <param name="parTicks">Par.</param>
    /// <returns>The move.</returns>
    public static KeyMove Of(RunResult result, int keyLevel, int finishTick, int parTicks) => new(keyLevel, keyLevel, 0);

    /// <summary>The move for <paramref name="run"/>, or null while it is live.</summary>
    /// <param name="run">The run.</param>
    /// <returns>The move, or null.</returns>
    public static KeyMove? ForRun(Run run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return null;
    }
}
