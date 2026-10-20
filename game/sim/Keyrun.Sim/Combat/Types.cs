using System.Collections.Generic;

namespace Keyrun.Sim.Combat;

// Shared combat type contracts (lane 2A; lane 2B takes them as given, ENGINE-PLAN section 5).
// sim/combat/types.ts only re-exports sim/core types (ActorState, SimState, EffectInstance, ...); those live in
// Keyrun.Sim.Core. This file adds the combat-side shapes the TS declares in sim/combat/abilities.ts
// (EffectOp, AbilityShape, AbilityDef) and sim/combat/effects.ts (EffectModifiers, EffectDef), because the
// lane-2A modules (Cooldowns, Targeting, Effects, EffectsRemove) need them before 2B ports abilities.ts.
// Content-to-sim mapping (Keyrun.Content records to these) is the run root's job (lane 2D).

/// <summary>Ability shape kind (sim/combat/abilities.ts:40 AbilityShape.kind).</summary>
public enum ShapeKind
{
    /// <summary>"target": the caster's current targetId.</summary>
    Target,

    /// <summary>"self": the caster.</summary>
    Self,

    /// <summary>"cone": radius + angle1024 ahead of the caster.</summary>
    Cone,

    /// <summary>"circle": radius around the caster.</summary>
    Circle,

    /// <summary>"line": length ahead, radius = half-width.</summary>
    Line,
}

/// <summary>An ability's area (sim/combat/abilities.ts:39-44). Absent TS fields are null.</summary>
/// <param name="Kind">Shape kind.</param>
/// <param name="Radius">Radius (circle, cone) or half-width (line), mm.</param>
/// <param name="Angle1024">Cone full angle, 1/1024 turn.</param>
/// <param name="Length">Line length, mm.</param>
public sealed record AbilityShape(ShapeKind Kind, long? Radius = null, long? Angle1024 = null, long? Length = null);

/// <summary>An ability's cost (sim/combat/abilities.ts:52).</summary>
/// <param name="Resource">Resource name.</param>
/// <param name="Amount">Integer amount.</param>
public sealed record AbilityCost(string Resource, long Amount);

/// <summary>One step of an ability, combo or periodic effect (sim/combat/abilities.ts:7-37 EffectOp union).</summary>
public abstract record EffectOp;

/// <summary><c>{op:"damage", amount, school}</c> (abilities.ts:8).</summary>
/// <param name="Amount">Raw damage.</param>
/// <param name="School">School tag.</param>
public sealed record DamageOp(long Amount, string School) : EffectOp;

/// <summary><c>{op:"heal", amount}</c> (abilities.ts:9).</summary>
/// <param name="Amount">Heal amount.</param>
public sealed record HealOp(long Amount) : EffectOp;

/// <summary><c>{op:"applyEffect", effectId, durationTicks, stacks}</c> (abilities.ts:10).</summary>
/// <param name="EffectId">Effect definition id.</param>
/// <param name="DurationTicks">Duration.</param>
/// <param name="Stacks">Stacks applied.</param>
public sealed record ApplyEffectOp(string EffectId, int DurationTicks, long Stacks) : EffectOp;

/// <summary><c>{op:"removeEffect", effectId}</c>, "*" = dispel (abilities.ts:11).</summary>
/// <param name="EffectId">Effect id or "*".</param>
public sealed record RemoveEffectOp(string EffectId) : EffectOp;

/// <summary><c>{op:"interrupt"}</c> (abilities.ts:12).</summary>
public sealed record InterruptOp : EffectOp;

/// <summary><c>{op:"resource", delta}</c>: credited to the caster (abilities.ts:13, mitigation.ts:13-14).</summary>
/// <param name="Delta">Signed change.</param>
public sealed record ResourceOp(long Delta) : EffectOp;

/// <summary><c>{op:"threat", amount?, multiplierBp?}</c> (abilities.ts:14).</summary>
/// <param name="Amount">Flat threat, or null.</param>
/// <param name="MultiplierBp">Multiplier in bp, or null.</param>
public sealed record ThreatOp(long? Amount = null, long? MultiplierBp = null) : EffectOp;

/// <summary><c>{op:"window", ...}</c>: opens a Window (abilities.ts:31, BIBLE section 4).</summary>
/// <param name="WindowId">Window id.</param>
/// <param name="DurationTicks">Lifetime.</param>
/// <param name="OnTarget">True: on the target (or self); false: floor patch.</param>
/// <param name="SetupClass">ComboTable row key.</param>
/// <param name="Self">On the caster itself, or null.</param>
/// <param name="Alternate">The Lock alternation rule, or null.</param>
/// <param name="RadiusMm">Floor Window radius override, or null.</param>
public sealed record WindowOp(
    string WindowId,
    int DurationTicks,
    bool OnTarget,
    string SetupClass,
    bool? Self = null,
    bool? Alternate = null,
    long? RadiusMm = null) : EffectOp;

/// <summary><c>{op:"payoff"}</c>: marks a Payoff ability (abilities.ts:37).</summary>
public sealed record PayoffOp : EffectOp;

/// <summary>A data-driven ability (sim/combat/abilities.ts:46-60 AbilityDef).</summary>
public sealed record AbilityDef
{
    /// <summary>Ability id.</summary>
    public required string Id { get; init; }

    /// <summary>Cast time in ticks (0 = instant).</summary>
    public required int CastTicks { get; init; }

    /// <summary>Global cooldown it starts, ticks.</summary>
    public required int GcdTicks { get; init; }

    /// <summary>Cooldown per charge, ticks (0 = none).</summary>
    public required int CooldownTicks { get; init; }

    /// <summary>Maximum charges (&gt;= 1).</summary>
    public required long Charges { get; init; }

    /// <summary>Cost.</summary>
    public required AbilityCost Cost { get; init; }

    /// <summary>Range, mm.</summary>
    public required long Range { get; init; }

    /// <summary>Area.</summary>
    public required AbilityShape Shape { get; init; }

    /// <summary>Whether a kick can interrupt it.</summary>
    public required bool Interruptible { get; init; }

    /// <summary>Ops, in order.</summary>
    public required IReadOnlyList<EffectOp> Effects { get; init; }

    /// <summary>Free-form tags, or null (abilities.ts:59 optional).</summary>
    public IReadOnlyList<string>? Tags { get; init; }

    /// <summary>
    /// Per-verse replacement ops (content <c>variants</c>, keyed "mend"/"ward"; BIBLE section 5 Chanter), or null.
    /// <see cref="Abilities.ResolveVerse"/> picks one from the caster's verse at cast completion. Null (every oracle
    /// setup) = <see cref="Effects"/> always.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<EffectOp>>? Variants { get; init; }
}

/// <summary>Basis-point modifiers and absorb an effect carries per stack (sim/combat/effects.ts:15-21). Absent = null.</summary>
/// <param name="DamageDoneBp">Damage done change.</param>
/// <param name="DamageTakenBp">Damage taken change.</param>
/// <param name="HasteBp">Haste change.</param>
/// <param name="MovementBp">Movement change.</param>
/// <param name="Absorb">Flat absorb per stack.</param>
public sealed record EffectModifiers(
    long? DamageDoneBp = null,
    long? DamageTakenBp = null,
    long? HasteBp = null,
    long? MovementBp = null,
    long? Absorb = null);

/// <summary>A periodic tick: run <see cref="Op"/> every <see cref="EveryTicks"/> (effects.ts:28).</summary>
/// <param name="EveryTicks">Interval.</param>
/// <param name="Op">Op run each interval.</param>
public sealed record EffectPeriodic(int EveryTicks, EffectOp Op);

/// <summary>An effect definition (sim/combat/effects.ts:23-29 EffectDef).</summary>
/// <param name="Id">Effect id.</param>
/// <param name="MaxStacks">Stack cap.</param>
/// <param name="DispellableTag">Dispel tag, or null when not dispellable.</param>
/// <param name="Modifiers">Per-stack modifiers.</param>
/// <param name="Periodic">Periodic tick, or null.</param>
public sealed record EffectDef(string Id, long MaxStacks, string? DispellableTag, EffectModifiers Modifiers, EffectPeriodic? Periodic = null);

/// <summary>Shape-target faction filter (sim/combat/affinity.ts:49). Lane 2B's Affinity.cs classifies abilities.</summary>
public enum Affinity
{
    /// <summary>Damage/debuff: keeps the opposing kind, excludes the caster.</summary>
    Hostile,

    /// <summary>Heal/buff: keeps the caster's own kind, includes the caster.</summary>
    Friendly,
}

/// <summary>
/// The active run's Pact weighting of a raw hit (sim/encounter/clauses.ts:142 applyPactDamageBp, read in
/// sim/combat/damage.ts:54-55 through clause-hooks.ts's module-scoped getActiveEncounterRules/isComboActive).
/// C# passes it explicitly instead of a process-global: null is the TS "no active rules" branch (raw passes
/// through); the supplier (lanes 2B/2C) binds the rules and the combo flag.
/// </summary>
/// <param name="rawAmount">The raw hit.</param>
/// <returns>The Pact-weighted amount.</returns>
public delegate long DamageWeighting(long rawAmount);
