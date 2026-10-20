using System.Collections.Generic;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Encounter;

/// <summary>What schedules a <see cref="ScriptStep"/> (sim/encounter/scripts.ts:76 <c>at</c>).</summary>
public enum StepTrigger
{
    /// <summary><c>at: "loop"</c>: every <c>every</c> ticks, first at tick <c>every</c> (never tick 0).</summary>
    Loop,

    /// <summary><c>at: &lt;number&gt;</c>: once at that tick, or at <c>at + k * every</c> when <c>every</c> is set.</summary>
    AtTick,

    /// <summary><c>at: "onDeath"</c>: once, the first step that sees the actor dead.</summary>
    OnDeath,
}

/// <summary>Per-step target override (sim/encounter/script-target.ts:43 ScriptTargetOverride).</summary>
public enum ScriptTargetOverride
{
    /// <summary>"self": always the caster.</summary>
    Self,

    /// <summary>"threat": the player pool with threat-then-nearest selection, even for a heal.</summary>
    Threat,

    /// <summary>"random": the normal pool, one in-range candidate picked by a derived PRNG draw.</summary>
    Random,
}

/// <summary>
/// A telegraph a script step announces <see cref="LeadTicks"/> before its cast (sim/encounter/scripts.ts:66 TelegraphSpec).
/// </summary>
/// <param name="Shape">"circle", "cone" or "line".</param>
/// <param name="LeadTicks">Ticks between the announcement and the cast it warns of.</param>
public sealed record TelegraphSpec(string Shape, int LeadTicks)
{
    /// <summary>Circle and cone radius, mm.</summary>
    public long? Radius { get; init; }

    /// <summary>Full cone angle, 1/1024 turn.</summary>
    public int? Angle1024 { get; init; }

    /// <summary>Line length, mm.</summary>
    public long? Length { get; init; }

    /// <summary>Line full width, mm.</summary>
    public long? Width { get; init; }

    /// <summary>
    /// SLAM-TELEGRAPH (BIBLE s3, "a floor telegraph drawn ahead of resolution"): when true the telegraph resolves on
    /// the tick the cast LANDS (the script's fire tick plus the ability's <see cref="ScriptAbility.CastTicks"/>) and is
    /// announced <see cref="LeadTicks"/> before that, so with a lead of at least the cast time it stays on the floor for
    /// the whole cast bar. False (the default, and the frozen TS rule): it resolves on the fire tick, when the cast
    /// starts. C#-only; oracle replays never apply it (tools/Keyrun.Replay RunSetup clears it).
    /// </summary>
    public bool SpansCast { get; init; }
}

/// <summary>
/// One step of an enemy script (sim/encounter/scripts.ts:75 ScriptStep). <see cref="AtTick"/> is meaningful only
/// for <see cref="StepTrigger.AtTick"/>; <see cref="Every"/> is the loop period or the phase-offset period.
/// </summary>
/// <param name="Trigger">How the step is scheduled.</param>
/// <param name="Cast">The ability id cast when it fires.</param>
public sealed record ScriptStep(StepTrigger Trigger, string Cast)
{
    /// <summary>The absolute tick of a numeric <c>at</c> (0 for the other triggers).</summary>
    public int AtTick { get; init; }

    /// <summary>Loop period, or the repeat period of a numeric <c>at</c>; absent means "not set" (TS undefined).</summary>
    public int? Every { get; init; }

    /// <summary>The telegraph announced ahead of the cast, if any.</summary>
    public TelegraphSpec? Telegraph { get; init; }

    /// <summary>Target override; absent means the default rules of <see cref="ScriptTarget"/>.</summary>
    public ScriptTargetOverride? Target { get; init; }

    /// <summary>A <c>{at:"loop", every, cast}</c> step.</summary>
    public static ScriptStep Loop(int? every, string cast) => new(StepTrigger.Loop, cast) { Every = every };

    /// <summary>A <c>{at:&lt;tick&gt;, every?, cast}</c> step.</summary>
    public static ScriptStep At(int tick, string cast, int? every = null) => new(StepTrigger.AtTick, cast) { AtTick = tick, Every = every };

    /// <summary>A <c>{at:"onDeath", cast}</c> step.</summary>
    public static ScriptStep OnDeath(string cast) => new(StepTrigger.OnDeath, cast);
}

/// <summary>
/// The encounter's minimal view of an ability (the fields of sim/combat/abilities.ts:46 AbilityDef that
/// sim/encounter/script-target.ts and chase.ts read). Encounter keeps its own shape, as pulls.ts does for
/// DungeonDef, so it does not depend on the combat lanes' types; the run root adapts AbilityDef to this.
/// </summary>
/// <param name="Id">Ability id.</param>
/// <param name="Range">Range in mm (<c>def.range</c>).</param>
/// <param name="ShapeKind"><c>def.shape.kind</c>: "target", "self", "circle", "cone" or "line".</param>
/// <param name="Heals">True when any effect op is <c>"heal"</c> (script-target.ts:61 isHealAbility).</param>
public sealed record ScriptAbility(string Id, long Range, string ShapeKind, bool Heals)
{
    /// <summary>The ability's cast time, ticks (<c>def.castTicks</c>); read only for a <see cref="TelegraphSpec.SpansCast"/> telegraph.</summary>
    public int CastTicks { get; init; }

    /// <summary>
    /// The full definition, for <see cref="Casting.StartError"/>: a <see cref="TelegraphSpec.SpansCast"/> telegraph is
    /// drawn only when its cast would start. Null = unknown (the check is skipped).
    /// </summary>
    public AbilityDef? Def { get; init; }
}

/// <summary>One enemy a pull spawns (sim/encounter/pulls.ts:17 PullSpawn).</summary>
/// <param name="ActorId">Actor id.</param>
/// <param name="ClassId">Enemy definition id.</param>
/// <param name="Pos">Spawn point, mm.</param>
/// <param name="Yaw">Spawn facing, 1/1024 turn.</param>
/// <param name="Health">Spawn (and max) health.</param>
public sealed record PullSpawn(int ActorId, string ClassId, Vec3 Pos, int Yaw, long Health)
{
    /// <summary>Copied onto ActorState only when present.</summary>
    public long? SpeedMmPerTick { get; init; }

    /// <summary>Copied onto ActorState only when present.</summary>
    public long? ChaseMarginMm { get; init; }
}

/// <summary>One pull (sim/encounter/pulls.ts:28 PullDef).</summary>
/// <param name="Id">Pull id.</param>
/// <param name="Spawns">Spawns in order.</param>
public sealed record PullDef(string Id, IReadOnlyList<PullSpawn> Spawns)
{
    /// <summary>
    /// PRESPAWN-AGGRO (C#-only; BIBLE section 9.1): the aggro radius, mm. On a layout run the pull stands idle from
    /// tick 0 and engages on the first tick a living party member is within this distance of <see cref="Pulls.RingCenter"/>.
    /// </summary>
    public long AggroMm { get; init; } = Pulls.DefaultAggroMm;

    /// <summary>The aggro ring's centre, mm; null = the integer mean of the spawn points.</summary>
    public Vec3? AggroCenter { get; init; }

    /// <summary>Group ids (pulls or packs) that engage with this one, a chain pull (BIBLE section 9.1, links).</summary>
    public IReadOnlyList<string> Links { get; init; } = System.Array.Empty<string>();
}

/// <summary>A dungeon as the encounter sees it (sim/encounter/pulls.ts:33 DungeonDef).</summary>
/// <param name="Id">Dungeon id.</param>
/// <param name="Pulls">Ordered pulls.</param>
public sealed record DungeonDef(string Id, IReadOnlyList<PullDef> Pulls)
{
    /// <summary>
    /// MPLUS-PATROLS (C#-only, layout runs; BIBLE section 9.1 "it does not move unless it is a patrol"): packs that walk
    /// a fixed waypoint loop while idle. A patrol is not a pull: it never moves the pull index and the run never waits on
    /// it, but its members bank forces like any pack. Empty for every oracle run.
    /// </summary>
    public IReadOnlyList<PatrolDef> Patrols { get; init; } = System.Array.Empty<PatrolDef>();
}

/// <summary>
/// One patrol pack (MPLUS-PATROLS; game/content/data/layout.json <c>patrols</c>). While idle its point walks the closed
/// loop <paramref name="Waypoints"/>[0] -> [1] -> ... -> [0] at <paramref name="SpeedMmPerTick"/> from tick 0
/// (<see cref="Patrols.PointAt"/>), and each member stands at that point plus its spawn offset. Every leg is
/// axis-aligned, so the walk is exact integer mm.
/// </summary>
/// <param name="Id">Group id (shares the pull and pack namespace: <c>pack_engage</c> names it).</param>
/// <param name="Spawns">Members; each <see cref="PullSpawn.Pos"/> is the member's offset from the patrol point.</param>
/// <param name="Waypoints">The loop's corners, at least two (two = out and back).</param>
/// <param name="SpeedMmPerTick">Walk speed while idle, mm per tick (positive).</param>
public sealed record PatrolDef(string Id, IReadOnlyList<PullSpawn> Spawns, IReadOnlyList<Vec3> Waypoints, long SpeedMmPerTick)
{
    /// <summary>Aggro radius, mm: the same rule and default as a prespawned idle pack (<see cref="Pulls.DefaultAggroMm"/>).</summary>
    public long AggroMm { get; init; } = Pulls.DefaultAggroMm;
}
