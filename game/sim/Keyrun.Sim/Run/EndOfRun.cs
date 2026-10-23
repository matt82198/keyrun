using System;
using System.Collections.Generic;
using Keyrun.Sim.Encounter;

namespace Keyrun.Sim.Runs;

/// <summary>
/// The end-of-run reads of a finished run: the reward seed and the abandoned-run line. The reward itself is the
/// completion roll (<see cref="CompletionRoll"/>, R-ROLL-1); the choice of three (BIBLE s1/s7) and its <c>Offer</c> read
/// were removed with R-ROLL-1 (b)(iii).
/// </summary>
/// <remarks>
/// An abandoned run gets nothing (STATE.md D45): its completion roll is "nothing" for every seat, <see cref="Ash.ForResult"/>
/// is 0, <see cref="Progression.Take"/> refuses, and the key holds (<see cref="Timer.KeyAfterRun"/>).
/// </remarks>
public static class EndOfRun
{
    /// <summary>The HUD line (run-end panel and the screen's "since last run") for a run the player left (D45).</summary>
    public const string NothingLine = "Left mid-run: nothing gained";

    /// <summary>The run-end panel's "nothing granted" line for an abandoned run (D45), else null.</summary>
    /// <param name="run">The run.</param>
    /// <returns><see cref="NothingLine"/> for an abandoned run, else null.</returns>
    public static string? HudLine(Run run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return run.RunEnd.Result == RunResult.Abandoned ? NothingLine : null;
    }

    /// <summary>
    /// The reward seed the run-end panel has always used (server/src/run-end.ts): the run seed x 1000003 plus the tick
    /// the run ended.
    /// </summary>
    /// <param name="run">A finished run.</param>
    /// <returns>The seed.</returns>
    /// <exception cref="ArgumentException">The run has not ended.</exception>
    public static long RewardSeed(Run run)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.RunEnd.ResultTick is not { } tick)
        {
            throw new ArgumentException("the run has not ended", nameof(run));
        }
        return RewardSeedOf(run.State.Seed, tick);
    }

    /// <summary>The reward seed from a run seed and the tick the run ended (<see cref="RewardSeed(Run)"/>'s formula).</summary>
    /// <param name="seed">The run seed.</param>
    /// <param name="tick">The run-end tick.</param>
    /// <returns>The seed.</returns>
    public static long RewardSeedOf(long seed, long tick) => unchecked((seed * 1_000_003L) + tick);

    /// <summary>Whether taking <paramref name="card"/> is a strict upgrade (<see cref="Rewards.IsUpgrade"/>).</summary>
    /// <param name="card">The card.</param>
    /// <param name="equipped">The character's equipped gear by slot.</param>
    /// <returns>Whether it is a strict upgrade.</returns>
    public static bool IsUpgrade(Item card, IReadOnlyDictionary<string, Item> equipped) => Rewards.IsUpgrade(card, equipped);
}
