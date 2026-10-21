using System;
using System.Collections.Generic;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Combat;

/// <summary>
/// The spell queue window (PR #370; twin of sim/combat/spell-queue.ts). A player's ability is never usable mid-cast
/// (Casting.StartError "casting") nor, unless its own gcdTicks is 0, during the GCD ("not_ready"). WoW softens both
/// with a queue: a press made inside the last <see cref="WindowTicks"/> ticks of the lock that refused it is held on
/// the actor (<see cref="ActorState.QueuedCast"/>) and fires on the first tick the lock is over. One press is held per
/// actor and the latest wins; a press earlier than the window is dropped, as before. Enemies never queue (their
/// scripts drive their casts).
/// <para>
/// Firing: on a tick the lock is over and the actor has no fresh cast command of its own, the held press runs as
/// that actor's cast command, in the tick's fixed (actorId, kind, insertion) order (<see cref="Tick.Sort"/>). So a
/// queued kick lands on the tick it fires and races any other kick on that tick by actor id, never by when its key
/// went down (BIBLE s3). A fresh cast command on the firing tick supersedes the held press (the latest wins). A held
/// press that then fails (out of range, a dead target, no resource) is dropped, never retried.
/// </para>
/// </summary>
public static class SpellQueue
{
    /// <summary>WoW's default spell queue window, 400 ms, in ticks at <see cref="Tick.TickMs"/> (8 at 20 Hz).</summary>
    public static readonly int WindowTicks = (int)IntMath.RoundHalfUp(400, Tick.TickMs);

    /// <summary>
    /// The tick the lock on <paramref name="def"/> ends for <paramref name="actor"/>: the later of the cast under way's
    /// end and (unless the ability is off the GCD) the GCD's ready tick; <paramref name="now"/> or earlier when nothing
    /// locks it.
    /// </summary>
    /// <param name="actor">The presser.</param>
    /// <param name="def">The pressed ability.</param>
    /// <param name="now">Current tick.</param>
    /// <returns>The first tick the cast and the GCD no longer block the press.</returns>
    public static int LockEndsTick(ActorState actor, AbilityDef def, int now)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(def);
        var end = now;
        if (actor.CastId is not null)
        {
            end = Math.Max(end, actor.CastEndsTick ?? int.MaxValue);
        }
        if (def.GcdTicks > 0)
        {
            end = Math.Max(end, actor.GcdReadyTick);
        }
        return end;
    }

    /// <summary>
    /// True when a press of <paramref name="def"/> refused at <paramref name="now"/> with <paramref name="error"/> is
    /// queued: a living player refused only by the cast under way or the GCD, inside the last
    /// <see cref="WindowTicks"/> ticks of that lock.
    /// </summary>
    /// <param name="actor">The presser.</param>
    /// <param name="def">The pressed ability.</param>
    /// <param name="error">StartCast's refusal.</param>
    /// <param name="now">Current tick.</param>
    /// <returns>True when the press waits in the queue.</returns>
    public static bool Queues(ActorState actor, AbilityDef def, string error, int now)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (actor.Kind != ActorKind.Player || !actor.Alive || (error != "casting" && error != "not_ready"))
        {
            return false;
        }
        var wait = (long)LockEndsTick(actor, def, now) - now;
        return wait > 0 && wait <= WindowTicks;
    }

    /// <summary>
    /// This tick's commands with each held press that fires now added as its actor's cast command, re-sorted in the
    /// fixed order. A held press fires when its actor is alive, sent no cast command this tick, and its lock is over.
    /// A dead actor's held press is dropped.
    /// </summary>
    /// <param name="state">Sim state (casts due this tick already completed).</param>
    /// <param name="commands">This tick's sorted commands.</param>
    /// <param name="abilities">The run's abilities.</param>
    /// <param name="fired">Receives the commands that came from the queue.</param>
    /// <returns>The commands to run, in order.</returns>
    public static IReadOnlyList<Command> WithFiring(
        SimState state,
        IReadOnlyList<Command> commands,
        IReadOnlyDictionary<string, AbilityDef> abilities,
        HashSet<Command> fired)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(abilities);
        ArgumentNullException.ThrowIfNull(fired);
        List<Command>? merged = null;
        foreach (var actor in state.Actors.Values)
        {
            if (actor.QueuedCast is not { } q)
            {
                continue;
            }
            if (!actor.Alive || !abilities.TryGetValue(q.AbilityId, out var def))
            {
                actor.QueuedCast = null;
                continue;
            }
            if (HasCast(commands, actor.Id) || LockEndsTick(actor, def, state.Tick) > state.Tick)
            {
                continue;
            }
            merged ??= new List<Command>(commands);
            var cmd = new CastCommand(state.Tick, actor.Id, q.AbilityId, q.TargetId, q.Point);
            merged.Add(cmd);
            fired.Add(cmd);
        }
        return merged is null ? commands : Tick.Sort(merged);
    }

    /// <summary>
    /// Updates <paramref name="actor"/>'s held press after a cast command of <paramref name="def"/> ran at
    /// <paramref name="now"/> with StartCast's <paramref name="error"/>. A started cast, or a held press that fired,
    /// clears it. A fresh press inside the window replaces it (the latest wins). A fresh press refused once its lock is
    /// over supersedes it and clears it. A fresh press refused earlier than the window leaves it held.
    /// </summary>
    /// <param name="actor">The presser.</param>
    /// <param name="def">The pressed ability.</param>
    /// <param name="cmd">The cast command.</param>
    /// <param name="error">StartCast's result (null on success).</param>
    /// <param name="fromQueue">True when <paramref name="cmd"/> is a held press firing.</param>
    /// <param name="now">Current tick.</param>
    public static void AfterPress(ActorState actor, AbilityDef def, CastCommand cmd, string? error, bool fromQueue, int now)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(cmd);
        if (error is null || fromQueue)
        {
            actor.QueuedCast = null;
        }
        else if (Queues(actor, def, error, now))
        {
            actor.QueuedCast = new QueuedPress(cmd.AbilityId, cmd.TargetId, cmd.Point, now);
        }
        else if (LockEndsTick(actor, def, now) <= now)
        {
            actor.QueuedCast = null;
        }
    }

    private static bool HasCast(IReadOnlyList<Command> commands, int actorId)
    {
        foreach (var c in commands)
        {
            if (c is CastCommand && c.ActorId == actorId)
            {
                return true;
            }
        }
        return false;
    }
}
