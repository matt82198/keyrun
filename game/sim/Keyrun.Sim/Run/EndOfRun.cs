using System;
using System.Collections.Generic;
using Keyrun.Sim.Encounter;

namespace Keyrun.Sim.Runs;

/// <summary>
/// The end-of-run choice of three for a finished run (BIBLE s1 "Dungeon -> End-of-run choice of three -> Screen", s7
/// "End of every run: choose one of three"). The one read the run-end panel draws (RunEndView.Of), so the offer for
/// a timed clear, an untimed clear and a lasting wipe is a pure function of the run, the class's facet pools and the
/// character's equipped gear, testable without the engine.
/// </summary>
/// <remarks>
/// An abandoned run gets nothing (STATE.md D45: "every finished run, timed or depleted, offers the choice of three; a
/// player who leaves mid-run gets nothing"): <see cref="Offer"/> is empty, <see cref="Ash.ForResult"/> is 0,
/// <see cref="Progression.Take"/> refuses, and the key holds (<see cref="Timer.KeyAfterRun"/>).
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
        return unchecked((run.State.Seed * 1_000_003L) + tick);
    }

    /// <summary>
    /// The three cards for a finished run, or null while it is live:
    /// <see cref="Rewards.RollRewardChoices(long, int, RunResult, IReadOnlyList{string}, IReadOnlyDictionary{string, IReadOnlyList{Facet}}, IReadOnlyDictionary{string, Item})"/>
    /// over all five slots at the run's key, seeded by <see cref="RewardSeed"/>. Without <paramref name="facetPools"/>
    /// the placeholder pools are used (no content loaded) and no upgrade is guaranteed.
    /// </summary>
    /// <param name="run">The run.</param>
    /// <param name="facetPools">The human class's facets per slot, or null.</param>
    /// <param name="equipped">The character's equipped gear by slot (empty for a fresh character).</param>
    /// <returns>Exactly three distinct cards, none for an abandoned run (D45), or null while the run is live.</returns>
    public static IReadOnlyList<Item>? Offer(Run run, IReadOnlyDictionary<string, IReadOnlyList<Facet>>? facetPools, IReadOnlyDictionary<string, Item> equipped)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(equipped);
        if (run.RunEnd.Result is not { } result || run.RunEnd.ResultTick is null)
        {
            return null;
        }
        if (result == RunResult.Abandoned)
        {
            // D45: a player who leaves mid-run gets nothing.
            return Array.Empty<Item>();
        }
        var seed = RewardSeed(run);
        return facetPools is null
            ? Rewards.WithAscensionCard(Rewards.RollRewardChoices(seed, run.Encounter.KeyLevel, result, Rewards.GearSlots), Rewards.GearSlots, equipped)
            : Rewards.RollRewardChoices(seed, run.Encounter.KeyLevel, result, Rewards.GearSlots, facetPools, equipped);
    }

    /// <summary>Whether taking <paramref name="card"/> is a strict upgrade (<see cref="Rewards.IsUpgrade"/>).</summary>
    /// <param name="card">The card.</param>
    /// <param name="equipped">The character's equipped gear by slot.</param>
    /// <returns>Whether it is a strict upgrade.</returns>
    public static bool IsUpgrade(Item card, IReadOnlyDictionary<string, Item> equipped) => Rewards.IsUpgrade(card, equipped);
}
