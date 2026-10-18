using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;

namespace Keyrun.Sim.Runs;

/// <summary>
/// Ash banked per run (BIBLE s7 "Every run banks Ash, win or lose"; the frozen TS screen/src/progression.ts
/// ASH_TIMED/ASH_DEPLETED/ASH_ABANDONED). Ash buys only cosmetics and titles, so it never feeds power.
/// </summary>
public static class Ash
{
    /// <summary>Ash for a timed clear (progression.ts ASH_TIMED).</summary>
    public const int Timed = 50;

    /// <summary>Ash for a depleted key (progression.ts ASH_DEPLETED).</summary>
    public const int Depleted = 30;

    /// <summary>Ash for an abandoned run (progression.ts ASH_ABANDONED).</summary>
    public const int Abandoned = 10;

    /// <summary>Ash a run with <paramref name="result"/> banks; every result banks some.</summary>
    /// <param name="result">How the run ended.</param>
    /// <returns>The Ash.</returns>
    public static int ForResult(RunResult result) => result switch
    {
        RunResult.Timed => Timed,
        RunResult.Depleted => Depleted,
        RunResult.Abandoned => Abandoned,
        _ => throw new ArgumentOutOfRangeException(nameof(result), result, "unknown run result"),
    };
}

/// <summary>One reroll attempt.</summary>
/// <param name="Ok">True when a charge was spent and the Facet redrawn; false when refused.</param>
/// <param name="Item">The item after the attempt (the input item itself when refused).</param>
/// <param name="Prng">The crafting PRNG after the attempt (unchanged when refused).</param>
/// <param name="Refusal">Why it was refused, else null.</param>
public sealed record RerollResult(bool Ok, Item Item, PrngState Prng, string? Refusal);

/// <summary>
/// The BIBLE s7 CRAFTING REROLL ("two charges per item: reroll the Facet from that slot's listed pool, twice, then it
/// is what it is"). Pure: the PRNG state is passed in and the advanced state returned, so the same seed gives the same
/// reroll sequence.
/// </summary>
public static class Crafting
{
    /// <summary>
    /// Spends one charge and redraws the Facet uniformly from <paramref name="slotPool"/>. Refused (nothing spent,
    /// the PRNG not advanced) when the item has no charges left or the pool is empty.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <param name="slotPool">That slot's listed Facets for the character's class.</param>
    /// <param name="prng">The crafting PRNG.</param>
    /// <returns>The outcome.</returns>
    public static RerollResult Reroll(Item item, IReadOnlyList<Facet> slotPool, PrngState prng)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(slotPool);
        if (item.Charges <= 0)
        {
            return new RerollResult(false, item, prng, "no crafting charges left");
        }
        if (slotPool.Count == 0)
        {
            return new RerollResult(false, item, prng, $"no Facets listed for slot \"{item.Slot}\"");
        }
        var (index, next) = Prng.NextInt(prng, slotPool.Count);
        return new RerollResult(true, item with { Facet = slotPool[(int)index], Charges = item.Charges - 1 }, next, null);
    }
}

/// <summary>What the last banked run changed, for the screen's "since last run" line (BIBLE s10).</summary>
/// <param name="Result">How it ended.</param>
/// <param name="AshGained">Ash it banked.</param>
/// <param name="Taken">The end-of-run reward taken after it, or null.</param>
/// <param name="PreviousTier">The tier that slot held before the reward, or null when the slot was empty.</param>
public sealed record RunDelta(RunResult Result, int AshGained, Item? Taken, int? PreviousTier)
{
    /// <summary>"+30 Ash, weapon tier 3 (was 1)", "+50 Ash, weapon tier 2 (new)" or "+10 Ash".</summary>
    /// <returns>The line.</returns>
    public string Text() => Taken is not { } t
        ? FormattableString.Invariant($"+{AshGained} Ash")
        : FormattableString.Invariant($"+{AshGained} Ash, {t.Slot} tier {t.Tier} ({(PreviousTier is { } p ? FormattableString.Invariant($"was {p}") : "new")})");
}

/// <summary>
/// The character's bounded progression (BIBLE s7): Ash banked every run, the five gear slots filled from the
/// end-of-run choice of three with Tier capped at 10, and crafting rerolls on the equipped items. Immutable.
/// </summary>
public sealed record Progression
{
    /// <summary>Tier is power and it is capped at 10 (BIBLE s7 "Ten is the end").</summary>
    public const int MaxTier = 10;

    /// <summary>Ash banked so far.</summary>
    public long AshTotal { get; init; }

    /// <summary>Runs banked so far.</summary>
    public int Runs { get; init; }

    /// <summary>Equipped items by slot (<see cref="Rewards.GearSlots"/>).</summary>
    public IReadOnlyDictionary<string, Item> Equipped { get; init; } = new Dictionary<string, Item>(StringComparer.Ordinal);

    /// <summary>The crafting PRNG (in state, as the sim's; never System.Random).</summary>
    public PrngState Prng { get; init; }

    /// <summary>The last banked run, or null before the first.</summary>
    public RunDelta? LastRun { get; init; }

    /// <summary>A fresh character whose crafting PRNG is seeded from <paramref name="seed"/>.</summary>
    /// <param name="seed">The seed.</param>
    /// <returns>The progression.</returns>
    public static Progression Start(long seed) => new() { Prng = Core.Prng.SeedState(seed) };

    /// <summary>Banks a run: its Ash (win or lose) and a new "since last run" delta.</summary>
    /// <param name="result">How it ended.</param>
    /// <returns>The progression.</returns>
    public Progression BankRun(RunResult result)
    {
        var ash = Ash.ForResult(result);
        return this with { AshTotal = AshTotal + ash, Runs = Runs + 1, LastRun = new RunDelta(result, ash, null, null) };
    }

    /// <summary>
    /// Equips the taken reward in its slot, replacing what was there, with its tier capped at <see cref="MaxTier"/>;
    /// the last run's delta records it and the tier it replaced.
    /// </summary>
    /// <param name="item">The reward.</param>
    /// <returns>The progression.</returns>
    /// <exception cref="ArgumentException">The item's slot is not one of the five gear slots.</exception>
    public Progression Take(Item item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!Rewards.GearSlots.Contains(item.Slot, StringComparer.Ordinal))
        {
            throw new ArgumentException($"unknown gear slot \"{item.Slot}\"", nameof(item));
        }
        var capped = item with { Tier = Math.Min(MaxTier, Math.Max(1, item.Tier)) };
        int? previous = Equipped.TryGetValue(item.Slot, out var old) ? old.Tier : null;
        return this with
        {
            Equipped = With(item.Slot, capped),
            LastRun = LastRun is { } last ? last with { Taken = capped, PreviousTier = previous } : null,
        };
    }

    /// <summary>Rerolls the item equipped in <paramref name="slot"/> (<see cref="Crafting.Reroll"/>).</summary>
    /// <param name="slot">The slot.</param>
    /// <param name="slotPool">That slot's listed Facets.</param>
    /// <returns>The progression (this same instance when refused) and the outcome, or a null outcome when the slot
    /// is empty.</returns>
    public (Progression Progression, RerollResult? Result) Reroll(string slot, IReadOnlyList<Facet> slotPool)
    {
        if (!Equipped.TryGetValue(slot, out var item))
        {
            return (this, null);
        }
        var result = Crafting.Reroll(item, slotPool, Prng);
        return result.Ok ? (this with { Equipped = With(slot, result.Item), Prng = result.Prng }, result) : (this, result);
    }

    private Dictionary<string, Item> With(string slot, Item item) =>
        new(Equipped, StringComparer.Ordinal) { [slot] = item };
}
