namespace Keyrun.Content;

/// <summary>
/// One step of an ability, combo, periodic effect or break-bar payload. Mirrors the
/// <c>EffectOp</c> union in sim/combat/abilities.ts; the JSON <c>"op"</c> field picks the
/// concrete record (see <see cref="EffectOpConverter"/>). An unknown <c>op</c> fails the load.
/// </summary>
public abstract record EffectOp;

/// <summary><c>{"op":"damage"}</c>: deal <see cref="Amount"/> damage of <see cref="School"/>.</summary>
public sealed record DamageOp : EffectOp
{
    /// <summary>Damage amount (integer, pre-mitigation).</summary>
    public required int Amount { get; init; }

    /// <summary>Damage school tag, e.g. "physical" or "fire".</summary>
    public required string School { get; init; }
}

/// <summary><c>{"op":"heal"}</c>: restore <see cref="Amount"/> health.</summary>
public sealed record HealOp : EffectOp
{
    /// <summary>Heal amount (integer).</summary>
    public required int Amount { get; init; }
}

/// <summary><c>{"op":"applyEffect"}</c>: apply effect <see cref="EffectId"/>.</summary>
public sealed record ApplyEffectOp : EffectOp
{
    /// <summary>Id of an <see cref="EffectDef"/>.</summary>
    public required string EffectId { get; init; }

    /// <summary>Duration in sim ticks.</summary>
    public required int DurationTicks { get; init; }

    /// <summary>Stacks applied.</summary>
    public required int Stacks { get; init; }
}

/// <summary><c>{"op":"removeEffect"}</c>: remove effect <see cref="EffectId"/> (<c>"*"</c> = any dispellable).</summary>
public sealed record RemoveEffectOp : EffectOp
{
    /// <summary>Id of an <see cref="EffectDef"/>, or <c>"*"</c>.</summary>
    public required string EffectId { get; init; }
}

/// <summary><c>{"op":"interrupt"}</c>: interrupt the target's cast.</summary>
public sealed record InterruptOp : EffectOp;

/// <summary><c>{"op":"resource"}</c>: change the caster's class resource by <see cref="Delta"/>.</summary>
public sealed record ResourceOp : EffectOp
{
    /// <summary>Signed resource change.</summary>
    public required int Delta { get; init; }

    /// <summary>Optional resource name; only facet patches set it (content-only, not in the sim union).</summary>
    public string? Resource { get; init; }
}

/// <summary><c>{"op":"threat"}</c>: flat and/or multiplied threat.</summary>
public sealed record ThreatOp : EffectOp
{
    /// <summary>Flat threat, if any.</summary>
    public int? Amount { get; init; }

    /// <summary>Threat multiplier in basis points, if any.</summary>
    public int? MultiplierBp { get; init; }
}

/// <summary><c>{"op":"window"}</c>: open a Window (design/BIBLE.md section 4).</summary>
public sealed record WindowOp : EffectOp
{
    /// <summary>Window id, e.g. "sundered".</summary>
    public required string WindowId { get; init; }

    /// <summary>Window lifetime in ticks.</summary>
    public required int DurationTicks { get; init; }

    /// <summary>True: on the target (or self). False: a floor patch.</summary>
    public required bool OnTarget { get; init; }

    /// <summary>
    /// ComboTable row key. Required by the sim union and by <see cref="ContentValidator"/> on
    /// abilities and talents; facet <c>effectsAppend</c> entries omit it (the patch layer fills it).
    /// </summary>
    public string? SetupClass { get; init; }

    /// <summary>Window on the caster itself (boss self-Windows).</summary>
    public bool? Self { get; init; }

    /// <summary>The Lock alternation rule (BIBLE.md section 9).</summary>
    public bool? Alternate { get; init; }

    /// <summary>Floor Window radius override, mm.</summary>
    public int? RadiusMm { get; init; }
}

/// <summary><c>{"op":"payoff"}</c>: marks the ability as a Payoff that closes a Window.</summary>
public sealed record PayoffOp : EffectOp;

/// <summary><c>{"op":"combo"}</c>: content-only facet op naming the ComboTable a Payoff uses.</summary>
public sealed record ComboOp : EffectOp
{
    /// <summary>ComboTable name, e.g. "combos".</summary>
    public required string ComboTable { get; init; }
}

/// <summary>
/// <c>{"op":"partyEffect"}</c>: C#-only combo op (BIBLE section 4 Anthem) applying <see cref="EffectId"/> to every
/// living party member of the Payoff actor. <see cref="ContentValidator"/> allows it only in combo-riders.json.
/// </summary>
public sealed record PartyEffectOp : EffectOp
{
    /// <summary>Id of an <see cref="EffectDef"/>.</summary>
    public required string EffectId { get; init; }

    /// <summary>Duration in sim ticks.</summary>
    public required int DurationTicks { get; init; }

    /// <summary>Stacks applied.</summary>
    public required int Stacks { get; init; }
}

/// <summary>
/// <c>{"op":"cloud"}</c>: C#-only combo op (BIBLE section 4 Steam) opening a floor cloud on the Window's carrier that
/// gives <see cref="EffectId"/> to each enemy inside it while it lasts. Allowed only in combo-riders.json.
/// </summary>
public sealed record CloudOp : EffectOp
{
    /// <summary>Id of an <see cref="EffectDef"/>.</summary>
    public required string EffectId { get; init; }

    /// <summary>Cloud lifetime in sim ticks.</summary>
    public required int DurationTicks { get; init; }

    /// <summary>Cloud radius, mm.</summary>
    public required int RadiusMm { get; init; }
}

/// <summary>
/// One row of the C#-only game/content/data/combo-riders.json: the effects that replace those of combo <see cref="Id"/>
/// when the host builds its ComboTables. It exists because content/data/combos.json is a frozen oracle root and
/// cannot carry the BIBLE section 4 party and cloud effects; oracle runs read their own setup and never see a rider.
/// </summary>
public sealed record ComboRider : IContentItem
{
    /// <summary>Id of the combo (combos.json) whose effects this replaces.</summary>
    public required string Id { get; init; }

    /// <summary>The replacement effects.</summary>
    public required ValueList<EffectOp> Effects { get; init; }
}
