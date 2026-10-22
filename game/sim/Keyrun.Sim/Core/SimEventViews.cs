using System.Collections.Generic;

namespace Keyrun.Sim.Core;

// Typed, read-only views over the sim's string-keyed events (C#-only test and log seam; nothing in the sim reads them,
// so no trace, hash or fixture moves). One view per event type tag the sim emits with `new SimEvent(`; each documents
// the file:line (under game/sim/Keyrun.Sim/) that emits it. Ids, ticks and amounts read as int (an integer outside the
// int range is refused), ability, school, class and other tags as string. A field only some emit sites write is
// nullable (or, for a flag, false when absent); every other field is required.

/// <summary><c>overheat</c>, emitted at Combat/Casting.cs:279 (an Overheat cast completes).</summary>
public readonly record struct OverheatView(int Tick, int ActorId, string AbilityId);

/// <summary><c>cast_stopped</c>, emitted at Combat/Casting.cs:300 (Stopcast: a cast in progress ends without landing).</summary>
public readonly record struct CastStoppedView(int Tick, int ActorId, string AbilityId);

/// <summary><c>cast_complete</c>, emitted at Combat/Casting.cs:413 (a cast lands).</summary>
public readonly record struct CastCompleteView(int Tick, int ActorId, string AbilityId);

/// <summary>
/// <c>interrupt</c>, emitted at Combat/Casting.cs:508 (a kick) and Run/ControlStops.cs:94 (a stun or other stop ends
/// the cast; that site also writes <see cref="Stop"/>, the stopping effect id, which is null for a kick).
/// </summary>
public readonly record struct InterruptView(int Tick, int SourceId, int TargetId, string AbilityId, string School, string? Stop);

/// <summary><c>damage</c>, emitted at Combat/Damage.cs:120 (the damage pipeline).</summary>
public readonly record struct DamageView(int Tick, int SourceId, int TargetId, int RawAmount, int ModifiedAmount, int Absorbed, int HealthDamage);

/// <summary>
/// <c>death</c>, emitted at Combat/Damage.cs:131 (health reaches 0), Encounter/Boss.cs:488 (a Twin falls with its boss)
/// and Encounter/EchoSpawns.cs:68 (an Echo add falls with its boss); the last two write <see cref="Reason"/> "boss_death".
/// </summary>
public readonly record struct DeathView(int Tick, int ActorId, int SourceId, string? Reason);

/// <summary><c>heal</c>, emitted at Combat/Damage.cs:172 (ApplyHeal; <see cref="Amount"/> is the amount applied).</summary>
public readonly record struct HealView(int Tick, int SourceId, int TargetId, int Amount);

/// <summary><c>miss</c>, emitted at Combat/Mitigation.cs:42 (a blinded source's hit misses).</summary>
public readonly record struct MissView(int Tick, int SourceId, int TargetId, int RawAmount);

/// <summary><c>mark_applied</c>, emitted at Combat/PairMarks.cs:144 (the Pair's spread or stack marks land).</summary>
public readonly record struct MarkAppliedView(int Tick, string Cast, string Kind, int SourceId, IReadOnlyList<int> TargetIds, int Radius, int DetonatesTick);

/// <summary>One entry of <see cref="MarkDetonatedView.Damage"/>: who the detonation hit and the health it took.</summary>
public readonly record struct MarkHit(int ActorId, int Amount);

/// <summary><c>mark_detonated</c>, emitted at Combat/PairMarks.cs:185 (a mark runs out and resolves).</summary>
public readonly record struct MarkDetonatedView(int Tick, string Kind, int MarkedId, IReadOnlyList<int> HitIds, IReadOnlyList<MarkHit> Damage);

/// <summary>
/// <c>combo_denied</c>, emitted at Combat/Payoff.cs:67 (a Window on a target) and Combat/Payoff.cs:141 (a floor patch,
/// which writes no targetId, so <see cref="TargetId"/> is null).
/// </summary>
public readonly record struct ComboDeniedView(int Tick, string WindowId, int? TargetId, int SetupActorId, int PayoffActorId, string Reason);

/// <summary>
/// <c>combo</c>, emitted at Combat/Payoff.cs:89 (a Window on a target closes; writes targetId) and Combat/Payoff.cs:157
/// (a floor patch closes; writes "field": true and no targetId).
/// </summary>
public readonly record struct ComboView(int Tick, string ComboId, string WindowId, int SetupActorId, string SetupClass, int PayoffActorId, string PayoffClass, int? TargetId, bool Field);

/// <summary><c>cast_refused</c>, emitted at Combat/Rules.cs:200 (a cast command is refused; <see cref="Reason"/> is the error).</summary>
public readonly record struct CastRefusedView(int Tick, int ActorId, string AbilityId, string Reason);

/// <summary>
/// <c>window_opened</c>, emitted at Combat/Windows.cs:95 (on a target; writes targetId) and Combat/Windows.cs:108 (a floor
/// patch; writes the patch's telegraph object instead, kept as raw JSON in <see cref="Telegraph"/>).
/// </summary>
public readonly record struct WindowOpenedView(int Tick, string WindowId, bool OnTarget, int SetupActorId, string SetupClass, int? TargetId, int EndsTick, JsValue? Telegraph);

/// <summary>
/// <c>window_expired</c>, emitted at Combat/Windows.cs:145 (on a target; writes targetId and lock) and
/// Combat/Windows.cs:182 (a floor patch; neither, so <see cref="TargetId"/> is null and <see cref="Lock"/> false).
/// </summary>
public readonly record struct WindowExpiredView(int Tick, string WindowId, bool OnTarget, int SetupActorId, string SetupClass, int? TargetId, bool Lock);

/// <summary><c>lock_enrage</c>, emitted at Combat/Windows.cs:160 (unpaid Lock Windows reach the threshold).</summary>
public readonly record struct LockEnrageView(int Tick, int ActorId);

/// <summary><c>pull_start</c>, emitted at Encounter/Pulls.cs:149 (SpawnPull) and Encounter/Aggro.cs:208 (a pull group engages).</summary>
public readonly record struct PullStartView(int Tick, int PullIndex, string PullId, IReadOnlyList<int> ActorIds);

/// <summary><c>pack_engage</c>, emitted at Encounter/Aggro.cs:209 (a pack that is not a pull engages).</summary>
public readonly record struct PackEngageView(int Tick, string PackId, IReadOnlyList<int> ActorIds);

/// <summary><c>lock_failed</c>, emitted at Encounter/Boss.cs:279 (a Lock Window closes unbroken).</summary>
public readonly record struct LockFailedView(int Tick, int ActorId, int LockIndex, int Fails, int TempoBp);

/// <summary><c>boss_enrage</c>, emitted at Encounter/Boss.cs:287 (enough Lock fails).</summary>
public readonly record struct BossEnrageView(int Tick, int ActorId, int TempoBp);

/// <summary><c>lock_cast</c>, emitted at Encounter/Boss.cs:322 (the boss begins casting the Lock).</summary>
public readonly record struct LockCastView(int Tick, int ActorId, int LockIndex, int LandsTick);

/// <summary><c>lock_open</c>, emitted at Encounter/Boss.cs:344 (OpenLock).</summary>
public readonly record struct LockOpenView(int Tick, int ActorId, int LockIndex, int EndsTick);

/// <summary><c>boss_add_spawn</c>, emitted at Encounter/Boss.cs:465 (a Twin spawns beside the boss).</summary>
public readonly record struct BossAddSpawnView(int Tick, int ActorId, string ClassId, int BossActorId);

/// <summary><c>lock_broken</c>, emitted at Encounter/Boss.cs:524 (a cross-class combo on the boss breaks the Lock).</summary>
public readonly record struct LockBrokenView(int Tick, int ActorId, int LockIndex, int ComboTick, int SetupActorId, string SetupClass, int PayoffActorId, string PayoffClass);

/// <summary><c>stagger</c>, emitted at Encounter/BreakBar.cs:95 (a Break bar empties).</summary>
public readonly record struct StaggerView(int Tick, string BarId, IReadOnlyList<int> ActorIds);

/// <summary><c>echo_spawn</c>, emitted at Encounter/EchoSpawns.cs:109 (an Echo add spawns).</summary>
public readonly record struct EchoSpawnView(int Tick, int ActorId, int SourceActorId, int MaxHealth);

/// <summary><c>pull_clear</c>, emitted at Encounter/Pulls.cs:176 (every spawn of the active pull is dead).</summary>
public readonly record struct PullClearView(int Tick, int PullIndex, string PullId);

/// <summary>
/// <c>telegraph</c>, emitted at Encounter/Scripts.cs:173 (TelegraphEventAt); <see cref="Radius"/>, <see cref="Angle1024"/>,
/// <see cref="Length"/> and <see cref="Width"/> are written only when the shape has them.
/// </summary>
public readonly record struct TelegraphView(int Tick, int ActorId, string Cast, string Shape, int LeadTicks, int ResolvesTick, Vec3 Origin, int Yaw, int? Radius, int? Angle1024, int? Length, int? Width);

/// <summary><c>telegraph_hit</c>, emitted at Encounter/Telegraphs.cs:270 (a telegraph resolves).</summary>
public readonly record struct TelegraphHitView(int Tick, int TelegraphId, IReadOnlyList<int> ActorIds);

/// <summary><c>telegraph_cancel</c>, emitted at Encounter/Telegraphs.cs:332 (CancelUnlanded).</summary>
public readonly record struct TelegraphCancelView(int Tick, int TelegraphId, int ActorId, string Cast);

/// <summary><c>finish</c>, emitted at Encounter/Timer.cs:220 (the encounter's result).</summary>
public readonly record struct FinishView(int Tick, string Result);

/// <summary><c>immune</c>, emitted at Run/ControlStops.cs:144 (a stun refused on a stun-immune actor).</summary>
public readonly record struct ImmuneView(int Tick, int SourceId, int TargetId, string EffectId);

/// <summary><c>respawn</c>, emitted at Run/Release.cs:75.</summary>
public readonly record struct RespawnView(int Tick, int ActorId);

/// <summary><c>run_end</c>, emitted at Run/Run.cs:289.</summary>
public readonly record struct RunEndView(int Tick, string Result);

/// <summary><c>room_enter</c>, emitted at Run/Run.cs:371 (an actor steps into a room box).</summary>
public readonly record struct RoomEnterView(int Tick, int ActorId, string Room);

/// <summary>
/// Readers from a <see cref="SimEvent"/> to its typed view. Each <c>TryX</c> returns true and fills the view only when
/// the event carries that type tag and every required field with the right JSON kind (ints within the int range);
/// otherwise it returns false with the view at its default. None throws, a null event included.
/// </summary>
public static class SimEventViews
{
    /// <summary>Reads an <c>overheat</c> event.</summary>
    public static bool TryOverheat(SimEvent e, out OverheatView v)
    {
        v = default;
        if (!Is(e, "overheat") || !Int(e, "actorId", out var actor) || !Str(e, "abilityId", out var ability))
        {
            return false;
        }
        v = new OverheatView(e.Tick, actor, ability);
        return true;
    }

    /// <summary>Reads a <c>cast_stopped</c> event.</summary>
    public static bool TryCastStopped(SimEvent e, out CastStoppedView v)
    {
        v = default;
        if (!Is(e, "cast_stopped") || !Int(e, "actorId", out var actor) || !Str(e, "abilityId", out var ability))
        {
            return false;
        }
        v = new CastStoppedView(e.Tick, actor, ability);
        return true;
    }

    /// <summary>Reads a <c>cast_complete</c> event.</summary>
    public static bool TryCastComplete(SimEvent e, out CastCompleteView v)
    {
        v = default;
        if (!Is(e, "cast_complete") || !Int(e, "actorId", out var actor) || !Str(e, "abilityId", out var ability))
        {
            return false;
        }
        v = new CastCompleteView(e.Tick, actor, ability);
        return true;
    }

    /// <summary>Reads an <c>interrupt</c> event (a kick, or a stop with its effect id).</summary>
    public static bool TryInterrupt(SimEvent e, out InterruptView v)
    {
        v = default;
        if (!Is(e, "interrupt") || !Int(e, "sourceId", out var source) || !Int(e, "targetId", out var target) ||
            !Str(e, "abilityId", out var ability) || !Str(e, "school", out var school) || !OptStr(e, "stop", out var stop))
        {
            return false;
        }
        v = new InterruptView(e.Tick, source, target, ability, school, stop);
        return true;
    }

    /// <summary>Reads a <c>damage</c> event.</summary>
    public static bool TryDamage(SimEvent e, out DamageView v)
    {
        v = default;
        if (!Is(e, "damage") || !Int(e, "sourceId", out var source) || !Int(e, "targetId", out var target) ||
            !Int(e, "rawAmount", out var raw) || !Int(e, "modifiedAmount", out var modified) ||
            !Int(e, "absorbed", out var absorbed) || !Int(e, "healthDamage", out var health))
        {
            return false;
        }
        v = new DamageView(e.Tick, source, target, raw, modified, absorbed, health);
        return true;
    }

    /// <summary>Reads a <c>death</c> event.</summary>
    public static bool TryDeath(SimEvent e, out DeathView v)
    {
        v = default;
        if (!Is(e, "death") || !Int(e, "actorId", out var actor) || !Int(e, "sourceId", out var source) || !OptStr(e, "reason", out var reason))
        {
            return false;
        }
        v = new DeathView(e.Tick, actor, source, reason);
        return true;
    }

    /// <summary>Reads a <c>heal</c> event.</summary>
    public static bool TryHeal(SimEvent e, out HealView v)
    {
        v = default;
        if (!Is(e, "heal") || !Int(e, "sourceId", out var source) || !Int(e, "targetId", out var target) || !Int(e, "amount", out var amount))
        {
            return false;
        }
        v = new HealView(e.Tick, source, target, amount);
        return true;
    }

    /// <summary>Reads a <c>miss</c> event.</summary>
    public static bool TryMiss(SimEvent e, out MissView v)
    {
        v = default;
        if (!Is(e, "miss") || !Int(e, "sourceId", out var source) || !Int(e, "targetId", out var target) || !Int(e, "rawAmount", out var raw))
        {
            return false;
        }
        v = new MissView(e.Tick, source, target, raw);
        return true;
    }

    /// <summary>Reads a <c>mark_applied</c> event.</summary>
    public static bool TryMarkApplied(SimEvent e, out MarkAppliedView v)
    {
        v = default;
        if (!Is(e, "mark_applied") || !Str(e, "cast", out var cast) || !Str(e, "kind", out var kind) ||
            !Int(e, "sourceId", out var source) || !IntList(e, "targetIds", out var targets) ||
            !Int(e, "radius", out var radius) || !Int(e, "detonatesTick", out var detonates))
        {
            return false;
        }
        v = new MarkAppliedView(e.Tick, cast, kind, source, targets, radius, detonates);
        return true;
    }

    /// <summary>Reads a <c>mark_detonated</c> event.</summary>
    public static bool TryMarkDetonated(SimEvent e, out MarkDetonatedView v)
    {
        v = default;
        if (!Is(e, "mark_detonated") || !Str(e, "kind", out var kind) || !Int(e, "markedId", out var marked) ||
            !IntList(e, "hitIds", out var hitIds) || e.Get("damage") is not JsArray damage)
        {
            return false;
        }
        var hits = new List<MarkHit>(damage.Items.Count);
        foreach (var item in damage.Items)
        {
            if (item is not JsObject o || !AsInt(o.Get("actorId"), out var id) || !AsInt(o.Get("amount"), out var amount))
            {
                return false;
            }
            hits.Add(new MarkHit(id, amount));
        }
        v = new MarkDetonatedView(e.Tick, kind, marked, hitIds, hits);
        return true;
    }

    /// <summary>Reads a <c>combo_denied</c> event.</summary>
    public static bool TryComboDenied(SimEvent e, out ComboDeniedView v)
    {
        v = default;
        if (!Is(e, "combo_denied") || !Str(e, "windowId", out var window) || !OptInt(e, "targetId", out var target) ||
            !Int(e, "setupActorId", out var setup) || !Int(e, "payoffActorId", out var payoff) || !Str(e, "reason", out var reason))
        {
            return false;
        }
        v = new ComboDeniedView(e.Tick, window, target, setup, payoff, reason);
        return true;
    }

    /// <summary>Reads a <c>combo</c> event.</summary>
    public static bool TryCombo(SimEvent e, out ComboView v)
    {
        v = default;
        if (!Is(e, "combo") || !Str(e, "comboId", out var combo) || !Str(e, "windowId", out var window) ||
            !Int(e, "setupActorId", out var setup) || !Str(e, "setupClass", out var setupClass) ||
            !Int(e, "payoffActorId", out var payoff) || !Str(e, "payoffClass", out var payoffClass) ||
            !OptInt(e, "targetId", out var target) || !OptBool(e, "field", out var field))
        {
            return false;
        }
        v = new ComboView(e.Tick, combo, window, setup, setupClass, payoff, payoffClass, target, field);
        return true;
    }

    /// <summary>Reads a <c>cast_refused</c> event.</summary>
    public static bool TryCastRefused(SimEvent e, out CastRefusedView v)
    {
        v = default;
        if (!Is(e, "cast_refused") || !Int(e, "actorId", out var actor) || !Str(e, "abilityId", out var ability) || !Str(e, "reason", out var reason))
        {
            return false;
        }
        v = new CastRefusedView(e.Tick, actor, ability, reason);
        return true;
    }

    /// <summary>Reads a <c>window_opened</c> event.</summary>
    public static bool TryWindowOpened(SimEvent e, out WindowOpenedView v)
    {
        v = default;
        if (!Is(e, "window_opened") || !Str(e, "windowId", out var window) || !Bool(e, "onTarget", out var onTarget) ||
            !Int(e, "setupActorId", out var setup) || !Str(e, "setupClass", out var setupClass) ||
            !OptInt(e, "targetId", out var target) || !Int(e, "endsTick", out var ends))
        {
            return false;
        }
        v = new WindowOpenedView(e.Tick, window, onTarget, setup, setupClass, target, ends, e.Get("telegraph"));
        return true;
    }

    /// <summary>Reads a <c>window_expired</c> event.</summary>
    public static bool TryWindowExpired(SimEvent e, out WindowExpiredView v)
    {
        v = default;
        if (!Is(e, "window_expired") || !Str(e, "windowId", out var window) || !Bool(e, "onTarget", out var onTarget) ||
            !Int(e, "setupActorId", out var setup) || !Str(e, "setupClass", out var setupClass) ||
            !OptInt(e, "targetId", out var target) || !OptBool(e, "lock", out var isLock))
        {
            return false;
        }
        v = new WindowExpiredView(e.Tick, window, onTarget, setup, setupClass, target, isLock);
        return true;
    }

    /// <summary>Reads a <c>lock_enrage</c> event.</summary>
    public static bool TryLockEnrage(SimEvent e, out LockEnrageView v)
    {
        v = default;
        if (!Is(e, "lock_enrage") || !Int(e, "actorId", out var actor))
        {
            return false;
        }
        v = new LockEnrageView(e.Tick, actor);
        return true;
    }

    /// <summary>Reads a <c>pull_start</c> event.</summary>
    public static bool TryPullStart(SimEvent e, out PullStartView v)
    {
        v = default;
        if (!Is(e, "pull_start") || !Int(e, "pullIndex", out var index) || !Str(e, "pullId", out var pull) || !IntList(e, "actorIds", out var actors))
        {
            return false;
        }
        v = new PullStartView(e.Tick, index, pull, actors);
        return true;
    }

    /// <summary>Reads a <c>pack_engage</c> event.</summary>
    public static bool TryPackEngage(SimEvent e, out PackEngageView v)
    {
        v = default;
        if (!Is(e, "pack_engage") || !Str(e, "packId", out var pack) || !IntList(e, "actorIds", out var actors))
        {
            return false;
        }
        v = new PackEngageView(e.Tick, pack, actors);
        return true;
    }

    /// <summary>Reads a <c>lock_failed</c> event.</summary>
    public static bool TryLockFailed(SimEvent e, out LockFailedView v)
    {
        v = default;
        if (!Is(e, "lock_failed") || !Int(e, "actorId", out var actor) || !Int(e, "lockIndex", out var index) ||
            !Int(e, "fails", out var fails) || !Int(e, "tempoBp", out var tempo))
        {
            return false;
        }
        v = new LockFailedView(e.Tick, actor, index, fails, tempo);
        return true;
    }

    /// <summary>Reads a <c>boss_enrage</c> event.</summary>
    public static bool TryBossEnrage(SimEvent e, out BossEnrageView v)
    {
        v = default;
        if (!Is(e, "boss_enrage") || !Int(e, "actorId", out var actor) || !Int(e, "tempoBp", out var tempo))
        {
            return false;
        }
        v = new BossEnrageView(e.Tick, actor, tempo);
        return true;
    }

    /// <summary>Reads a <c>lock_cast</c> event.</summary>
    public static bool TryLockCast(SimEvent e, out LockCastView v)
    {
        v = default;
        if (!Is(e, "lock_cast") || !Int(e, "actorId", out var actor) || !Int(e, "lockIndex", out var index) || !Int(e, "landsTick", out var lands))
        {
            return false;
        }
        v = new LockCastView(e.Tick, actor, index, lands);
        return true;
    }

    /// <summary>Reads a <c>lock_open</c> event.</summary>
    public static bool TryLockOpen(SimEvent e, out LockOpenView v)
    {
        v = default;
        if (!Is(e, "lock_open") || !Int(e, "actorId", out var actor) || !Int(e, "lockIndex", out var index) || !Int(e, "endsTick", out var ends))
        {
            return false;
        }
        v = new LockOpenView(e.Tick, actor, index, ends);
        return true;
    }

    /// <summary>Reads a <c>boss_add_spawn</c> event.</summary>
    public static bool TryBossAddSpawn(SimEvent e, out BossAddSpawnView v)
    {
        v = default;
        if (!Is(e, "boss_add_spawn") || !Int(e, "actorId", out var actor) || !Str(e, "classId", out var cls) || !Int(e, "bossActorId", out var boss))
        {
            return false;
        }
        v = new BossAddSpawnView(e.Tick, actor, cls, boss);
        return true;
    }

    /// <summary>Reads a <c>lock_broken</c> event.</summary>
    public static bool TryLockBroken(SimEvent e, out LockBrokenView v)
    {
        v = default;
        if (!Is(e, "lock_broken") || !Int(e, "actorId", out var actor) || !Int(e, "lockIndex", out var index) ||
            !Int(e, "comboTick", out var comboTick) || !Int(e, "setupActorId", out var setup) ||
            !Str(e, "setupClass", out var setupClass) || !Int(e, "payoffActorId", out var payoff) || !Str(e, "payoffClass", out var payoffClass))
        {
            return false;
        }
        v = new LockBrokenView(e.Tick, actor, index, comboTick, setup, setupClass, payoff, payoffClass);
        return true;
    }

    /// <summary>Reads a <c>stagger</c> event.</summary>
    public static bool TryStagger(SimEvent e, out StaggerView v)
    {
        v = default;
        if (!Is(e, "stagger") || !Str(e, "barId", out var bar) || !IntList(e, "actorIds", out var actors))
        {
            return false;
        }
        v = new StaggerView(e.Tick, bar, actors);
        return true;
    }

    /// <summary>Reads an <c>echo_spawn</c> event.</summary>
    public static bool TryEchoSpawn(SimEvent e, out EchoSpawnView v)
    {
        v = default;
        if (!Is(e, "echo_spawn") || !Int(e, "actorId", out var actor) || !Int(e, "sourceActorId", out var source) || !Int(e, "maxHealth", out var max))
        {
            return false;
        }
        v = new EchoSpawnView(e.Tick, actor, source, max);
        return true;
    }

    /// <summary>Reads a <c>pull_clear</c> event.</summary>
    public static bool TryPullClear(SimEvent e, out PullClearView v)
    {
        v = default;
        if (!Is(e, "pull_clear") || !Int(e, "pullIndex", out var index) || !Str(e, "pullId", out var pull))
        {
            return false;
        }
        v = new PullClearView(e.Tick, index, pull);
        return true;
    }

    /// <summary>Reads a <c>telegraph</c> event.</summary>
    public static bool TryTelegraph(SimEvent e, out TelegraphView v)
    {
        v = default;
        if (!Is(e, "telegraph") || !Int(e, "actorId", out var actor) || !Str(e, "cast", out var cast) ||
            !Str(e, "shape", out var shape) || !Int(e, "leadTicks", out var lead) || !Int(e, "resolvesTick", out var resolves) ||
            !Vec(e.Get("origin"), out var origin) || !Int(e, "yaw", out var yaw) ||
            !OptInt(e, "radius", out var radius) || !OptInt(e, "angle1024", out var angle) ||
            !OptInt(e, "length", out var length) || !OptInt(e, "width", out var width))
        {
            return false;
        }
        v = new TelegraphView(e.Tick, actor, cast, shape, lead, resolves, origin, yaw, radius, angle, length, width);
        return true;
    }

    /// <summary>Reads a <c>telegraph_hit</c> event.</summary>
    public static bool TryTelegraphHit(SimEvent e, out TelegraphHitView v)
    {
        v = default;
        if (!Is(e, "telegraph_hit") || !Int(e, "telegraphId", out var id) || !IntList(e, "actorIds", out var actors))
        {
            return false;
        }
        v = new TelegraphHitView(e.Tick, id, actors);
        return true;
    }

    /// <summary>Reads a <c>telegraph_cancel</c> event.</summary>
    public static bool TryTelegraphCancel(SimEvent e, out TelegraphCancelView v)
    {
        v = default;
        if (!Is(e, "telegraph_cancel") || !Int(e, "telegraphId", out var id) || !Int(e, "actorId", out var actor) || !Str(e, "cast", out var cast))
        {
            return false;
        }
        v = new TelegraphCancelView(e.Tick, id, actor, cast);
        return true;
    }

    /// <summary>Reads a <c>finish</c> event.</summary>
    public static bool TryFinish(SimEvent e, out FinishView v)
    {
        v = default;
        if (!Is(e, "finish") || !Str(e, "result", out var result))
        {
            return false;
        }
        v = new FinishView(e.Tick, result);
        return true;
    }

    /// <summary>Reads an <c>immune</c> event.</summary>
    public static bool TryImmune(SimEvent e, out ImmuneView v)
    {
        v = default;
        if (!Is(e, "immune") || !Int(e, "sourceId", out var source) || !Int(e, "targetId", out var target) || !Str(e, "effectId", out var effect))
        {
            return false;
        }
        v = new ImmuneView(e.Tick, source, target, effect);
        return true;
    }

    /// <summary>Reads a <c>respawn</c> event.</summary>
    public static bool TryRespawn(SimEvent e, out RespawnView v)
    {
        v = default;
        if (!Is(e, "respawn") || !Int(e, "actorId", out var actor))
        {
            return false;
        }
        v = new RespawnView(e.Tick, actor);
        return true;
    }

    /// <summary>Reads a <c>run_end</c> event.</summary>
    public static bool TryRunEnd(SimEvent e, out RunEndView v)
    {
        v = default;
        if (!Is(e, "run_end") || !Str(e, "result", out var result))
        {
            return false;
        }
        v = new RunEndView(e.Tick, result);
        return true;
    }

    /// <summary>Reads a <c>room_enter</c> event.</summary>
    public static bool TryRoomEnter(SimEvent e, out RoomEnterView v)
    {
        v = default;
        if (!Is(e, "room_enter") || !Int(e, "actorId", out var actor) || !Str(e, "room", out var room))
        {
            return false;
        }
        v = new RoomEnterView(e.Tick, actor, room);
        return true;
    }

    // ---- field readers: false on a missing field, the wrong JSON kind, or an integer outside the int range.

    private static bool Is(SimEvent? e, string type) => e is not null && string.Equals(e.Type, type, System.StringComparison.Ordinal);

    private static bool AsInt(JsValue? value, out int result)
    {
        result = 0;
        if (value is not JsInt i || i.Value < int.MinValue || i.Value > int.MaxValue)
        {
            return false;
        }
        result = (int)i.Value;
        return true;
    }

    private static bool Int(SimEvent e, string key, out int result) => AsInt(e.Get(key), out result);

    private static bool Str(SimEvent e, string key, out string result)
    {
        if (e.Get(key) is JsString s)
        {
            result = s.Value;
            return true;
        }
        result = string.Empty;
        return false;
    }

    private static bool Bool(SimEvent e, string key, out bool result)
    {
        if (e.Get(key) is JsBool b)
        {
            result = b.Value;
            return true;
        }
        result = false;
        return false;
    }

    // Optional fields: absent reads as null (or false); present but of the wrong kind is refused.
    private static bool OptInt(SimEvent e, string key, out int? result)
    {
        result = null;
        var value = e.Get(key);
        if (value is null)
        {
            return true;
        }
        if (!AsInt(value, out var i))
        {
            return false;
        }
        result = i;
        return true;
    }

    private static bool OptStr(SimEvent e, string key, out string? result)
    {
        result = null;
        var value = e.Get(key);
        if (value is null)
        {
            return true;
        }
        if (value is not JsString s)
        {
            return false;
        }
        result = s.Value;
        return true;
    }

    private static bool OptBool(SimEvent e, string key, out bool result)
    {
        result = false;
        var value = e.Get(key);
        if (value is null)
        {
            return true;
        }
        if (value is not JsBool b)
        {
            return false;
        }
        result = b.Value;
        return true;
    }

    private static bool IntList(SimEvent e, string key, out IReadOnlyList<int> result)
    {
        result = System.Array.Empty<int>();
        if (e.Get(key) is not JsArray arr)
        {
            return false;
        }
        var list = new int[arr.Items.Count];
        for (var i = 0; i < list.Length; i++)
        {
            if (!AsInt(arr.Items[i], out list[i]))
            {
                return false;
            }
        }
        result = list;
        return true;
    }

    private static bool Vec(JsValue? value, out Vec3 result)
    {
        result = default;
        if (value is not JsObject o || o.Get("x") is not JsInt x || o.Get("y") is not JsInt y || o.Get("z") is not JsInt z)
        {
            return false;
        }
        result = new Vec3(x.Value, y.Value, z.Value);
        return true;
    }
}
