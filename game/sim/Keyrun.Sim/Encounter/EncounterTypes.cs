using System.Collections.Generic;
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
public sealed record ScriptAbility(string Id, long Range, string ShapeKind, bool Heals);

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
public sealed record PullDef(string Id, IReadOnlyList<PullSpawn> Spawns);

/// <summary>A dungeon as the encounter sees it (sim/encounter/pulls.ts:33 DungeonDef).</summary>
/// <param name="Id">Dungeon id.</param>
/// <param name="Pulls">Ordered pulls.</param>
public sealed record DungeonDef(string Id, IReadOnlyList<PullDef> Pulls);
