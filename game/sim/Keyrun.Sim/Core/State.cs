using System;
using System.Collections.Generic;
using System.Linq;

namespace Keyrun.Sim.Core;

/// <summary>Actor faction (sim/core/state.ts ActorState.kind).</summary>
public enum ActorKind
{
    /// <summary>"player": a party member (human or bot).</summary>
    Player,

    /// <summary>"enemy".</summary>
    Enemy,
}

/// <summary>Actor role tag (sim/core/state.ts ActorState.role).</summary>
public enum ActorRole
{
    /// <summary>"tank".</summary>
    Tank,

    /// <summary>"healer".</summary>
    Healer,

    /// <summary>"damage".</summary>
    Damage,

    /// <summary>"none" (enemies).</summary>
    None,
}

/// <summary>The JS string spellings of <see cref="ActorKind"/> and <see cref="ActorRole"/> (event payloads, content).</summary>
public static class ActorTags
{
    /// <summary>"player" or "enemy".</summary>
    public static string ToJs(this ActorKind kind) => kind switch
    {
        ActorKind.Player => "player",
        ActorKind.Enemy => "enemy",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    /// <summary>"tank", "healer", "damage" or "none".</summary>
    public static string ToJs(this ActorRole role) => role switch
    {
        ActorRole.Tank => "tank",
        ActorRole.Healer => "healer",
        ActorRole.Damage => "damage",
        ActorRole.None => "none",
        _ => throw new ArgumentOutOfRangeException(nameof(role)),
    };

    /// <summary>Parses "player" / "enemy"; throws on anything else.</summary>
    public static ActorKind ParseKind(string s) => s switch
    {
        "player" => ActorKind.Player,
        "enemy" => ActorKind.Enemy,
        _ => throw new ArgumentException($"unknown actor kind {s}", nameof(s)),
    };

    /// <summary>Parses "tank" / "healer" / "damage" / "none"; throws on anything else.</summary>
    public static ActorRole ParseRole(string s) => s switch
    {
        "tank" => ActorRole.Tank,
        "healer" => ActorRole.Healer,
        "damage" => ActorRole.Damage,
        "none" => ActorRole.None,
        _ => throw new ArgumentException($"unknown actor role {s}", nameof(s)),
    };
}

/// <summary>A buff or debuff on an actor (sim/core/state.ts EffectInstance). Mutable; cloned with its actor.</summary>
public sealed class EffectInstance
{
    /// <summary>Effect definition id.</summary>
    public string EffectId { get; set; } = string.Empty;

    /// <summary>Actor that applied it.</summary>
    public int SourceId { get; set; }

    /// <summary>Stack count.</summary>
    public long Stacks { get; set; }

    /// <summary>Tick at which it ends.</summary>
    public int EndsTick { get; set; }

    /// <summary>Next periodic tick, or null for a non-periodic effect.</summary>
    public int? NextPeriodicTick { get; set; }

    /// <summary>A field-for-field copy.</summary>
    public EffectInstance Clone() => (EffectInstance)MemberwiseClone();
}

/// <summary>
/// A floor-patch Window (sim/core/state.ts FloorWindow, e.g. warden.bulwark), keyed by windowId in
/// <see cref="SimState.FloorWindows"/> so it survives the per-step rebuild of the rules.
/// </summary>
public sealed class FloorWindow
{
    /// <summary>Window id.</summary>
    public string WindowId { get; set; } = string.Empty;

    /// <summary>Class that set it up.</summary>
    public string SetupClass { get; set; } = string.Empty;

    /// <summary>Actor that set it up.</summary>
    public int SetupActorId { get; set; }

    /// <summary>Patch centre, mm.</summary>
    public Vec3 Pos { get; set; }

    /// <summary>Tick at which it ends.</summary>
    public int EndsTick { get; set; }

    /// <summary>Patch radius, mm.</summary>
    public long RadiusMm { get; set; }

    /// <summary>A field-for-field copy.</summary>
    public FloorWindow Clone() => (FloorWindow)MemberwiseClone();
}

/// <summary>
/// A combo's floor cloud (BIBLE section 4 Steam: "a cloud that blinds enemies inside"), opened by the C#-only
/// <c>cloud</c> combo op. While it lasts, every living actor of another faction than <see cref="SourceKind"/> inside
/// the circle carries <see cref="EffectId"/> until <see cref="EndsTick"/>. NEW behaviour with no TS counterpart: no
/// oracle run opens one.
/// </summary>
public sealed class FloorCloud
{
    /// <summary>Unique id: effectId, source actor id and opening tick.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>The effect it applies, e.g. "blinded".</summary>
    public string EffectId { get; set; } = string.Empty;

    /// <summary>The Payoff actor that opened it (the effect's source).</summary>
    public int SourceId { get; set; }

    /// <summary>The opener's faction; actors of any other faction are the cloud's enemies.</summary>
    public ActorKind SourceKind { get; set; }

    /// <summary>Cloud centre, mm.</summary>
    public Vec3 Pos { get; set; }

    /// <summary>Cloud radius, mm.</summary>
    public long RadiusMm { get; set; }

    /// <summary>Tick at which it ends.</summary>
    public int EndsTick { get; set; }

    /// <summary>A field-for-field copy.</summary>
    public FloorCloud Clone() => (FloorCloud)MemberwiseClone();
}

/// <summary>
/// A press held in the spell queue window (sim/core/state.ts QueuedPress): the cast command's ability, target and
/// point, and the tick the key went down. Immutable, so an actor clone may share it.
/// </summary>
/// <param name="AbilityId">The pressed ability.</param>
/// <param name="TargetId">The press's target, or null.</param>
/// <param name="Point">The press's ground point, or null.</param>
/// <param name="PressTick">The tick of the press.</param>
public sealed record QueuedPress(string AbilityId, int? TargetId, Vec3? Point, int PressTick);

/// <summary>
/// One actor (sim/core/state.ts ActorState). Mutable: rules mutate the clone that <see cref="Tick.Step"/>
/// hands them, exactly as the TS mutates its structuredClone.
/// </summary>
public sealed class ActorState
{
    /// <summary>Actor id; the actor map iterates ascending by it.</summary>
    public int Id { get; set; }

    /// <summary>Faction.</summary>
    public ActorKind Kind { get; set; }

    /// <summary>Class or enemy id.</summary>
    public string ClassId { get; set; } = string.Empty;

    /// <summary>Role tag.</summary>
    public ActorRole Role { get; set; } = ActorRole.None;

    /// <summary>Position, mm.</summary>
    public Vec3 Pos { get; set; }

    /// <summary>Facing, 1/1024 turn (0..1023 when set from a command; stored as given).</summary>
    public int Yaw { get; set; }

    /// <summary>Current health.</summary>
    public long Health { get; set; }

    /// <summary>Maximum health.</summary>
    public long MaxHealth { get; set; }

    /// <summary>Current resource.</summary>
    public long Resource { get; set; }

    /// <summary>Maximum resource.</summary>
    public long MaxResource { get; set; }

    /// <summary>Alive flag (TS defaults it to true at creation).</summary>
    public bool Alive { get; set; } = true;

    /// <summary>Current target, or null.</summary>
    public int? TargetId { get; set; }

    /// <summary>Ability being cast, or null.</summary>
    public string? CastId { get; set; }

    /// <summary>Tick the current cast completes, or null.</summary>
    public int? CastEndsTick { get; set; }

    /// <summary>Tick the current cast started, or null.</summary>
    public int? CastStartTick { get; set; }

    /// <summary>
    /// The current cast is the Cinder's free Overheat (BIBLE section 5; Combat/Casting.cs OverheatRule). C#-only: false
    /// on every oracle run (no Overheat rule), and cleared with the cast.
    /// </summary>
    public bool CastIsOverheat { get; set; }

    /// <summary>
    /// The one press waiting in the spell queue window (Combat/SpellQueue.cs, sim/combat/spell-queue.ts), or null.
    /// Players only; never projected into an oracle trace.
    /// </summary>
    public QueuedPress? QueuedCast { get; set; }

    /// <summary>Ability id to ready tick, in JS key order.</summary>
    public JsRecord<int> Cooldowns { get; set; } = new();

    /// <summary>Tick the global cooldown is ready.</summary>
    public int GcdReadyTick { get; set; }

    /// <summary>Active effects, in application order.</summary>
    public List<EffectInstance> Effects { get; set; } = new();

    /// <summary>Threat by actor id (JS integer keys iterate ascending, hence sorted).</summary>
    public SortedDictionary<int, long> Threat { get; set; } = new();

    /// <summary>Per-actor walk speed (mm/tick) for move commands; null = RulesConfig.baseSpeedMmPerTick.</summary>
    public long? SpeedMmPerTick { get; set; }

    /// <summary>Per-actor chase margin override; null = encounter/chase.ts CHASE_MARGIN_MM.</summary>
    public long? ChaseMarginMm { get; set; }

    /// <summary>
    /// PRESPAWN-AGGRO (C#-only; BIBLE section 9.1): true while the actor's group stands idle in formation, before it
    /// engages. An idle enemy runs no script, casts nothing, never chases and is never auto-targeted; it can still be
    /// hit, and taking damage engages its whole group. False for every actor of an oracle run.
    /// </summary>
    public bool Idle { get; set; }

    /// <summary>
    /// The wearer's summed gear damageDoneBp (BIBLE s7 "Tier is power"; Runs.GearPower). C#-only: 0 on every oracle run.
    /// </summary>
    public long GearDamageDoneBp { get; set; }

    /// <summary>The wearer's summed gear damageTakenBp (negative reduces damage taken). C#-only: 0 on every oracle run.</summary>
    public long GearDamageTakenBp { get; set; }

    /// <summary>A deep copy (effects, cooldowns and threat are copied, not shared).</summary>
    public ActorState Clone()
    {
        var copy = (ActorState)MemberwiseClone();
        copy.Cooldowns = Cooldowns.Clone(v => v);
        copy.Effects = Effects.Select(e => e.Clone()).ToList();
        copy.Threat = new SortedDictionary<int, long>(Threat);
        return copy;
    }
}

/// <summary>
/// The whole sim state (sim/core/state.ts SimState). Actors live in a map ordered ascending by id, which is
/// the order JS gives <c>Object.keys</c> on the TS <c>Record&lt;ActorId, ActorState&gt;</c> (ids are positive
/// integers). The TS <c>log: never[]</c> field is always empty and is not carried. The TS <c>hashState</c>
/// is not reproduced (ORACLE.md section 2); parity uses <see cref="Projection"/>.
/// </summary>
public sealed class SimState
{
    /// <summary>Current tick.</summary>
    public int Tick { get; set; }

    /// <summary>The run seed as given (the PRNG is seeded from its ToUint32).</summary>
    public long Seed { get; set; }

    /// <summary>The in-state PRNG.</summary>
    public PrngState Prng { get; set; }

    /// <summary>Actors by id, ascending.</summary>
    public SortedDictionary<int, ActorState> Actors { get; set; } = new();

    /// <summary>Ticks elapsed on the run timer.</summary>
    public int RunTimerTicks { get; set; }

    /// <summary>Phase tag.</summary>
    public string Phase { get; set; } = "init";

    /// <summary>
    /// Floor-patch Windows by windowId, in JS key order. Always present here (the TS field is optional only
    /// for hand-built test fixtures; createState always populates it).
    /// </summary>
    public JsRecord<FloorWindow> FloorWindows { get; set; } = new();

    /// <summary>Open combo clouds in opening order (C#-only; empty on every oracle run).</summary>
    public List<FloorCloud> FloorClouds { get; set; } = new();

    /// <summary>sim/core/state.ts createState: tick 0, seeded PRNG, no actors, phase "init".</summary>
    public static SimState Create(long seed) => new()
    {
        Tick = 0,
        Seed = seed,
        Prng = Core.Prng.SeedState(seed),
        RunTimerTicks = 0,
        Phase = "init",
    };

    /// <summary>sim/core/state.ts cloneState: a deep, independent copy.</summary>
    public SimState Clone()
    {
        var copy = (SimState)MemberwiseClone();
        copy.Actors = new SortedDictionary<int, ActorState>();
        foreach (var kv in Actors)
        {
            copy.Actors.Add(kv.Key, kv.Value.Clone());
        }
        copy.FloorWindows = FloorWindows.Clone(w => w.Clone());
        copy.FloorClouds = FloorClouds.Select(c => c.Clone()).ToList();
        return copy;
    }
}
