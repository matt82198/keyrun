using System;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Combat;

/// <summary>
/// The floor-patch Window registry (sim/combat/window-state.ts:176-182 FloorWindowRegistry). In rules its
/// <see cref="Patches"/> is the SAME record as <see cref="SimState.FloorWindows"/>, so a floor Window survives the
/// per-step state clone and the rules being rebuilt.
/// </summary>
public sealed class FloorWindowRegistry
{
    /// <summary>A registry over an existing patch record (normally <c>state.FloorWindows</c>).</summary>
    /// <param name="patches">The patches, by windowId.</param>
    public FloorWindowRegistry(JsRecord<FloorWindow> patches)
    {
        Patches = patches ?? throw new ArgumentNullException(nameof(patches));
    }

    /// <summary>A free-standing registry (window-state.ts:180 createFloorRegistry), detached from any state.</summary>
    public FloorWindowRegistry()
        : this(new JsRecord<FloorWindow>())
    {
    }

    /// <summary>Open floor patches by windowId, in JS key order.</summary>
    public JsRecord<FloorWindow> Patches { get; }
}

/// <summary>A parsed Window effect id (window-state.ts:201-205 ParsedWindowId).</summary>
/// <param name="WindowId">Window id.</param>
/// <param name="SetupClass">ComboTable row key.</param>
/// <param name="Alternate">True for a Lock (alternation) Window.</param>
public sealed record ParsedWindowId(string WindowId, string SetupClass, bool Alternate);

/// <summary>
/// Port of sim/combat/window-state.ts: the shared low-level Window and Lock state Windows.cs and Payoff.cs both use.
/// A target/self Window rides on its carrier as a synthetic EffectInstance, effectId
/// <c>window:&lt;windowId&gt;:&lt;setupClass&gt;</c> (or <c>lockwindow:...</c> for a Lock), sourceId = setup actor.
/// The Lock bookkeeping (the class that closed the last Lock, the unpaid-Lock counter) is stored the same way on
/// the boss with an effectively permanent end tick. No EffectDef matches these ids, so modifier aggregation and
/// periodic ticks ignore them.
/// </summary>
public static class WindowState
{
    /// <summary>
    /// window-state.ts:162 PERMANENT_TICK. C# deviation: the TS uses Number.MAX_SAFE_INTEGER; EffectInstance.EndsTick
    /// is an int here, so int.MaxValue. Both compare greater than every reachable tick and neither is projected.
    /// </summary>
    public const int PermanentTick = int.MaxValue;

    /// <summary>window-state.ts:168 FLOOR_WINDOW_RADIUS_MM: a floor Window's radius when its op sets none.</summary>
    public const long FloorWindowRadiusMm = 3000;

    /// <summary>window-state.ts:170 WINDOW_PREFIX.</summary>
    public const string WindowPrefix = "window:";

    /// <summary>window-state.ts:171 LOCK_WINDOW_PREFIX.</summary>
    public const string LockWindowPrefix = "lockwindow:";

    /// <summary>window-state.ts:174 LOCK_ENRAGE_THRESHOLD: unpaid Locks before the boss enrages.</summary>
    public const long LockEnrageThreshold = 2;

    private const string LastClassPrefix = "lock:lastclass:";
    private const string LockFailsId = "lock:fails";

    /// <summary>window-state.ts:186-195 makeFloorWindow: a patch, the radius defaulting to <see cref="FloorWindowRadiusMm"/>.</summary>
    /// <param name="windowId">Window id.</param>
    /// <param name="setupClass">Setup class.</param>
    /// <param name="setupActorId">Setup actor.</param>
    /// <param name="pos">Centre.</param>
    /// <param name="endsTick">End tick.</param>
    /// <param name="radiusMm">Radius override, or null.</param>
    /// <returns>The patch.</returns>
    public static FloorWindow MakeFloorWindow(string windowId, string setupClass, int setupActorId, Vec3 pos, int endsTick, long? radiusMm) => new()
    {
        WindowId = windowId,
        SetupClass = setupClass,
        SetupActorId = setupActorId,
        Pos = pos,
        EndsTick = endsTick,
        RadiusMm = radiusMm ?? FloorWindowRadiusMm,
    };

    /// <summary>window-state.ts:197-199 windowEffectId.</summary>
    /// <param name="windowId">Window id.</param>
    /// <param name="setupClass">Setup class.</param>
    /// <param name="alternate">True for a Lock.</param>
    /// <returns>The synthetic effect id.</returns>
    public static string WindowEffectId(string windowId, string setupClass, bool alternate) =>
        (alternate ? LockWindowPrefix : WindowPrefix) + windowId + ":" + setupClass;

    /// <summary>window-state.ts:207-215 parseWindowEffectId: null for a non-Window id or one with no ':' after the prefix.</summary>
    /// <param name="effectId">An effect id.</param>
    /// <returns>The parts, or null.</returns>
    public static ParsedWindowId? ParseWindowEffectId(string effectId)
    {
        var alternate = effectId.StartsWith(LockWindowPrefix, StringComparison.Ordinal);
        var plain = effectId.StartsWith(WindowPrefix, StringComparison.Ordinal);
        if (!alternate && !plain)
        {
            return null;
        }
        var rest = effectId[(alternate ? LockWindowPrefix : WindowPrefix).Length..];
        var sep = rest.IndexOf(':', StringComparison.Ordinal);
        if (sep < 0)
        {
            return null;
        }
        return new ParsedWindowId(rest[..sep], rest[(sep + 1)..], alternate);
    }

    /// <summary>window-state.ts:217-219 isWindowEffectId.</summary>
    /// <param name="effectId">An effect id.</param>
    /// <returns>True for a Window or Lock Window id.</returns>
    public static bool IsWindowEffectId(string effectId) =>
        effectId.StartsWith(WindowPrefix, StringComparison.Ordinal) || effectId.StartsWith(LockWindowPrefix, StringComparison.Ordinal);

    /// <summary>window-state.ts:221-223 findOpenWindow: the first Window instance still open at <paramref name="now"/>.</summary>
    /// <param name="target">The carrier.</param>
    /// <param name="now">Current tick.</param>
    /// <returns>The instance, or null.</returns>
    public static EffectInstance? FindOpenWindow(ActorState target, int now) =>
        target.Effects.Find(e => IsWindowEffectId(e.EffectId) && e.EndsTick > now);

    /// <summary>window-state.ts:225-232 lastSetupClassOf: the class that closed this actor's last Lock, or null.</summary>
    /// <param name="actor">The boss.</param>
    /// <returns>The class id, or null.</returns>
    public static string? LastSetupClassOf(ActorState actor)
    {
        var inst = actor.Effects.Find(e => e.EffectId.StartsWith(LastClassPrefix, StringComparison.Ordinal));
        return inst is null ? null : inst.EffectId[LastClassPrefix.Length..];
    }

    /// <summary>window-state.ts:234-243 setLastSetupClass: replaces the record with <paramref name="classId"/>.</summary>
    /// <param name="actor">The boss.</param>
    /// <param name="classId">The class that just closed a Lock.</param>
    public static void SetLastSetupClass(ActorState actor, string classId)
    {
        Effects.RemoveEffectInstanceWhere(actor, e => e.EffectId.StartsWith(LastClassPrefix, StringComparison.Ordinal));
        Effects.PushRawEffectInstance(actor, Permanent(actor, LastClassPrefix + classId));
    }

    /// <summary>window-state.ts:245-259 incrementLockFailCount: bumps (or starts at 1) the unpaid-Lock counter.</summary>
    /// <param name="actor">The boss.</param>
    /// <returns>The new count.</returns>
    public static long IncrementLockFailCount(ActorState actor)
    {
        var inst = actor.Effects.Find(e => e.EffectId == LockFailsId);
        if (inst is not null)
        {
            inst.Stacks += 1;
            return inst.Stacks;
        }
        Effects.PushRawEffectInstance(actor, Permanent(actor, LockFailsId));
        return 1;
    }

    /// <summary>window-state.ts:261-263 resetLockFailCount.</summary>
    /// <param name="actor">The boss.</param>
    public static void ResetLockFailCount(ActorState actor) => Effects.RemoveEffectInstanceWhere(actor, e => e.EffectId == LockFailsId);

    /// <summary>window-state.ts:265-267 lockFailCount: 0 when there is no counter.</summary>
    /// <param name="actor">The boss.</param>
    /// <returns>The count.</returns>
    public static long LockFailCount(ActorState actor) => actor.Effects.Find(e => e.EffectId == LockFailsId)?.Stacks ?? 0;

    private static EffectInstance Permanent(ActorState actor, string effectId) => new()
    {
        EffectId = effectId,
        SourceId = actor.Id,
        Stacks = 1,
        EndsTick = PermanentTick,
        NextPeriodicTick = null,
    };
}
