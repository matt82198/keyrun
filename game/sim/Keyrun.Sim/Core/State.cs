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
/// The cast in flight on an actor (<see cref="ActorState.Cast"/>; R2 of the C# architecture review). One value, so a
/// cast is started whole and ended whole (<c>Combat.Casting.End</c>), never half-cleared.
/// </summary>
/// <param name="AbilityId">The ability being cast.</param>
/// <param name="StartTick">Tick the cast started.</param>
/// <param name="EndsTick">Tick the cast completes.</param>
/// <param name="IsOverheat">The cast is the Cinder's free Overheat (BIBLE section 5). False on every oracle run.</param>
/// <param name="PinnedTargetId">The pinned start target (meaningful only while <paramref name="TargetPinned"/>).</param>
/// <param name="TargetPinned">
/// CAST-KEEPS-START-TARGET (f-bd8fbbb1): a mid-cast retarget pinned the target the cast started on. Separate from
/// <paramref name="PinnedTargetId"/> because a cast begun with no target can pin "no target".
/// </param>
/// <param name="AimOrigin">A cone cast's origin at cast start (the telegraph's origin); null for every other shape.</param>
/// <param name="AimYaw">A cone cast's yaw at cast start (the telegraph's yaw); null for every other shape.</param>
public readonly record struct CastInFlight(string AbilityId, int StartTick, int EndsTick, bool IsOverheat, int? PinnedTargetId, bool TargetPinned, Vec3? AimOrigin = null, int? AimYaw = null);

/// <summary>
/// A cone or line telegraph's drawn pose as it lands (<see cref="SimState.LandedAims"/>): the cast it warns of, and the
/// origin and yaw it resolved on, after any Tide drift.
/// </summary>
/// <param name="Cast">The ability the telegraph warns of.</param>
/// <param name="Origin">The telegraph origin on its resolve tick.</param>
/// <param name="Yaw">The telegraph yaw (copied from the caster at draw time).</param>
public readonly record struct LandedAim(string Cast, Vec3 Origin, int Yaw);

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

    private CastInFlight? _cast;

    // Only the legacy set shims below write this: a cast they leave incomplete (a test that sets CastId without its
    // ticks, or ticks without a CastId). Production writes Cast whole (Casting.StartCast) and clears it only through
    // Casting.End, so every production actor has an empty one.
    private LegacyCast _partial;

    /// <summary>
    /// The cast in flight, or null when idle (R2: one value, so a cast cannot be half-cleared). Written whole by
    /// <see cref="Combat.Casting.StartCast"/>, pinned by <see cref="Combat.Casting.SetTarget"/>, and cleared only by
    /// <see cref="Combat.Casting.End"/>, which returns the ended cast. Never projected into a trace (the projection reads
    /// the <see cref="CastId"/> and <see cref="CastEndsTick"/> shims).
    /// </summary>
    public CastInFlight? Cast
    {
        get => _cast;
        set
        {
            _cast = value;
            _partial = default;
        }
    }

    /// <summary>Ability being cast, or null. Forwarding shim over <see cref="Cast"/> (setter kept for one release).</summary>
    public string? CastId
    {
        get => View.Id;
        set => Write(View with { Id = value });
    }

    /// <summary>Tick the current cast completes, or null. Forwarding shim over <see cref="Cast"/> (setter kept for one release).</summary>
    public int? CastEndsTick
    {
        get => View.Ends;
        set => Write(View with { Ends = value });
    }

    /// <summary>Tick the current cast started, or null. Forwarding shim over <see cref="Cast"/> (setter kept for one release).</summary>
    public int? CastStartTick
    {
        get => View.Start;
        set => Write(View with { Start = value });
    }

    /// <summary>
    /// The current cast is the Cinder's free Overheat (BIBLE section 5; Combat/Casting.cs OverheatRule). C#-only: false
    /// on every oracle run (no Overheat rule), and cleared with the cast. Forwarding shim over <see cref="Cast"/>.
    /// </summary>
    public bool CastIsOverheat
    {
        get => View.Overheat;
        set => Write(View with { Overheat = value });
    }

    /// <summary>
    /// CAST-KEEPS-START-TARGET (finding f-bd8fbbb1; sim/core/state.ts castTargetId): true while the cast in flight has
    /// pinned the target it started on because the actor retargeted mid-cast (<see cref="Combat.Casting.SetTarget"/>).
    /// Cleared with the cast by <see cref="Combat.Casting.End"/>; the ended cast carries it to its Windows. Never
    /// projected into a trace. Forwarding shim over <see cref="Cast"/>.
    /// </summary>
    public bool CastTargetPinned
    {
        get => View.Pinned;
        set => Write(View with { Pinned = value });
    }

    /// <summary>
    /// The pinned start target of the cast in flight (meaningful only while <see cref="CastTargetPinned"/>). Forwarding
    /// shim over <see cref="Cast"/>.
    /// </summary>
    public int? CastTargetId
    {
        get => View.PinId;
        set => Write(View with { PinId = value });
    }

    private LegacyCast View => _cast is { } c ? new LegacyCast(c.AbilityId, c.StartTick, c.EndsTick, c.IsOverheat, c.TargetPinned, c.PinnedTargetId) : _partial;

    private void Write(LegacyCast v)
    {
        if (v is { Id: { } id, Start: { } start, Ends: { } ends })
        {
            _cast = new CastInFlight(id, start, ends, v.Overheat, v.PinId, v.Pinned);
            _partial = default;
        }
        else
        {
            _cast = null;
            _partial = v;
        }
    }

    // The six legacy cast fields as the shims see them; lossless both ways for a complete cast.
    private readonly record struct LegacyCast(string? Id, int? Start, int? Ends, bool Overheat, bool Pinned, int? PinId);

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

    /// <summary>
    /// HOLDER-LOCK (STATE.md D30): the player an engaged enemy is locked onto, or null (not engaged, a player, or no
    /// living player). Set on engage to the nearest living player (Combat/Holder.cs), moved only by a taunt, the
    /// holder's death (then the nearest living player) or a wipe. Not projected (ORACLE.md section 1).
    /// </summary>
    public int? HolderId { get; set; }

    /// <summary>Per-actor walk speed (mm/tick) for move commands; null = RulesConfig.baseSpeedMmPerTick.</summary>
    public long? SpeedMmPerTick { get; set; }

    /// <summary>Per-actor collision cylinder radius (mm); null = the run default (MovementConfig.ActorRadiusMm). A pair separates to the sum of the two radii (R-BOSSROOM-1 b).</summary>
    public long? RadiusMm { get; set; }

    /// <summary>Per-actor chase margin override; null = encounter/chase.ts CHASE_MARGIN_MM.</summary>
    public long? ChaseMarginMm { get; set; }

    /// <summary>
    /// PRESPAWN-AGGRO (C#-only; BIBLE section 9.1): true while the actor's group stands idle in formation, before it
    /// engages. An idle enemy runs no script, casts nothing, never chases and is never auto-targeted; it can still be
    /// hit, and taking damage engages its whole group. False for every actor of an oracle run.
    /// </summary>
    public bool Idle { get; set; }

    /// <summary>
    /// Retired by R-SEED-2 (2026-10-22): nothing sets this any more (the KF3 room-activation gate is removed). It stays only
    /// because game/bots HostEnemyAggroTests reads it; always false. Never read it as a gate in new code.
    /// </summary>
    public bool Dormant { get; set; }

    /// <summary>
    /// AGGRO-CHASE (C#-only, layout runs; f-f0c886eb, WoW Mythic+): this enemy's own aggro radius, mm. An idle enemy
    /// engages its whole group on the first tick a living seated player stands within this distance of the enemy itself
    /// (inclusive, XZ). Null = only the group's ring (<c>Encounter.Aggro</c>). Null on every oracle run.
    /// </summary>
    public long? AggroMm { get; set; }

    /// <summary>
    /// AGGRO-CHASE: this enemy's assist radius, mm. An idle enemy engages its whole group on the first tick an engaged,
    /// living, non-evading enemy stands within this distance of it (a pack that a chase or a fight comes near joins it,
    /// the WoW body pull). Null = never assists. Null on every oracle run.
    /// </summary>
    public long? AssistMm { get; set; }

    /// <summary>
    /// AGGRO-CHASE: the leash distance, mm. An engaged enemy pulled farther than this from <see cref="Home"/> makes its
    /// group evade: every living member walks home, then resets at full health, idle again. Null = never leashes (the boss,
    /// patrols). Null on every oracle run.
    /// </summary>
    public long? LeashMm { get; set; }

    /// <summary>AGGRO-CHASE: where an idle enemy stands (kept current while it is idle), the point it leashes back to.</summary>
    public Vec3? Home { get; set; }

    /// <summary>
    /// IDLE-WANDER (C#-only, layout runs with <c>RunContent.IdleWander</c>; f-c1044aa4): where this idle enemy stands at rest
    /// (its spot in its pack when the wander began). Its personal sight and assist radii are measured from here, never from
    /// where it wandered to, so wandering wakes no pack earlier than standing still. Null = not wandering (every oracle run).
    /// </summary>
    public Vec3? WanderAnchor { get; set; }

    /// <summary>IDLE-WANDER: the tick the wander began (the loop is read at the ticks since). Meaningful only with <see cref="WanderAnchor"/>.</summary>
    public int WanderEpoch { get; set; }

    /// <summary>
    /// AGGRO-CHASE: the cause of the evade this enemy is walking home from (<c>"leash"</c> or <c>"no-living-seat"</c>), so the
    /// reset event carries the same cause as the evade. Null = not evading (every oracle run).
    /// </summary>
    public string? EvadeCause { get; set; }

    /// <summary>IDLE-WANDER: the pack's closed loop of offsets (an immutable value shared by the pack's members), or null.</summary>
    public Encounter.IdleWander.Loop? WanderLoop { get; set; }

    /// <summary>
    /// AGGRO-CHASE: true while the actor walks back to <see cref="Home"/> after its group leashed. An evading enemy is idle
    /// (no script, no chase), and nothing engages its group (no ring, damage or assist) until every member is home.
    /// </summary>
    public bool Evading { get; set; }

    /// <summary>
    /// G2.5 (f-1c94a912): true from the tick a leashed group resets until this member stands on its patrol loop again. While
    /// it is set, <see cref="Encounter.Patrols"/> walks the member at its speed instead of placing it on the loop.
    /// </summary>
    public bool ResetWalk { get; set; }

    /// <summary>
    /// The wearer's summed gear damageDoneBp (BIBLE s7 "Tier is power"; Runs.GearPower). C#-only: 0 on every oracle run.
    /// </summary>
    public long GearDamageDoneBp { get; set; }

    /// <summary>The wearer's summed gear damageTakenBp (negative reduces damage taken). C#-only: 0 on every oracle run.</summary>
    public long GearDamageTakenBp { get; set; }

    /// <summary>
    /// The class's passive armor, bp of every incoming hit removed after all bp modifiers (design/BIBLE.md
    /// section 3.1; Combat.Damage.ApplyArmor). Multiplicative, so it never stacks to immunity with Brace, Vigil or
    /// Bulwark. C#-only: 0 on every oracle run.
    /// </summary>
    public long ArmorBp { get; set; }

    /// <summary>
    /// The class's threat multiplier, bp (design/THREAT-MODEL.md section 7.2: Warden 20000, Cinder and Chanter 10000).
    /// Scales every threat this actor generates. 10000 (100%) unless the party spec carries another class number.
    /// </summary>
    public long ThreatBp { get; set; } = 10000;

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

    /// <summary>
    /// The drawn pose of each cone or line telegraph that resolved on this tick, by caster id (C#-only; empty on every
    /// oracle run). The encounter step writes it from the registry telegraph's origin and yaw after Tide drift, so a cast
    /// that lands on this tick resolves on the pose its telegraph showed, not the caster's live pose (Targeting.AimOf).
    /// Replaced whole each tick, never mutated in place, so a shallow <see cref="Clone"/> is enough.
    /// </summary>
    public IReadOnlyDictionary<int, LandedAim> LandedAims { get; set; } = new Dictionary<int, LandedAim>();

    /// <summary>
    /// The reward seed the run-end completion rolls used (the host's <c>StepRunOpts.RewardSeedOverride</c>, else
    /// <c>EndOfRun.RewardSeedOf</c>). Null until the run ends; set by the run-end phase (R-K4-16 (b)).
    /// </summary>
    public long? RewardSeed { get; set; }

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
