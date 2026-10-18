using System;
using System.Collections.Generic;
using System.Linq;
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
}

/// <summary>The armed boss's pre-combat output for one tick.</summary>
/// <param name="Boss">The next boss state.</param>
/// <param name="Scripts">The scripts to run this tick (Twins added, suppressed casts removed, enrage applied).</param>
/// <param name="TwinsBar">The Twins' bar source to drain this tick.</param>
/// <param name="EffectPatches">Shield on/off patches for the run root to apply after combat.</param>
public sealed record BossPreStep(BossState Boss, IReadOnlyDictionary<int, IReadOnlyList<ScriptStep>> Scripts, BreakBarSource TwinsBar, IReadOnlyList<BreakBarEffectPatch> EffectPatches);

/// <summary>The armed boss's post-combat output for one tick.</summary>
/// <param name="Boss">The next boss state.</param>
/// <param name="Spawned">Twins realised this tick, for the run root to merge into state.</param>
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
/// ends unbroken is a failed Lock; the third failed Lock enrages the boss into a Tempo spike (its script loops fire
/// <see cref="BossConfig.EnrageTempoBp"/> as often), never an instant wipe.</item>
/// </list>
/// Events: <c>boss_add_spawn</c>, <c>lock_open</c>, <c>lock_broken</c>, <c>lock_failed</c>, <c>boss_enrage</c>.
/// </summary>
public static class BossRules
{
    /// <summary>Arms <paramref name="encounter"/> with the boss rules (call once, after <c>CreateRun</c>).</summary>
    /// <exception cref="ArgumentException">The bar's actors are not exactly the twins, or a twin id repeats or is the boss's.</exception>
    public static EncounterState Arm(EncounterState encounter, BossConfig config)
    {
        ArgumentNullException.ThrowIfNull(encounter);
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
        return encounter with { Boss = new BossState(config) };
    }

    /// <summary>
    /// The scripts the armed encounter runs: the boss's suppressed casts removed, the Twins' scripts added, and once
    /// enraged every boss loop (and repeating step) re-paced by <see cref="BossConfig.EnrageTempoBp"/>.
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
            result[cfg.BossActorId] = (boss.Enraged ? kept.Select(s => Spike(s, cfg.EnrageTempoBp)) : kept).ToList();
        }
        foreach (var twin in cfg.Twins)
        {
            result[twin.ActorId] = twin.Script;
        }
        return result;
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
    /// 50% gate (or the 30 s repeat once it has opened) opens the next Lock and shields the boss.
    /// </summary>
    public static BossPreStep PreCombat(SimState state, BossState boss, IReadOnlyDictionary<int, IReadOnlyList<ScriptStep>> scripts, Action<SimEvent> emit)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(boss);
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
            var fails = next.LockFails + 1;
            emit(new SimEvent(tick, "lock_failed",
                ("actorId", JsValue.Int(cfg.BossActorId)),
                ("lockIndex", JsValue.Int(open.Index)),
                ("fails", JsValue.Int(fails))));
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
            next = next with { Active = null };
        }
        else if (next.Active is null)
        {
            var due = next.NextLockTick is { } at ? tick >= at : AtOrBelowThreshold(bossActor!, cfg.LockThresholdBp);
            if (due)
            {
                var index = next.LocksOpened + 1;
                var window = new LockWindow(index, tick, tick + cfg.LockWindowTicks);
                emit(new SimEvent(tick, "lock_open",
                    ("actorId", JsValue.Int(cfg.BossActorId)),
                    ("lockIndex", JsValue.Int(index)),
                    ("endsTick", JsValue.Int(window.EndsTick))));
                // Patches land after this tick's combat (state tick + 1), so the shield ends exactly at EndsTick.
                patches.Add(new BreakBarEffectPatch(cfg.BossActorId, cfg.ShieldEffectId, cfg.LockWindowTicks - 1, 1));
                next = next with { Active = window, LocksOpened = index, NextLockTick = tick + cfg.LockRepeatTicks };
            }
        }

        return new BossPreStep(next, EffectiveScripts(next, scripts), cfg.TwinsBar, patches);
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

        if (!next.TwinsSpawned && state.Actors.TryGetValue(cfg.BossActorId, out var bossActor) && bossActor.Alive)
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
