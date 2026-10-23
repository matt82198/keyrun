using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;

namespace Keyrun.Sim.Runs;

/// <summary>
/// The completion table (game/content/data/loot/completion.json; R-ROLL-1): the basis-point drop chance of a timed and
/// of a depleted clear, and the five gear slots an item is drawn over.
/// </summary>
/// <param name="DropBpTimed">Drop chance of one seat's roll on a timed clear, basis points of 10000.</param>
/// <param name="DropBpDepleted">Drop chance of one seat's roll on a depleted clear, basis points of 10000.</param>
/// <param name="Slots">The slots an item's slot is drawn uniformly over.</param>
public sealed record CompletionRollTable(long DropBpTimed, long DropBpDepleted, IReadOnlyList<string> Slots)
{
    /// <summary>
    /// Class id to that class's facets per gear slot (R-ROLL-1 (b)(iii)): the pool a seat of that class draws over. Null, or
    /// a class with no entry, draws over <see cref="CompletionFacets.Fallback"/>.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, IReadOnlyList<Facet>>>? ClassFacets { get; init; }

    /// <summary>The table's values with no content loaded (6000, 3500, the five gear slots).</summary>
    public static CompletionRollTable Default { get; } = new(6000, 3500, Rewards.GearSlots);
}

/// <summary>One seat's completion roll: the seat's actor id and the item it won, or null for "nothing".</summary>
/// <param name="Seat">The party member's actor id.</param>
/// <param name="Item">The item won, or null.</param>
public sealed record CompletionRollResult(int Seat, Item? Item);

/// <summary>
/// R-ROLL-1 (BIBLE s1; design/PROGRESSION-PIVOT.md s2.1-2.3): the end-of-run completion roll, one per party seat. It
/// replaces the choice of three as the run's reward; a seat's roll can give nothing.
/// </summary>
/// <remarks>
/// Draw 0 for a seat is <c>Prng.NextInt(Prng.SeedState(rewardSeed * 31 + seat), 10000) &lt; dropBp</c>, where dropBp is
/// <see cref="CompletionRollTable.DropBpTimed"/> for a timed clear and <see cref="CompletionRollTable.DropBpDepleted"/> for
/// a depleted one. A roll that misses is "nothing", never a coin flip. On a hit the item is drawn from the same
/// <see cref="Rewards.DrawItem"/> as the choice of three: slot, then facet, then tier (<see cref="Rewards.TierBand"/>),
/// continuing the seat's own PRNG state. An abandoned run gets nothing for every seat (D45) and draws nothing.
/// </remarks>
public static class CompletionRoll
{
    /// <summary>The denominator of a drop roll: basis points.</summary>
    public const long DropDenominator = 10000;

    /// <summary>Rolls every seat, in the order given.</summary>
    /// <param name="rewardSeed">The run's reward seed (<see cref="EndOfRun.RewardSeedOf"/>).</param>
    /// <param name="keyLevel">The key level; the tier band is <see cref="Rewards.TierBand"/> of it.</param>
    /// <param name="result">The run's result.</param>
    /// <param name="seats">The party member actor ids, one roll each.</param>
    /// <param name="table">The completion table, or null for <see cref="CompletionRollTable.Default"/>.</param>
    /// <param name="facetsOfSeat">The facet pool per slot of each seat (its class pool), or null for <see cref="CompletionFacets.Fallback"/>
    /// for every seat. The placeholder pools are never used (R-ROLL-1 (b)(iii)).</param>
    /// <returns>One result per seat, in <paramref name="seats"/> order.</returns>
    /// <exception cref="ArgumentException">The table has no slots, or a slot has no facet pool.</exception>
    public static IReadOnlyList<CompletionRollResult> Roll(long rewardSeed, int keyLevel, RunResult result, IReadOnlyList<int> seats, CompletionRollTable? table = null, Func<int, Func<string, IReadOnlyList<Facet>>>? facetsOfSeat = null)
    {
        ArgumentNullException.ThrowIfNull(seats);
        var t = table ?? CompletionRollTable.Default;
        if (t.Slots.Count == 0)
        {
            throw new ArgumentException("completion table has no slots", nameof(table));
        }
        var fallback = CompletionFacets.Fallback;
        var poolOf = facetsOfSeat ?? (_ => slot => PoolOf(fallback, slot));
        var dropBp = result switch
        {
            RunResult.Timed => t.DropBpTimed,
            RunResult.Depleted => t.DropBpDepleted,
            _ => 0L,
        };
        var band = Rewards.TierBand(keyLevel);
        return seats.Select(seat => RollSeat(rewardSeed, seat, result == RunResult.Abandoned, dropBp, t.Slots, poolOf(seat), band)).ToList();
    }

    /// <summary>
    /// Rolls every player seat of <paramref name="state"/> (actor id order, as the run-end phase emits them), each over its
    /// own class's facet pool (<see cref="CompletionFacets.For"/> of the seat's class id). The one roll the sim's
    /// <c>completion_roll</c> events and the run-end panel both read.
    /// </summary>
    /// <param name="state">The state at the run-end tick.</param>
    /// <param name="rewardSeed">The run's reward seed (<see cref="EndOfRun.RewardSeedOf"/>).</param>
    /// <param name="keyLevel">The key level.</param>
    /// <param name="result">The run's result.</param>
    /// <param name="table">The completion table, or null for <see cref="CompletionRollTable.Default"/>.</param>
    /// <returns>One result per player seat.</returns>
    public static IReadOnlyList<CompletionRollResult> ForSeats(SimState state, long rewardSeed, int keyLevel, RunResult result, CompletionRollTable? table)
    {
        ArgumentNullException.ThrowIfNull(state);
        var seats = state.Actors.Values.Where(a => a.Kind == ActorKind.Player).Select(a => a.Id).ToList();
        return Roll(rewardSeed, keyLevel, result, seats, table, seat =>
        {
            var pool = CompletionFacets.For(table, state.Actors[seat].ClassId);
            return slot => PoolOf(pool, slot);
        });
    }

    /// <summary>The rolls of a finished run (the ones its <c>completion_roll</c> events carried), or null while it is live.</summary>
    /// <param name="run">The run.</param>
    /// <returns>One result per player seat, or null.</returns>
    public static IReadOnlyList<CompletionRollResult>? ForRun(Run run)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.RunEnd.Result is not { } result || run.RunEnd.ResultTick is null)
        {
            return null;
        }
        return ForSeats(run.State, EndOfRun.RewardSeed(run), run.Encounter.KeyLevel, result, run.Content.Completion);
    }

    private static IReadOnlyList<Facet> PoolOf(IReadOnlyDictionary<string, IReadOnlyList<Facet>> pool, string slot) =>
        pool.TryGetValue(slot, out var facets) ? facets : throw new ArgumentException($"no facet pool for gear slot \"{slot}\"", nameof(slot));

    private static CompletionRollResult RollSeat(long rewardSeed, int seat, bool abandoned, long dropBp, IReadOnlyList<string> slots, Func<string, IReadOnlyList<Facet>> facets, (int Min, int Max) band)
    {
        if (abandoned)
        {
            return new CompletionRollResult(seat, null);
        }
        var (raw, afterDrop) = Prng.NextInt(Prng.SeedState(unchecked((rewardSeed * 31) + seat)), DropDenominator);
        if (raw >= dropBp)
        {
            return new CompletionRollResult(seat, null);
        }
        var (item, _) = Rewards.DrawItem(afterDrop, slots, facets, band, seat);
        return new CompletionRollResult(seat, item);
    }
}
