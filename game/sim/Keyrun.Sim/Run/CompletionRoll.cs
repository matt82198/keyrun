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
    /// <param name="facetsOf">The facet pool per slot, or null for the placeholder pools (no class pool is passed).</param>
    /// <returns>One result per seat, in <paramref name="seats"/> order.</returns>
    /// <exception cref="ArgumentException">The table has no slots, or a slot has no facet pool.</exception>
    public static IReadOnlyList<CompletionRollResult> Roll(long rewardSeed, int keyLevel, RunResult result, IReadOnlyList<int> seats, CompletionRollTable? table = null, Func<string, IReadOnlyList<Facet>>? facetsOf = null)
    {
        ArgumentNullException.ThrowIfNull(seats);
        var t = table ?? CompletionRollTable.Default;
        if (t.Slots.Count == 0)
        {
            throw new ArgumentException("completion table has no slots", nameof(table));
        }
        var facets = facetsOf ?? Rewards.PlaceholderFacets;
        var dropBp = result switch
        {
            RunResult.Timed => t.DropBpTimed,
            RunResult.Depleted => t.DropBpDepleted,
            _ => 0L,
        };
        var band = Rewards.TierBand(keyLevel);
        return seats.Select(seat => RollSeat(rewardSeed, seat, result == RunResult.Abandoned, dropBp, t.Slots, facets, band)).ToList();
    }

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
