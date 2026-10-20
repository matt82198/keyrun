using System.Runtime.Serialization;

namespace Keyrun.Content;

/// <summary>A combo row (content/schema/combo.ts ComboEntry).</summary>
public sealed record ComboEntry : IContentItem
{
    /// <inheritdoc />
    public required string Id { get; init; }

    /// <summary>Display name.</summary>
    public required string Name { get; init; }

    /// <summary>Setup class id, or the pseudo-class "boss".</summary>
    public required string SetupClass { get; init; }

    /// <summary>Payoff class id, or "*" (any class other than the setup class).</summary>
    public required string PayoffClass { get; init; }

    /// <summary>Window id this combo closes.</summary>
    public required string WindowId { get; init; }

    /// <summary>Ops fired when the combo lands.</summary>
    public required ValueList<EffectOp> Effects { get; init; }
}

/// <summary>Enemy role (content/schema/enemy.ts EnemyRole).</summary>
public enum EnemyRole
{
    /// <summary>Melee trash.</summary>
    [EnumMember(Value = "melee")] Melee,

    /// <summary>Caster trash.</summary>
    [EnumMember(Value = "caster")] Caster,

    /// <summary>Lieutenant.</summary>
    [EnumMember(Value = "lieutenant")] Lieutenant,

    /// <summary>Elite.</summary>
    [EnumMember(Value = "elite")] Elite,

    /// <summary>Boss.</summary>
    [EnumMember(Value = "boss")] Boss,
}

/// <summary>Telegraph geometry kind (sim/encounter/scripts.ts TelegraphSpec.shape).</summary>
public enum TelegraphShape
{
    /// <summary>Circle.</summary>
    [EnumMember(Value = "circle")] Circle,

    /// <summary>Cone.</summary>
    [EnumMember(Value = "cone")] Cone,

    /// <summary>Line.</summary>
    [EnumMember(Value = "line")] Line,
}

/// <summary>Script target override (sim/encounter/script-target.ts ScriptTargetOverride).</summary>
public enum ScriptTarget
{
    /// <summary>The caster.</summary>
    [EnumMember(Value = "self")] Self,

    /// <summary>Top of the threat table.</summary>
    [EnumMember(Value = "threat")] Threat,

    /// <summary>Seeded random party member.</summary>
    [EnumMember(Value = "random")] Random,
}

/// <summary>A ground telegraph shown before a scripted cast lands.</summary>
public sealed record TelegraphSpec
{
    /// <summary>Geometry kind.</summary>
    public required TelegraphShape Shape { get; init; }

    /// <summary>Radius, mm.</summary>
    public int? Radius { get; init; }

    /// <summary>Cone angle, 1/1024 turns.</summary>
    public int? Angle1024 { get; init; }

    /// <summary>Line length, mm.</summary>
    public int? Length { get; init; }

    /// <summary>Line width, mm.</summary>
    public int? Width { get; init; }

    /// <summary>Ticks between telegraph start and the cast landing.</summary>
    public required int LeadTicks { get; init; }

    /// <summary>
    /// SLAM-TELEGRAPH (BIBLE s3): true when the telegraph resolves on the tick the cast lands (fire tick plus the
    /// ability's castTicks) rather than when it starts, so the floor shape stays down for the whole cast bar. Absent =
    /// false (the frozen TS rule). Only valid on a step whose ability has castTicks &gt; 0.
    /// </summary>
    public bool? SpansCast { get; init; }
}

/// <summary>How a <see cref="ScriptAt"/> trigger fires.</summary>
public enum ScriptAtKind
{
    /// <summary>JSON <c>"loop"</c>: every <see cref="ScriptStep.Every"/> ticks.</summary>
    [EnumMember(Value = "loop")] Loop,

    /// <summary>JSON <c>"onDeath"</c>: once when the enemy dies.</summary>
    [EnumMember(Value = "onDeath")] OnDeath,

    /// <summary>JSON integer: at that tick (then every <see cref="ScriptStep.Every"/>, if set).</summary>
    [EnumMember(Value = "tick")] Tick,
}

/// <summary>
/// A script step's <c>at</c> field: the JSON is either the string "loop", the string "onDeath",
/// or an integer tick (sim/encounter/scripts.ts ScriptStep.at). See <see cref="ScriptAtConverter"/>.
/// </summary>
public sealed record ScriptAt
{
    /// <summary>Trigger kind.</summary>
    public required ScriptAtKind Kind { get; init; }

    /// <summary>The tick when <see cref="Kind"/> is <see cref="ScriptAtKind.Tick"/>; otherwise 0.</summary>
    public required int Tick { get; init; }
}

/// <summary>One step of an enemy AI script.</summary>
public sealed record ScriptStep
{
    /// <summary>When the step fires.</summary>
    public required ScriptAt At { get; init; }

    /// <summary>Repeat interval in ticks.</summary>
    public int? Every { get; init; }

    /// <summary>Ability id cast.</summary>
    public required string Cast { get; init; }

    /// <summary>Optional telegraph.</summary>
    public TelegraphSpec? Telegraph { get; init; }

    /// <summary>Optional target-selection override.</summary>
    public ScriptTarget? Target { get; init; }
}

/// <summary>A Break bar shared by several enemies (Sundered Twins, BIBLE.md section 9).</summary>
public sealed record BreakBarDef
{
    /// <summary>Bar id that <see cref="EnemyDef.BreakBarId"/> points at.</summary>
    public required string Id { get; init; }

    /// <summary>Bar size.</summary>
    public required int Max { get; init; }

    /// <summary>Drain per interrupt-tagged hit.</summary>
    public required int DrainPerInterrupt { get; init; }

    /// <summary>Drain per control-tagged hit.</summary>
    public required int DrainPerControl { get; init; }

    /// <summary>Ops fired when the bar empties.</summary>
    public required ValueList<EffectOp> OnEmpty { get; init; }
}

/// <summary>An enemy (content/schema/enemy.ts EnemyDef).</summary>
public sealed record EnemyDef : IContentItem
{
    /// <inheritdoc />
    public required string Id { get; init; }

    /// <summary>Display name.</summary>
    public required string Name { get; init; }

    /// <summary>Max health.</summary>
    public required int MaxHealth { get; init; }

    /// <summary>Role.</summary>
    public required EnemyRole Role { get; init; }

    /// <summary>Ability ids this enemy owns.</summary>
    public required ValueList<string> Abilities { get; init; }

    /// <summary>AI script.</summary>
    public required ValueList<ScriptStep> Script { get; init; }

    /// <summary>Walk speed, mm/tick; null = the run default.</summary>
    public int? SpeedMmPerTick { get; init; }

    /// <summary>Chase stop margin, mm; null = the sim default.</summary>
    public int? ChaseMarginMm { get; init; }

    /// <summary>Id of a <see cref="BreakBarDef"/> declared on another enemy.</summary>
    public string? BreakBarId { get; init; }

    /// <summary>Break bars declared by this (boss) entry.</summary>
    public ValueList<BreakBarDef>? BreakBars { get; init; }
}

/// <summary>Spawn point, mm.</summary>
public sealed record SpawnPoint
{
    /// <summary>X, mm.</summary>
    public required int X { get; init; }

    /// <summary>Z, mm.</summary>
    public required int Z { get; init; }
}

/// <summary>An enemy group inside a pull.</summary>
public sealed record PullEnemy
{
    /// <summary>Enemy id.</summary>
    public required string EnemyId { get; init; }

    /// <summary>How many spawn.</summary>
    public required int Count { get; init; }

    /// <summary>Where they spawn.</summary>
    public required SpawnPoint Spawn { get; init; }
}

/// <summary>One trash pull.</summary>
public sealed record PullDef
{
    /// <summary>Pull id.</summary>
    public required string Id { get; init; }

    /// <summary>Display name.</summary>
    public required string Name { get; init; }

    /// <summary>Enemy groups.</summary>
    public required ValueList<PullEnemy> Enemies { get; init; }

    /// <summary>What the pull teaches.</summary>
    public required string Teaches { get; init; }
}

/// <summary>The dungeon's boss encounter.</summary>
public sealed record BossEncounter
{
    /// <summary>Boss enemy id.</summary>
    public required string EnemyId { get; init; }

    /// <summary>Mechanic names, for display.</summary>
    public required ValueList<string> Mechanics { get; init; }
}

/// <summary>A dungeon authoring recipe (content/schema/dungeon.ts DungeonDef).</summary>
public sealed record DungeonDef : IContentItem
{
    /// <inheritdoc />
    public required string Id { get; init; }

    /// <summary>Display name.</summary>
    public required string Name { get; init; }

    /// <summary>Par time, ticks.</summary>
    public required int ParTicks { get; init; }

    /// <summary>Trash pulls in order.</summary>
    public required ValueList<PullDef> Pulls { get; init; }

    /// <summary>Boss encounter.</summary>
    public required BossEncounter Boss { get; init; }
}

/// <summary>Clause kind (content/schema/clause.ts ClauseKind).</summary>
public enum ClauseKind
{
    /// <summary>Echo.</summary>
    [EnumMember(Value = "echo")] Echo,

    /// <summary>Tide.</summary>
    [EnumMember(Value = "tide")] Tide,

    /// <summary>Hush.</summary>
    [EnumMember(Value = "hush")] Hush,

    /// <summary>Pact.</summary>
    [EnumMember(Value = "pact")] Pact,
}

/// <summary>A run-wide Clause modifier (content/schema/clause.ts ClauseDef).</summary>
public sealed record ClauseDef : IContentItem
{
    /// <inheritdoc />
    public required string Id { get; init; }

    /// <summary>Display name.</summary>
    public required string Name { get; init; }

    /// <summary>Kind.</summary>
    public required ClauseKind Kind { get; init; }

    /// <summary>Integer parameters by name.</summary>
    public required ValueMap<int> Params { get; init; }

    /// <summary>Description.</summary>
    public required string Description { get; init; }
}
