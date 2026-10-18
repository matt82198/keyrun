using System;
using System.Collections.Generic;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;

namespace Keyrun.Sim.Runs;

/// <summary>Ash banked per run (BIBLE s7). RED stub.</summary>
public static class Ash
{
    /// <summary>Ash for a timed clear.</summary>
    public const int Timed = 50;

    /// <summary>Ash for a depleted key.</summary>
    public const int Depleted = 30;

    /// <summary>Ash for an abandoned run.</summary>
    public const int Abandoned = 10;

    /// <summary>Ash for a result.</summary>
    public static int ForResult(RunResult result) => throw new NotImplementedException();
}

/// <summary>One reroll attempt's outcome. RED stub.</summary>
public sealed record RerollResult(bool Ok, Item Item, PrngState Prng, string? Refusal);

/// <summary>Crafting rerolls (BIBLE s7). RED stub.</summary>
public static class Crafting
{
    /// <summary>Rerolls the Facet from the slot's pool.</summary>
    public static RerollResult Reroll(Item item, IReadOnlyList<Facet> slotPool, PrngState prng) => throw new NotImplementedException();
}

/// <summary>What the last run changed. RED stub.</summary>
public sealed record RunDelta(RunResult Result, int AshGained, Item? Taken, int? PreviousTier)
{
    /// <summary>The "since last run" line.</summary>
    public string Text() => throw new NotImplementedException();
}

/// <summary>The character's progression. RED stub.</summary>
public sealed record Progression
{
    /// <summary>Highest tier.</summary>
    public const int MaxTier = 10;

    /// <summary>Ash banked.</summary>
    public long AshTotal { get; init; }

    /// <summary>Runs banked.</summary>
    public int Runs { get; init; }

    /// <summary>Equipped items by slot.</summary>
    public IReadOnlyDictionary<string, Item> Equipped { get; init; } = new Dictionary<string, Item>();

    /// <summary>Crafting PRNG.</summary>
    public PrngState Prng { get; init; }

    /// <summary>The last run's delta.</summary>
    public RunDelta? LastRun { get; init; }

    /// <summary>A fresh character.</summary>
    public static Progression Start(long seed) => throw new NotImplementedException();

    /// <summary>Banks a run.</summary>
    public Progression BankRun(RunResult result) => throw new NotImplementedException();

    /// <summary>Equips a reward.</summary>
    public Progression Take(Item item) => throw new NotImplementedException();

    /// <summary>Rerolls an equipped item.</summary>
    public (Progression Progression, RerollResult Result) Reroll(string slot, IReadOnlyList<Facet> slotPool) => throw new NotImplementedException();
}
