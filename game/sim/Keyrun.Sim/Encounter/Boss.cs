using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Encounter;

/// <summary>One Sundered Twin the armed boss brings (BIBLE s9 mechanic 2).</summary>
/// <param name="ActorId">Actor id the twin spawns with (must be free when the boss pull goes live).</param>
/// <param name="ClassId">Enemy definition id (e.g. <c>hollowWarden.twinA</c>).</param>
/// <param name="MaxHealth">Spawn and max health (the caller applies any Tempo health scaling).</param>
/// <param name="Offset">Spawn point relative to the boss, mm.</param>
/// <param name="Script">The twin's enemy script.</param>
public sealed record BossTwinSpec(int ActorId, string ClassId, long MaxHealth, Vec3 Offset, IReadOnlyList<ScriptStep> Script);

/// <summary>
/// The armed boss encounter's rules (lane 3F; BIBLE s9 "The Sundered Twins" and "The Lock"). Content-specific values
/// (ids, health, the bar) are required; the BIBLE s9 structure (50%, 3 s, every 30 s, three fails) is the default.
/// </summary>
/// <param name="BossActorId">The boss actor (the host's boss pull, 900 for hollow-vault).</param>
/// <param name="Twins">The Twins, in spawn order.</param>
/// <param name="TwinsBar">The Twins' one shared Break bar; its actor ids must be the twins' ids.</param>
public sealed record BossConfig(int BossActorId, IReadOnlyList<BossTwinSpec> Twins, BreakBarSource TwinsBar)
{
    /// <summary>The Lock opens the first tick boss health is at or below this share of max, basis points (50%).</summary>
    public int LockThresholdBp { get; init; } = 5000;

    /// <summary>Lock Window length, ticks (3 s; content hollowWarden.lock <c>durationTicks</c>).</summary>
    public int LockWindowTicks { get; init; } = 60;

    /// <summary>Ticks between Lock openings once the first has opened (30 s).</summary>
    public int LockRepeatTicks { get; init; } = 600;

    /// <summary>The effect the boss carries while Locked (content <c>lockShield</c>, damageTakenBp -10000).</summary>
    public string ShieldEffectId { get; init; } = "lockShield";

    /// <summary>Unbroken Locks before the boss enrages (BIBLE s9 "Fail three Locks").</summary>
    public int FailsToEnrage { get; init; } = 3;

    /// <summary>Enrage Tempo spike: the boss's loop cadence is multiplied by this, basis points (15000 = 1.5x as often).</summary>
    public int EnrageTempoBp { get; init; } = 15000;

    /// <summary>
    /// Boss script casts the armed rules replace (the content's flat <c>every: 600</c> lock loop, which ignores the 50%
    /// gate). The Lock is opened by <see cref="BossRules"/> instead.
    /// </summary>
    public IReadOnlyList<string> SuppressedBossCasts { get; init; } = new[] { "hollowWarden.lock" };

    /// <summary>
    /// LOCK-TELEGRAPH (BIBLE s3 "Cast bars are the enemy's language", f-dfafcec6): true when each Lock begins with the
    /// boss casting its Lock ability (the first of <see cref="SuppressedBossCasts"/>) for that ability's castTicks, with
    /// a cast bar and its script step's floor telegraph, the Window opening on the tick the cast lands. False (the
    /// default, and every synthetic test config) opens the Window on the tick the Lock is due, with no cast.
    /// </summary>
    public bool CastLock { get; init; }
}

/// <summary>One open Lock Window on the boss.</summary>
/// <param name="Index">1-based Lock number.</param>
/// <param name="OpenTick">Tick it opened.</param>
/// <param name="EndsTick">First tick it is no longer open (<c>OpenTick + LockWindowTicks</c>).</param>
public sealed record LockWindow(int Index, int OpenTick, int EndsTick)
{
    /// <summary>The party actor whose Setup tagged the boss inside this Window, or null.</summary>
    public int? SetupActorId { get; init; }
}

/// <summary>The armed boss encounter's durable state (in <see cref="EncounterState.Boss"/>; immutable, stepped via <c>with</c>).</summary>
/// <param name="Config">The rules.</param>
public sealed record BossState(BossConfig Config)
{
    /// <summary>True once the Twins have spawned (they spawn once).</summary>
    public bool TwinsSpawned { get; init; }

    /// <summary>The open Lock, or null.</summary>
    public LockWindow? Active { get; init; }

    /// <summary>Tick the next Lock opens; null until the 50% gate first opens one.</summary>
    public int? NextLockTick { get; init; }

    /// <summary>Locks opened so far.</summary>
    public int LocksOpened { get; init; }

    /// <summary>Locks the party broke.</summary>
    public int LocksBroken { get; init; }

    /// <summary>Locks that closed unbroken.</summary>
    public int LockFails { get; init; }

    /// <summary>True once the boss has enraged (once per run).</summary>
    public bool Enraged { get; init; }

    /// <summary>A broken Lock's shield is dropped on the next encounter step.</summary>
    public bool ShieldDropPending { get; init; }

    /// <summary>The Lock the boss is casting (<see cref="BossConfig.CastLock"/>), or null.</summary>
    public LockCast? Casting { get; init; }
}

/// <summary>
/// LOCK-TELEGRAPH: a Lock the boss is casting (<see cref="BossConfig.CastLock"/>). Its cast bar and floor circle start on
/// <paramref name="StartTick"/>; on <paramref name="LandsTick"/> the cast lands, the circle resolves and the Window opens.
/// </summary>
/// <param name="Index">1-based number of the Lock it opens.</param>
/// <param name="StartTick">Tick the cast starts.</param>
/// <param name="LandsTick">Tick the cast lands and the Window opens (<c>StartTick</c> plus the Lock ability's castTicks).</param>
public sealed record LockCast(int Index, int StartTick, int LandsTick);

/// <summary>The armed boss's pre-combat output for one tick.</summary>
/// <param name="Boss">The next boss state.</param>
/// <param name="Scripts">The scripts to run this tick (Twins added, suppressed casts removed, enrage applied).</param>
/// <param name="TwinsBar">The Twins' bar source to drain this tick.</param>
/// <param name="EffectPatches">Shield on/off patches for the run root to apply after combat.</param>
public sealed record BossPreStep(BossState Boss, IReadOnlyDictionary<int, IReadOnlyList<ScriptStep>> Scripts, BreakBarSource TwinsBar, IReadOnlyList<BreakBarEffectPatch> EffectPatches);

/// <summary>The armed boss's post-combat output for one tick.</summary>
/// <param name="Boss">The next boss state.</param>
/// <param name="Spawned">Twins realised this tick, and Twins that fell with the boss this tick (Health 0, not Alive),
/// for the run root to merge into state (a merge replaces an actor by id).</param>
public sealed record BossPostStep(BossState Boss, IReadOnlyList<ActorState> Spawned);

/// <summary>
/// The Hollow Warden's boss rules (lane 3F; BIBLE s9; ENGINE-PLAN 3F). NEW behaviour with no TS oracle: a run gets it
/// only through <see cref="Arm"/>, so an unarmed run (every oracle trace) is unchanged. The run root reaches it through
/// the two encounter seams it already calls each tick: <see cref="EncounterStep.Step"/> (pre-combat:
/// <see cref="PreCombat"/>) and <see cref="EchoSpawns.Step"/> (post-combat: <see cref="AfterCombat"/>).
/// <list type="bullet">
/// <item>The Sundered Twins spawn once, flanking the boss, the step its pull goes live, and share one Break bar: the
/// existing <see cref="BreakBar"/> drain fed by interrupt- and control-tagged casts on either twin; at empty both are
/// stunned (the bar's onEmpty).</item>
/// <item>The Lock: the first tick boss health is at or below 50% the boss shields and opens a 3 s Window on itself,
/// then again every 30 s while it lives. A party Setup on the boss inside the Window, then a Payoff on it from a
/// different class (the <c>combo</c> event closing that Setup's Window), breaks it and drops the shield. A Window that
/// ends unbroken is a failed Lock. Each failed Lock adds one Tempo stack to the boss at once (LOCK-STAKES, ruling
/// R-LOCK-1: its loops re-pace to <see cref="LockTempoBp"/>, and <c>lock_failed</c> carries the new tempo); the third
/// failed Lock enrages the boss into the full Tempo spike (its script loops fire <see cref="BossConfig.EnrageTempoBp"/>
/// as often), never an instant wipe.</item>
/// <item>LOCK-TELEGRAPH (<see cref="BossConfig.CastLock"/>, BIBLE s3 "Cast bars are the enemy's language"): a due Lock
/// begins as a cast. On the first due tick the boss is not mid-cast and has no telegraph of its own pending, it casts
/// its Lock ability (a <c>lock_cast</c> event; a real cast bar for the ability's castTicks, its other script steps held
/// for the cast) with its Lock script step's floor telegraph spanning the cast; on the tick the cast lands the circle
/// resolves and the Window opens. The cast's own content effects never land (<see cref="CombatAbilities"/>): these
/// rules own the shield and the Window.</item>
/// <item>The boss's death ends the encounter: every Twin still standing dies on the boss's death tick (a <c>death</c>
/// event with <c>reason: "boss_death"</c>), as WoW despawns a boss's adds when the boss dies.</item>
/// </list>
/// Events: <c>boss_add_spawn</c>, <c>death</c> (a Twin falling with the boss), <c>lock_cast</c>, <c>lock_open</c>, <c>lock_broken</c>, <c>lock_failed</c>, <c>boss_enrage</c>.
/// </summary>
public static class BossRules
{
    /// <summary>
    /// R-MULTIBOSS-1: arms <paramref name="encounter"/> with the boss rules of a multi-segment dungeon's bosses, in fight
    /// order. The first boss is armed now; each later boss is queued (<see cref="EncounterState.NextBosses"/>) and armed in
    /// its place the step the one before it dies (<see cref="Advance"/>), so a boss's rules (its adds, Break bar and Lock)
    /// run from its own pull's engage to its own death. A one-boss list is exactly <see cref="Arm"/>. A longer list also
    /// makes every boss death emit <c>boss_death</c> (<see cref="EncounterState.BossesArmed"/>).
    /// </summary>
    /// <param name="encounter">The encounter.</param>
    /// <param name="configs">One config per boss, in the order the dungeon's boss pulls are fought.</param>
    /// <returns>The armed encounter.</returns>
    /// <exception cref="ArgumentException">An empty list, an invalid config (<see cref="Arm"/>), or an actor id (a boss or a
    /// twin) used by two bosses.</exception>
    public static EncounterState ArmAll(EncounterState encounter, IReadOnlyList<BossConfig> configs)
    {
        ArgumentNullException.ThrowIfNull(encounter);
        ArgumentNullException.ThrowIfNull(configs);
        if (configs.Count == 0)
        {
            throw new ArgumentException("arm at least one boss", nameof(configs));
        }
        foreach (var config in configs)
        {
            Validate(config);
        }
        var ids = configs.SelectMany(c => c.Twins.Select(t => t.ActorId).Append(c.BossActorId)).ToList();
        if (ids.Distinct().Count() != ids.Count)
        {
            throw new ArgumentException("a boss or twin actor id is used by two bosses", nameof(configs));
        }
        return encounter with
        {
            Boss = new BossState(configs[0]),
            NextBosses = configs.Skip(1).ToList(),
            BossesArmed = configs.Count > 1 ? configs.Count : 0,
            BossIndex = 0,
        };
    }

    /// <summary>
    /// R-MULTIBOSS-1: the armed boss has died. When bosses are queued the next one is armed in its place (its rules run from
    /// now; its pull engages on its own aggro ring), else the encounter is returned unchanged (the last boss stays armed,
    /// and its death ends the run through the pull index).
    /// </summary>
    /// <param name="encounter">The encounter whose armed boss died this step.</param>
    /// <returns>The encounter with the next boss armed, or <paramref name="encounter"/>.</returns>
    public static EncounterState Advance(EncounterState encounter)
    {
        ArgumentNullException.ThrowIfNull(encounter);
        if (encounter.NextBosses.Count == 0)
        {
            return encounter;
        }
        return encounter with
        {
            Boss = new BossState(encounter.NextBosses[0]),
            NextBosses = encounter.NextBosses.Skip(1).ToList(),
            BossIndex = encounter.BossIndex + 1,
        };
    }

    /// <summary>Arms <paramref name="encounter"/> with the boss rules (call once, after <c>CreateRun</c>).</summary>
    /// <exception cref="ArgumentException">The bar's actors are not exactly the twins, or a twin id repeats or is the boss's.</exception>
    public static EncounterState Arm(EncounterState encounter, BossConfig config)
    {
        ArgumentNullException.ThrowIfNull(encounter);
        Validate(config);
        return encounter with { Boss = new BossState(config) };
    }

    private static void Validate(BossConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var twinIds = config.Twins.Select(t => t.ActorId).ToList();
        if (twinIds.Distinct().Count() != twinIds.Count || twinIds.Contains(config.BossActorId))
        {
            throw new ArgumentException("twin actor ids must be distinct and differ from the boss id", nameof(config));
        }
        if (!config.TwinsBar.ActorIds.OrderBy(i => i).SequenceEqual(twinIds.OrderBy(i => i)))
        {
            throw new ArgumentException("the Twins' bar must be shared by exactly the twins", nameof(config));
        }
        if (config.LockWindowTicks <= 0 || config.LockRepeatTicks <= config.LockWindowTicks || config.FailsToEnrage <= 0 || config.EnrageTempoBp <= 0)
        {
            throw new ArgumentException("lock window, repeat, fail threshold and enrage tempo must be positive, repeat longer than the window", nameof(config));
        }
    }

    /// <summary>
    /// The scripts the armed encounter runs: the boss's suppressed casts removed, the Twins' scripts added, and every
    /// boss loop (and repeating step) re-paced by the failed Locks' Tempo stacks (<see cref="LockTempoBp"/>), or by
    /// <see cref="BossConfig.EnrageTempoBp"/> once enraged.
    /// </summary>
    public static IReadOnlyDictionary<int, IReadOnlyList<ScriptStep>> EffectiveScripts(BossState boss, IReadOnlyDictionary<int, IReadOnlyList<ScriptStep>> scripts)
    {
        ArgumentNullException.ThrowIfNull(boss);
        ArgumentNullException.ThrowIfNull(scripts);
        var cfg = boss.Config;
        var result = new SortedDictionary<int, IReadOnlyList<ScriptStep>>();
        foreach (var kv in scripts)
        {
            result[kv.Key] = kv.Value;
        }
        if (result.TryGetValue(cfg.BossActorId, out var bossScript) && bossScript is not null)
        {
            var kept = bossScript.Where(s => !cfg.SuppressedBossCasts.Contains(s.Cast));
            var tempoBp = boss.Enraged ? cfg.EnrageTempoBp : LockTempoBp(cfg, boss.LockFails);
            result[cfg.BossActorId] = (tempoBp != Tempo.BpDenom ? kept.Select(s => Spike(s, tempoBp)) : kept).ToList();
        }
        foreach (var twin in cfg.Twins)
        {
            result[twin.ActorId] = twin.Script;
        }
        return result;
    }

    /// <summary>
    /// LOCK-STAKES (ruling R-LOCK-1, BIBLE s9 "The Lock"): the boss's loop tempo after <paramref name="fails"/> failed
    /// Locks, basis points. Each fail is one Tempo stack, capped at <see cref="BossConfig.FailsToEnrage"/>; stack n paces
    /// the boss at <c>10000 + trunc((EnrageTempoBp - 10000) * n / FailsToEnrage)</c>, so the last stack is exactly the
    /// enrage spike (11666, 13333, 15000 at the defaults) and the enrage stays the ceiling. 10000 with no fails.
    /// </summary>
    /// <param name="config">The armed rules.</param>
    /// <param name="fails">Locks that closed unbroken so far.</param>
    /// <returns>The effective tempo multiplier, basis points.</returns>
    public static int LockTempoBp(BossConfig config, int fails)
    {
        ArgumentNullException.ThrowIfNull(config);
        var stacks = Math.Clamp(fails, 0, config.FailsToEnrage);
        if (stacks == 0)
        {
            return Tempo.BpDenom;
        }
        var bonus = IntMath.TruncDiv((long)(config.EnrageTempoBp - Tempo.BpDenom) * stacks, config.FailsToEnrage);
        return checked((int)(Tempo.BpDenom + bonus));
    }

    /// <summary>A step's period multiplied by <paramref name="tempoBp"/>: <c>max(20, trunc(every * 10000 / tempoBp))</c>.</summary>
    private static ScriptStep Spike(ScriptStep step, int tempoBp)
    {
        if (step.Every is not { } every || every <= 0)
        {
            return step;
        }
        var scaled = IntMath.TruncDiv((long)every * Tempo.BpDenom, tempoBp);
        return step with { Every = checked((int)Math.Max(Tempo.EveryFloorTicks, scaled)) };
    }

    private static bool AtOrBelowThreshold(ActorState boss, int thresholdBp) =>
        boss.Health * Tempo.BpDenom <= boss.MaxHealth * thresholdBp;

    /// <summary>
    /// Pre-combat (inside <see cref="EncounterStep.Step"/>, on the tick's state): a pending shield drop is patched; an
    /// open Window that has run out fails (counting toward the enrage); with the boss alive and no Window open, the
    /// 50% gate (or the 30 s repeat once it has opened) opens the next Lock and shields the boss. With
    /// <see cref="BossConfig.CastLock"/> and a Lock ability with castTicks &gt; 0 the due Lock is cast first (see the
    /// class summary): the cast starts on the first due tick the boss is free, and the Window opens on the tick it lands.
    /// </summary>
    /// <param name="state">The tick's state (read only).</param>
    /// <param name="boss">The boss state.</param>
    /// <param name="scripts">The run's scripts this tick.</param>
    /// <param name="emit">Event sink.</param>
    /// <param name="abilities">Ability views (the Lock ability's castTicks); null = none, so the Lock is never cast.</param>
    /// <param name="pendingTelegraphs">The telegraph registry before this tick (a Lock cast waits for the boss's own).</param>
    public static BossPreStep PreCombat(
        SimState state,
        BossState boss,
        IReadOnlyDictionary<int, IReadOnlyList<ScriptStep>> scripts,
        Action<SimEvent> emit,
        IReadOnlyDictionary<string, ScriptAbility>? abilities = null,
        IReadOnlyList<Telegraph>? pendingTelegraphs = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(boss);
        ArgumentNullException.ThrowIfNull(scripts);
        ArgumentNullException.ThrowIfNull(emit);
        var cfg = boss.Config;
        var tick = state.Tick;
        var patches = new List<BreakBarEffectPatch>();
        var next = boss;

        if (next.ShieldDropPending)
        {
            // Duration 0 re-ends the boss's own shield instance at the patch tick; the next expiry sweep drops it.
            patches.Add(new BreakBarEffectPatch(cfg.BossActorId, cfg.ShieldEffectId, 0, 1));
            next = next with { ShieldDropPending = false };
        }

        if (next.Active is { } open && tick >= open.EndsTick)
        {
            // LOCK-STAKES (R-LOCK-1): the fail is one Tempo stack on the boss at once (EffectiveScripts reads LockFails);
            // the event carries the boss's new effective tempo so the run log shows the cost.
            var fails = next.LockFails + 1;
            emit(new SimEvent(tick, "lock_failed",
                ("actorId", JsValue.Int(cfg.BossActorId)),
                ("lockIndex", JsValue.Int(open.Index)),
                ("fails", JsValue.Int(fails)),
                ("tempoBp", JsValue.Int(LockTempoBp(cfg, fails)))));
            next = next with { Active = null, LockFails = fails };
            if (!next.Enraged && fails >= cfg.FailsToEnrage)
            {
                emit(new SimEvent(tick, "boss_enrage",
                    ("actorId", JsValue.Int(cfg.BossActorId)),
                    ("tempoBp", JsValue.Int(cfg.EnrageTempoBp))));
                next = next with { Enraged = true };
            }
        }

        var bossAlive = state.Actors.TryGetValue(cfg.BossActorId, out var bossActor) && bossActor.Alive;
        if (!bossAlive)
        {
            next = next with { Active = null, Casting = null };
        }
        else if (next.Casting is { } casting)
        {
            if (tick >= casting.LandsTick)
            {
                next = OpenLock(next, casting.Index, tick, emit, patches) with { Casting = null };
            }
        }
        else if (next.Active is null)
        {
            var due = next.NextLockTick is { } at ? tick >= at : AtOrBelowThreshold(bossActor!, cfg.LockThresholdBp);
            if (due)
            {
                var index = next.LocksOpened + 1;
                var castTicks = LockCastTicks(cfg, abilities);
                if (castTicks <= 0)
                {
                    next = OpenLock(next, index, tick, emit, patches) with { NextLockTick = tick + cfg.LockRepeatTicks };
                }
                else if (bossActor!.CastId is null && !HasOwnTelegraphPending(pendingTelegraphs, cfg.BossActorId, tick))
                {
                    // The boss never begins the Lock over a cast of its own, or while a circle it drew is still down: that
                    // cast would be replaced and its circle would resolve on nothing (wrong data shown). The Lock waits.
                    var landsTick = tick + castTicks;
                    emit(new SimEvent(tick, "lock_cast",
                        ("actorId", JsValue.Int(cfg.BossActorId)),
                        ("lockIndex", JsValue.Int(index)),
                        ("landsTick", JsValue.Int(landsTick))));
                    next = next with { Casting = new LockCast(index, tick, landsTick), NextLockTick = tick + cfg.LockRepeatTicks };
                }
            }
        }

        var effective = EffectiveScripts(next, scripts);
        if (next.Casting is { } lockCast)
        {
            effective = WithLockCast(effective, cfg, lockCast, scripts);
        }
        return new BossPreStep(next, effective, cfg.TwinsBar, patches);
    }

    /// <summary>Opens Lock <paramref name="index"/> on <paramref name="tick"/>: <c>lock_open</c>, the shield patch, the Window.</summary>
    private static BossState OpenLock(BossState boss, int index, int tick, Action<SimEvent> emit, List<BreakBarEffectPatch> patches)
    {
        var cfg = boss.Config;
        var window = new LockWindow(index, tick, tick + cfg.LockWindowTicks);
        emit(new SimEvent(tick, "lock_open",
            ("actorId", JsValue.Int(cfg.BossActorId)),
            ("lockIndex", JsValue.Int(index)),
            ("endsTick", JsValue.Int(window.EndsTick))));
        // Patches land after this tick's combat (state tick + 1), so the shield ends exactly at EndsTick.
        patches.Add(new BreakBarEffectPatch(cfg.BossActorId, cfg.ShieldEffectId, cfg.LockWindowTicks - 1, 1));
        return boss with { Active = window, LocksOpened = index };
    }

    /// <summary>The Lock ability's castTicks when the Lock is cast (<see cref="BossConfig.CastLock"/>), else 0.</summary>
    private static int LockCastTicks(BossConfig cfg, IReadOnlyDictionary<string, ScriptAbility>? abilities) =>
        cfg.CastLock && cfg.SuppressedBossCasts.Count > 0 && abilities is not null &&
        abilities.TryGetValue(cfg.SuppressedBossCasts[0], out var ability)
            ? Math.Max(0, ability.CastTicks)
            : 0;

    /// <summary>True when a telegraph <paramref name="actorId"/> drew resolves on or after <paramref name="tick"/>.</summary>
    private static bool HasOwnTelegraphPending(IReadOnlyList<Telegraph>? pending, int actorId, int tick) =>
        pending is not null && pending.Any(t => t.SourceId == actorId && t.ResolvesTick >= tick);

    /// <summary>
    /// The boss's script while it casts the Lock: only the Lock cast, a one-shot step on the cast's start tick (its other
    /// steps are held, so no cast replaces the bar), with the target and telegraph of the boss's Lock script step. The
    /// telegraph spans the cast with a lead of exactly the cast time, so the circle is drawn as the bar starts and
    /// resolves as it lands, whatever lead the step holds (an oracle setup clears spansCast; Tempo shrinks the lead).
    /// </summary>
    private static IReadOnlyDictionary<int, IReadOnlyList<ScriptStep>> WithLockCast(
        IReadOnlyDictionary<int, IReadOnlyList<ScriptStep>> effective,
        BossConfig cfg,
        LockCast casting,
        IReadOnlyDictionary<int, IReadOnlyList<ScriptStep>> scripts)
    {
        var lockId = cfg.SuppressedBossCasts[0];
        var content = scripts.TryGetValue(cfg.BossActorId, out var bossScript) && bossScript is not null
            ? bossScript.FirstOrDefault(s => string.Equals(s.Cast, lockId, StringComparison.Ordinal))
            : null;
        var castTicks = casting.LandsTick - casting.StartTick;
        var step = ScriptStep.At(casting.StartTick, lockId) with
        {
            Telegraph = content?.Telegraph is { } tg ? tg with { SpansCast = true, LeadTicks = castTicks } : null,
            Target = content?.Target,
        };
        var result = new SortedDictionary<int, IReadOnlyList<ScriptStep>>();
        foreach (var kv in effective)
        {
            result[kv.Key] = kv.Value;
        }
        result[cfg.BossActorId] = new[] { step };
        return result;
    }

    /// <summary>
    /// The ability table combat runs on an armed run (<c>RunRoot.StepRun</c>): with <see cref="BossConfig.CastLock"/>
    /// the Lock abilities (<see cref="BossConfig.SuppressedBossCasts"/>) keep their cast time, tags and shape but carry no
    /// effects, so the Lock cast is a cast bar whose landing applies nothing itself: these rules own its shield and Window
    /// (the content self Window would otherwise also open on the boss and take the party's Payoffs). Otherwise unchanged.
    /// </summary>
    /// <param name="config">The armed rules.</param>
    /// <param name="abilities">The run's abilities.</param>
    /// <returns><paramref name="abilities"/> itself, or a copy with the Lock abilities' effects removed.</returns>
    public static IReadOnlyDictionary<string, AbilityDef> CombatAbilities(BossConfig config, IReadOnlyDictionary<string, AbilityDef> abilities)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(abilities);
        if (!config.CastLock || !config.SuppressedBossCasts.Any(abilities.ContainsKey))
        {
            return abilities;
        }
        var result = new Dictionary<string, AbilityDef>(StringComparer.Ordinal);
        foreach (var kv in abilities)
        {
            result[kv.Key] = config.SuppressedBossCasts.Contains(kv.Key) ? kv.Value with { Effects = Array.Empty<EffectOp>() } : kv.Value;
        }
        return result;
    }

    /// <summary>
    /// Post-combat (inside <see cref="EchoSpawns.Step"/>, on the stepped state and the tick's events): the Twins spawn
    /// once beside the live boss; inside an open Window a party Setup on the boss is recorded, and a <c>combo</c> on the
    /// boss closing that Setup's Window with a Payoff from another party class breaks the Lock.
    /// </summary>
    /// <param name="boss">The boss state.</param>
    /// <param name="state">The stepped state (read only).</param>
    /// <param name="events">This tick's events so far.</param>
    /// <param name="tick">The run root's post-step tick (the tick pull_start and echo_spawn carry).</param>
    /// <param name="emit">Event sink.</param>
    /// <exception cref="InvalidOperationException">A twin id is already taken by another actor.</exception>
    public static BossPostStep AfterCombat(BossState boss, SimState state, IEnumerable<SimEvent> events, int tick, Action<SimEvent> emit)
    {
        ArgumentNullException.ThrowIfNull(boss);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(emit);
        var cfg = boss.Config;
        var next = boss;
        var spawned = new List<ActorState>();

        // PRESPAWN-AGGRO: a prespawned boss stands idle in its arena; the Twins join it on its engage, not at run start.
        if (!next.TwinsSpawned && state.Actors.TryGetValue(cfg.BossActorId, out var bossActor) && bossActor.Alive && !bossActor.Idle)
        {
            foreach (var twin in cfg.Twins)
            {
                if (state.Actors.ContainsKey(twin.ActorId))
                {
                    throw new InvalidOperationException($"twin actor id {twin.ActorId} is already in use");
                }
                var pos = new Vec3(bossActor.Pos.X + twin.Offset.X, bossActor.Pos.Y + twin.Offset.Y, bossActor.Pos.Z + twin.Offset.Z);
                spawned.Add(new ActorState
                {
                    Id = twin.ActorId,
                    Kind = ActorKind.Enemy,
                    ClassId = twin.ClassId,
                    Role = ActorRole.None,
                    Pos = pos,
                    Yaw = bossActor.Yaw,
                    Health = twin.MaxHealth,
                    MaxHealth = twin.MaxHealth,
                    Resource = 0,
                    MaxResource = 0,
                    Alive = true,
                });
                emit(new SimEvent(tick, "boss_add_spawn",
                    ("actorId", JsValue.Int(twin.ActorId)),
                    ("classId", JsValue.Str(twin.ClassId)),
                    ("bossActorId", JsValue.Int(cfg.BossActorId))));
            }
            next = next with { TwinsSpawned = true };
        }

        // TWINS-DIE-WITH-BOSS (Matt 2026-10-20; WoW ends an encounter on its boss's death): on the step the boss's death
        // event fires, every Twin still standing dies with it, on the boss's death tick, each with its own death event
        // (logs and combat text read a kill, not a silent vanish). No Twin is left alive for run_end.
        if (next.TwinsSpawned && events.FirstOrDefault(e => e.Type == "death" && IntOf(e, "actorId") == cfg.BossActorId) is { } bossDeath)
        {
            foreach (var twin in cfg.Twins)
            {
                if (!state.Actors.TryGetValue(twin.ActorId, out var standing) || !standing.Alive)
                {
                    continue;
                }
                var fallen = standing.Clone();
                fallen.Health = 0;
                fallen.Alive = false;
                spawned.Add(fallen);
                emit(new SimEvent(bossDeath.Tick, "death",
                    ("actorId", JsValue.Int(twin.ActorId)),
                    ("sourceId", JsValue.Int(cfg.BossActorId)),
                    ("reason", JsValue.Str("boss_death"))));
                Damage.EndCastOnDeath(fallen, emit, bossDeath.Tick);
            }
        }

        if (next.Active is not { } window)
        {
            return new BossPostStep(next, spawned);
        }
        foreach (var e in events)
        {
            if (e.Tick < window.OpenTick || e.Tick >= window.EndsTick || IntOf(e, "targetId") != cfg.BossActorId)
            {
                continue;
            }
            if (e.Type == "window_opened" && IntOf(e, "setupActorId") is { } setupId && IsPlayer(state, setupId))
            {
                window = window with { SetupActorId = setupId };
                continue;
            }
            if (e.Type != "combo" || window.SetupActorId is not { } inWindowSetup)
            {
                continue;
            }
            var setupActor = IntOf(e, "setupActorId");
            var payoffActor = IntOf(e, "payoffActorId");
            var setupClass = StrOf(e, "setupClass");
            var payoffClass = StrOf(e, "payoffClass");
            if (setupActor != inWindowSetup || payoffActor is not { } payoffId || !IsPlayer(state, payoffId) ||
                setupClass is null || payoffClass is null || string.Equals(setupClass, payoffClass, StringComparison.Ordinal))
            {
                continue;
            }
            emit(new SimEvent(tick, "lock_broken",
                ("actorId", JsValue.Int(cfg.BossActorId)),
                ("lockIndex", JsValue.Int(window.Index)),
                ("comboTick", JsValue.Int(e.Tick)),
                ("setupActorId", JsValue.Int(inWindowSetup)),
                ("setupClass", JsValue.Str(setupClass)),
                ("payoffActorId", JsValue.Int(payoffId)),
                ("payoffClass", JsValue.Str(payoffClass))));
            return new BossPostStep(next with { Active = null, LocksBroken = next.LocksBroken + 1, ShieldDropPending = true }, spawned);
        }
        return new BossPostStep(next with { Active = window }, spawned);
    }

    private static bool IsPlayer(SimState state, int actorId) =>
        state.Actors.TryGetValue(actorId, out var a) && a.Kind == ActorKind.Player;

    private static int? IntOf(SimEvent e, string key) =>
        e.Get(key) is JsInt v && v.Value >= int.MinValue && v.Value <= int.MaxValue ? (int)v.Value : null;

    private static string? StrOf(SimEvent e, string key) => e.Get(key) is JsString s ? s.Value : null;
}
