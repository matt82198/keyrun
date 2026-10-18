using System.Runtime.Serialization;

namespace Keyrun.Content;

/// <summary>Class resource loop kind (content/schema/class.ts LoopKind).</summary>
public enum LoopKind
{
    /// <summary>Builders generate, spenders spend.</summary>
    [EnumMember(Value = "builder-spender")] BuilderSpender,

    /// <summary>Heat builds toward overheat.</summary>
    [EnumMember(Value = "heat-overheat")] HeatOverheat,

    /// <summary>Stance switching.</summary>
    [EnumMember(Value = "stance")] Stance,
}

/// <summary>A class's resource loop.</summary>
public sealed record ClassLoop
{
    /// <summary>Resource name.</summary>
    public required string Resource { get; init; }

    /// <summary>Resource cap.</summary>
    public required int Max { get; init; }

    /// <summary>Loop kind.</summary>
    public required LoopKind Kind { get; init; }
}

/// <summary>Exactly five rotational slots plus the four fixed utility keys (BIBLE.md D4).</summary>
public sealed record ClassAbilitySlots
{
    /// <summary>Rotational slot 1 ability id.</summary>
    public required string Rot1 { get; init; }

    /// <summary>Rotational slot 2 ability id.</summary>
    public required string Rot2 { get; init; }

    /// <summary>Rotational slot 3 ability id.</summary>
    public required string Rot3 { get; init; }

    /// <summary>Rotational slot 4 ability id.</summary>
    public required string Rot4 { get; init; }

    /// <summary>Rotational slot 5 ability id.</summary>
    public required string Rot5 { get; init; }

    /// <summary>Interrupt ability id.</summary>
    public required string Interrupt { get; init; }

    /// <summary>Defensive ability id.</summary>
    public required string Defensive { get; init; }

    /// <summary>Utility ability id.</summary>
    public required string Utility { get; init; }

    /// <summary>Movement ability id.</summary>
    public required string Movement { get; init; }
}

/// <summary>One talent choice.</summary>
public sealed record TalentOption
{
    /// <summary>Talent id.</summary>
    public required string Id { get; init; }

    /// <summary>Display name.</summary>
    public required string Name { get; init; }

    /// <summary>Tooltip text.</summary>
    public required string Text { get; init; }

    /// <summary>Patch applied to the fork's ability.</summary>
    public required AbilityPatch Patch { get; init; }
}

/// <summary>A talent fork on one ability.</summary>
public sealed record TalentFork
{
    /// <summary>The ability every option patches.</summary>
    public required string AbilityId { get; init; }

    /// <summary>The options to pick between.</summary>
    public required ValueList<TalentOption> Options { get; init; }
}

/// <summary>A playable class (content/schema/class.ts ClassDef).</summary>
public sealed record ClassDef : IContentItem
{
    /// <inheritdoc />
    public required string Id { get; init; }

    /// <summary>Display name.</summary>
    public required string Name { get; init; }

    /// <summary>Resource loop.</summary>
    public required ClassLoop Loop { get; init; }

    /// <summary>Ability slots.</summary>
    public required ClassAbilitySlots Abilities { get; init; }

    /// <summary>Talent forks.</summary>
    public required ValueList<TalentFork> Talents { get; init; }
}
