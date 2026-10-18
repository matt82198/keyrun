using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Encounter;

/// <summary>One Echo step's output (echo-spawns.ts:30 EchoSpawnResult).</summary>
/// <param name="Encounter">The encounter with the queue and next id updated.</param>
/// <param name="Spawned">Adds realised this tick, for the caller to merge into state.</param>
public sealed record EchoSpawnResult(EncounterState Encounter, IReadOnlyList<ActorState> Spawned);

/// <summary>
/// Echo (BIBLE s8) spawn machinery (sim/encounter/echo-spawns.ts). Called after combat resolves the tick: queues
/// one delayed weak add per interrupt event (<see cref="Clauses.PendingEchoSpawns"/>) and realises every due request
/// as an enemy actor at its source's position (the origin when the source is gone), emitting <c>echo_spawn</c>.
/// </summary>
public static class EchoSpawns
{
    /// <summary>First Echo add id, far above any dungeon or party id (echo-spawns.ts:28).</summary>
    public const int EchoActorIdBase = 1_000_000;

    /// <summary>echo-spawns.ts:35 stepEchoSpawns.</summary>
    public static EchoSpawnResult Step(EncounterState encounter, SimState state, IEnumerable<SimEvent> events, int tick, Action<SimEvent> emit)
    {
        var queued = encounter.PendingEchoSpawns.Concat(Clauses.PendingEchoSpawns(events, encounter.Rules)).ToList();
        var due = queued.Where(r => r.SpawnAtTick <= tick).ToList();
        var remaining = queued.Where(r => r.SpawnAtTick > tick).ToList();
        if (due.Count == 0)
        {
            return new EchoSpawnResult(encounter with { PendingEchoSpawns = remaining }, Array.Empty<ActorState>());
        }

        var nextId = encounter.NextEchoActorId;
        var spawned = new List<ActorState>();
        foreach (var req in due)
        {
            var id = nextId;
            nextId += 1;
            var origin = state.Actors.TryGetValue(req.SourceActorId, out var source) ? source.Pos : Vec3.Zero;
            // sim/combat/actors.ts:19 createActor({id, kind:"enemy", classId:"echo-add", role:"none", pos, maxHealth, maxResource:0}).
            spawned.Add(new ActorState
            {
                Id = id,
                Kind = ActorKind.Enemy,
                ClassId = "echo-add",
                Role = ActorRole.None,
                Pos = origin,
                Yaw = 0,
                Health = req.MaxHealth,
                MaxHealth = req.MaxHealth,
                Resource = 0,
                MaxResource = 0,
                Alive = true,
            });
            emit(new SimEvent(tick, "echo_spawn",
                ("actorId", JsValue.Int(id)),
                ("sourceActorId", JsValue.Int(req.SourceActorId)),
                ("maxHealth", JsValue.Int(req.MaxHealth))));
        }
        return new EchoSpawnResult(encounter with { PendingEchoSpawns = remaining, NextEchoActorId = nextId }, spawned);
    }
}
