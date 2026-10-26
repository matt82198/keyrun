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

/// <summary>
/// TELEGRAPH-ANCHOR (finding f-ebb50027, BIBLE section 3 and 9.3): where a script telegraph's floor shape is drawn and
/// resolved. C#-only; the frozen TS has no anchor.
/// </summary>
public enum TelegraphAnchor
{
    /// <summary>At the caster (the default, and the frozen TS rule).</summary>
    [EnumMember(Value = "caster")] Caster,

    /// <summary>At the caster's current target; no telegraph is drawn on a tick the caster has none.</summary>
    [EnumMember(Value = "target")] Target,
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

    /// <summary>
    /// TELEGRAPH-ANCHOR: "caster" or "target" (<see cref="TelegraphAnchor"/>). Absent = caster. A target anchor is valid
    /// only on a circle: a cone or line keeps the caster's facing, so drawn from the target it would mark the wrong ground.
    /// </summary>
    public TelegraphAnchor? Anchor { get; init; }
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

    /// <summary>
    /// SCRIPT-GATE (D66, the Sunken Bellfoundry): a condition on the caster the step needs to fire, or null (always).
    /// C#-only; the frozen TS has no gate.
    /// </summary>
    public ScriptGate? When { get; init; }
}

/// <summary>
/// SCRIPT-GATE (D66): a script step fires only while its caster meets this condition. Exactly one field is set
/// (<see cref="ContentValidator"/>). <see cref="HasEffect"/>: the caster carries that effect (an onDeath step reads the
/// effects it died with), so a death burst can depend on a cast the party failed to kick. <see cref="HealthBelowBp"/>:
/// the caster's health is below that share of its max, basis points, so an enemy can enrage below 30%.
/// </summary>
public sealed record ScriptGate
{
    /// <summary>An effect id the caster must carry, or null.</summary>
    public string? HasEffect { get; init; }

    /// <summary>Health threshold, basis points of max health (1..9999): the step fires only below it; or null.</summary>
    public int? HealthBelowBp { get; init; }
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

    /// <summary>Collision cylinder radius, mm (R-BOSSROOM-1 b); null = the run default actor radius.</summary>
    public long? RadiusMm { get; init; }

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

    /// <summary>Boss encounter (the final boss: the last segment's).</summary>
    public required BossEncounter Boss { get; init; }

    /// <summary>
    /// D65/D67: true for the tutorial (the Hollow Vault: hand-placed teaching pulls, one boss); false for a seeded
    /// dungeon, which needs at least <see cref="MinSegmentsPastTutorial"/> segments.
    /// </summary>
    public required bool Tutorial { get; init; }

    /// <summary>
    /// D65: the segments in route order (entrance, fork, boss, fork, boss, ...). Each ends at its boss; the last
    /// segment's boss is <see cref="Boss"/>. The generator lays one segment per entry (DungeonGenParams.Segments).
    /// </summary>
    public required ValueList<DungeonSegment> Segments { get; init; }

    /// <summary>
    /// D46: the pack roster the fill step deals from, by id: <see cref="RosterPack"/> ids of this dungeon, or (the
    /// tutorial) its hand-placed layout pack ids.
    /// </summary>
    public required ValueList<string> PackPool { get; init; }

    /// <summary>Matt 2026-10-22, verbatim: "any dungeon past tutorial should have 3 bosses" "min".</summary>
    public const int MinSegmentsPastTutorial = 3;
}

/// <summary>One segment of a dungeon (D65): a fork into branches that meet at this segment's boss.</summary>
public sealed record DungeonSegment
{
    /// <summary>Segment id, unique in its dungeon (e.g. <c>s1</c>).</summary>
    public required string Id { get; init; }

    /// <summary>The boss the segment ends at.</summary>
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
