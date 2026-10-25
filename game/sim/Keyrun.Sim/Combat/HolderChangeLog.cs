using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Combat;

/// <summary>
/// The <c>holder_change</c> event (finding f-6dbdd121, G7 R-ARCH-4 observability): one event whenever an enemy's
/// <see cref="ActorState.HolderId"/> changes, so the playtest log shows which seat holds which enemy. Fields:
/// <c>enemyId</c> and <c>holderId</c> (null when the holder is cleared); the tick is the envelope tick. The holder is
/// only ever written by Holder.cs and by its callers (Leash clears it), so the callers snapshot and emit through here.
/// </summary>
internal static class HolderChangeLog
{
    /// <summary>The event type tag.</summary>
    public const string EventType = "holder_change";

    /// <summary>Builds one holder_change event.</summary>
    /// <param name="tick">The tick the change landed on.</param>
    /// <param name="enemyId">The enemy whose holder changed.</param>
    /// <param name="holderId">The new holder seat, or null when cleared.</param>
    /// <returns>The event.</returns>
    public static SimEvent Event(int tick, int enemyId, int? holderId) =>
        new(tick, EventType,
            ("enemyId", JsValue.Int(enemyId)),
            ("holderId", holderId is { } h ? JsValue.Int(h) : JsValue.Null));

    /// <summary>The holder of every enemy, keyed by actor id, before a holder-writing step.</summary>
    /// <param name="state">The state to snapshot.</param>
    /// <returns>Enemy id to holder id (null when none).</returns>
    public static Dictionary<int, int?> Snapshot(SimState state) =>
        state.Actors.Values
            .Where(a => a.Kind == ActorKind.Enemy)
            .ToDictionary(a => a.Id, a => a.HolderId);

    /// <summary>The ids of every enemy that currently has a holder, ascending (the creation-time set the first step logs).</summary>
    /// <param name="state">The state to read.</param>
    /// <returns>The ids; empty when no enemy is held.</returns>
    public static IReadOnlyCollection<int> HeldIds(SimState state) =>
        state.Actors.Values.Where(a => a.Kind == ActorKind.Enemy && a.HolderId is not null).Select(a => a.Id).OrderBy(i => i).ToList();

    /// <summary>
    /// Drains the creation-time held set into the first step's events: one holder_change carrying the enemy's final
    /// holder this tick, skipped for an enemy that already has a holder_change in <paramref name="events"/> (that one is
    /// its final state, so it is logged once, never twice) or that no longer has a holder.
    /// </summary>
    /// <param name="state">The state after the step's targeting fixup.</param>
    /// <param name="pending">The creation-time held ids (null = nothing to drain).</param>
    /// <param name="events">The step's events so far.</param>
    /// <param name="emit">Event sink.</param>
    public static void DrainPending(SimState state, IReadOnlyCollection<int>? pending, IReadOnlyList<SimEvent> events, Action<SimEvent> emit)
    {
        if (pending is null || pending.Count == 0)
        {
            return;
        }
        var logged = events.Where(e => e.Type == EventType).Select(e => e.Get("enemyId")).OfType<JsInt>().Select(i => i.Value).ToHashSet();
        foreach (var id in pending)
        {
            if (!logged.Contains(id) && state.Actors.TryGetValue(id, out var enemy) && enemy.HolderId is { } holder)
            {
                emit(Event(state.Tick, id, holder));
            }
        }
    }

    /// <summary>Emits one holder_change for every enemy whose holder differs from <paramref name="before"/>.</summary>
    /// <param name="state">The state after the holder-writing step.</param>
    /// <param name="before">The snapshot taken before it (<see cref="Snapshot"/>).</param>
    /// <param name="emit">Event sink.</param>
    public static void EmitChanges(SimState state, IReadOnlyDictionary<int, int?> before, Action<SimEvent> emit)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(emit);
        foreach (var enemy in state.Actors.Values.Where(a => a.Kind == ActorKind.Enemy))
        {
            if (before.TryGetValue(enemy.Id, out var previous) && previous != enemy.HolderId)
            {
                emit(Event(state.Tick, enemy.Id, enemy.HolderId));
            }
        }
    }
}
