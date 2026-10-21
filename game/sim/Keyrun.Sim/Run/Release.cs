using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Runs;

/// <summary>Where a seat stood when the run placed it (its respawn point): position and facing.</summary>
/// <param name="Pos">Position, mm.</param>
/// <param name="Yaw">Facing, 1/1024 turn.</param>
public sealed record SeatSpawn(Vec3 Pos, int Yaw);

/// <summary>
/// WIPE-IS-RELEASE (ruling R-BACKFILL-2, finding f-47a7a6db; design/BIBLE.md sections 8 and 9.1; WoW Mythic+: a wipe
/// is a release and a run-back, not a run end). On a layout run every dead seat that is in the run releases and,
/// <see cref="RunWiring.ReleaseDelayTicks"/> after its death, respawns at full health where the run placed it (the
/// dungeon entrance, <see cref="Run.Spawns"/>), with a <c>{tick, type:"respawn", actorId}</c> event. On a full wipe
/// every seat does this. The run timer keeps counting through the release and the run back; D46's death and wipe time
/// (<see cref="RunWiring.ApplyDeathsAndWipe"/>) is the whole penalty, and nothing else is charged here.
/// An open (abandoned or unfilled) seat is not in the run and is never released. A run without a layout (every frozen
/// TS oracle run) never releases: it keeps the TS rule, a lasting wipe finishing the run depleted (sim/run-wiring.ts:178).
/// </summary>
public static class Release
{
    /// <summary>Every player actor's placement in <paramref name="state"/> (the run's start), by actor id.</summary>
    /// <param name="state">The freshly created run state.</param>
    /// <returns>The respawn points.</returns>
    public static IReadOnlyDictionary<int, SeatSpawn> SpawnsOf(SimState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.Actors.Values
            .Where(a => a.Kind == ActorKind.Player)
            .ToDictionary(a => a.Id, a => new SeatSpawn(a.Pos, a.Yaw));
    }

    /// <summary>
    /// One release step on the post-tick state: each pending seat whose respawn tick has come respawns (ascending actor
    /// id), then each dead seat not yet pending is given its respawn tick (<paramref name="next"/>'s tick plus
    /// <see cref="RunWiring.ReleaseDelayTicks"/>). Unchanged for a run without a layout.
    /// </summary>
    /// <param name="run">The run before the step (its content, spawns and pending releases).</param>
    /// <param name="next">The post-tick state.</param>
    /// <param name="openSeats">Abandoned or unfilled seats' actor ids, or null.</param>
    /// <param name="emit">Event sink.</param>
    /// <returns>The state with any respawned seats, and the pending releases (actor id to respawn tick).</returns>
    public static (SimState State, IReadOnlyDictionary<int, int> Pending) Step(Run run, SimState next, IReadOnlyList<int>? openSeats, Action<SimEvent> emit)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(emit);
        if (run.Content.Layout is null)
        {
            return (next, run.Releases);
        }
        var tick = next.Tick;
        var pending = new SortedDictionary<int, int>();
        foreach (var (id, at) in run.Releases)
        {
            if (next.Actors.TryGetValue(id, out var a) && !a.Alive && !IsOpen(openSeats, id))
            {
                pending[id] = at;
            }
        }

        var respawned = new List<ActorState>();
        foreach (var (id, at) in pending.ToList())
        {
            if (at > tick || run.Spawns is not { } spawns || !spawns.TryGetValue(id, out var spawn))
            {
                continue;
            }
            respawned.Add(Respawn(next.Actors[id], spawn, run.Content));
            pending.Remove(id);
            emit(new SimEvent(tick, "respawn", ("actorId", JsValue.Int(id))));
        }

        foreach (var a in next.Actors.Values)
        {
            if (a.Kind == ActorKind.Player && !a.Alive && !IsOpen(openSeats, a.Id) && !pending.ContainsKey(a.Id)
                && !respawned.Any(r => r.Id == a.Id))
            {
                pending[a.Id] = tick + RunWiring.ReleaseDelayTicks;
            }
        }
        return (RunWiring.MergeActors(next, respawned), pending);
    }

    private static bool IsOpen(IReadOnlyList<int>? openSeats, int id) => openSeats is not null && openSeats.Contains(id);

    // A released seat comes back as the run placed it: full health, its class's starting resource, at its spawn point,
    // with no cast, queued press, target, effect or threat. Cooldowns, gear and armor are kept.
    private static ActorState Respawn(ActorState dead, SeatSpawn spawn, RunContent content)
    {
        var back = dead.Clone();
        back.Alive = true;
        back.Health = back.MaxHealth;
        back.Resource = Actors.StartingResource(back.ClassId, back.MaxResource, content.Overheat);
        back.Pos = spawn.Pos;
        back.Yaw = spawn.Yaw;
        back.TargetId = null;
        back.CastId = null;
        back.CastEndsTick = null;
        back.CastStartTick = null;
        back.CastIsOverheat = false;
        back.CastTargetPinned = false;
        back.CastTargetId = null;
        back.QueuedCast = null;
        back.Effects = new List<EffectInstance>();
        back.Threat = new SortedDictionary<int, long>();
        return back;
    }
}
