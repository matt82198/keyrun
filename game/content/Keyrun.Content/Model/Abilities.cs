using System.Runtime.Serialization;

namespace Keyrun.Content;

/// <summary>A content item addressable by id in a <see cref="ContentRegistry"/> map.</summary>
public interface IContentItem
{
    /// <summary>Unique id within its kind.</summary>
    string Id { get; }
}

/// <summary>Ability targeting shape kind (sim/combat/abilities.ts AbilityShape.kind).</summary>
public enum ShapeKind
{
    /// <summary>Single target.</summary>
    [EnumMember(Value = "target")] Target,

    /// <summary>Caster only.</summary>
    [EnumMember(Value = "self")] Self,

    /// <summary>Cone in front of the caster.</summary>
    [EnumMember(Value = "cone")] Cone,

    /// <summary>Circle.</summary>
    [EnumMember(Value = "circle")] Circle,

    /// <summary>Line.</summary>
    [EnumMember(Value = "line")] Line,
}

/// <summary>Ability shape; all lengths in mm, angles in 1/1024 of a turn.</summary>
public sealed record AbilityShape
{
    /// <summary>Shape kind.</summary>
    public required ShapeKind Kind { get; init; }

    /// <summary>Radius, mm.</summary>
    public int? Radius { get; init; }

    /// <summary>Cone angle in 1/1024 turns.</summary>
    public int? Angle1024 { get; init; }

    /// <summary>Line length, mm.</summary>
    public int? Length { get; init; }

    /// <summary>Line width, mm (facet patches only; not in the sim AbilityShape).</summary>
    public int? Width { get; init; }
}

/// <summary>Ability resource cost.</summary>
public sealed record AbilityCost
{
    /// <summary>Resource name.</summary>
    public required string Resource { get; init; }

    /// <summary>Amount spent.</summary>
    public required int Amount { get; init; }
}

/// <summary>
/// One ability (content/schema/ability.ts AbilityDef = sim AbilityDef + name, required tags and
/// optional Chanter stance <see cref="Variants"/>).
/// </summary>
public sealed record AbilityDef : IContentItem
{
    /// <inheritdoc />
    public required string Id { get; init; }

    /// <summary>Display name.</summary>
    public required string Name { get; init; }

    /// <summary>Cast time in ticks (0 = instant).</summary>
    public required int CastTicks { get; init; }

    /// <summary>Global cooldown in ticks.</summary>
    public required int GcdTicks { get; init; }

    /// <summary>Cooldown in ticks.</summary>
    public required int CooldownTicks { get; init; }

    /// <summary>Charges.</summary>
    public required int Charges { get; init; }

    /// <summary>Resource cost.</summary>
    public required AbilityCost Cost { get; init; }

    /// <summary>Range, mm.</summary>
    public required int Range { get; init; }

    /// <summary>Targeting shape.</summary>
    public required AbilityShape Shape { get; init; }

    /// <summary>Whether the cast can be interrupted.</summary>
    public required bool Interruptible { get; init; }

    /// <summary>Classification tags (e.g. "setup", "payoff").</summary>
    public required ValueList<string> Tags { get; init; }

    /// <summary>Ops applied on cast completion.</summary>
    public required ValueList<EffectOp> Effects { get; init; }

    /// <summary>Per-stance effect override ("mend"/"ward"); null = <see cref="Effects"/> always applies.</summary>
    public ValueMap<ValueList<EffectOp>>? Variants { get; init; }
}

/// <summary>
/// A talent patch: <c>Partial&lt;AbilityDef&gt;</c> without <c>id</c>. Any key outside this set
/// (including <c>id</c>, which TS validate.ts forbids) fails the load.
/// </summary>
public record AbilityPatch
{
    /// <summary>Replacement display name.</summary>
    public string? Name { get; init; }

    /// <summary>Replacement cast time.</summary>
    public int? CastTicks { get; init; }

    /// <summary>Replacement GCD.</summary>
    public int? GcdTicks { get; init; }

    /// <summary>Replacement cooldown.</summary>
    public int? CooldownTicks { get; init; }

    /// <summary>Replacement charges.</summary>
    public int? Charges { get; init; }

    /// <summary>Replacement cost.</summary>
    public AbilityCost? Cost { get; init; }

    /// <summary>Replacement range.</summary>
    public int? Range { get; init; }

    /// <summary>Replacement shape.</summary>
    public AbilityShape? Shape { get; init; }

    /// <summary>Replacement interruptible flag.</summary>
    public bool? Interruptible { get; init; }

    /// <summary>Replacement tags.</summary>
    public ValueList<string>? Tags { get; init; }

    /// <summary>Replacement effects.</summary>
    public ValueList<EffectOp>? Effects { get; init; }

    /// <summary>Replacement stance variants.</summary>
    public ValueMap<ValueList<EffectOp>>? Variants { get; init; }
}
