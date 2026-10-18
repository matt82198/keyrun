namespace Keyrun.Content;

/// <summary>Basis-point modifiers an effect applies while active (sim/combat/effects.ts).</summary>
public sealed record EffectModifiers
{
    /// <summary>Damage done change, bp.</summary>
    public int? DamageDoneBp { get; init; }

    /// <summary>Damage taken change, bp.</summary>
    public int? DamageTakenBp { get; init; }

    /// <summary>Haste change, bp.</summary>
    public int? HasteBp { get; init; }

    /// <summary>Movement speed change, bp.</summary>
    public int? MovementBp { get; init; }

    /// <summary>Absorb amount.</summary>
    public int? Absorb { get; init; }
}

/// <summary>A periodic tick: run <see cref="Op"/> every <see cref="EveryTicks"/> ticks.</summary>
public sealed record EffectPeriodic
{
    /// <summary>Tick interval.</summary>
    public required int EveryTicks { get; init; }

    /// <summary>Op run each interval.</summary>
    public required EffectOp Op { get; init; }
}

/// <summary>
/// An effect definition (content/schema/effect.ts EffectDef = sim EffectDef + name). The sim type
/// declares <c>periodic: ... | null</c> as always present; the JSON omits it on 18 of 22 effects,
/// so here absent and null both mean "no periodic tick".
/// </summary>
public sealed record EffectDef : IContentItem
{
    /// <inheritdoc />
    public required string Id { get; init; }

    /// <summary>Display name.</summary>
    public required string Name { get; init; }

    /// <summary>Maximum stacks.</summary>
    public required int MaxStacks { get; init; }

    /// <summary>Dispel tag ("magic", "physical") or null when not dispellable. Must be present.</summary>
    public required string? DispellableTag { get; init; }

    /// <summary>Stat modifiers while active.</summary>
    public required EffectModifiers Modifiers { get; init; }

    /// <summary>Periodic tick, or null.</summary>
    public EffectPeriodic? Periodic { get; init; }
}
