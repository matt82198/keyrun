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

    /// <summary>
    /// echo-spawns.ts:35 stepEchoSpawns, then (lane 3F, no TS counterpart) the armed boss's post-combat step: this is
    /// the encounter's one post-combat seam in the run root, so an armed boss's Twins spawn and its Lock reads the
    /// tick's combos here. Unarmed (Boss null, every oracle run) the result is exactly stepEchoSpawns'.
    /// </summary>
    public static EchoSpawnResult Step(EncounterState encounter, SimState state, IEnumerable<SimEvent> events, int tick, Action<SimEvent> emit)
    {
        ArgumentNullException.ThrowIfNull(encounter);
        var materialized = events as IReadOnlyCollection<SimEvent> ?? events.ToList();
        // ADDS-DIE-WITH-BOSS (Matt 2026-10-20, BIBLE s9: the boss's death ends the encounter and any adds still alive
        // despawn, as in WoW): on the armed boss's death step no Echo add realises, the queue is dropped, and every
        // Echo add still standing dies on the boss's death tick. The Twins fall in BossRules.AfterCombat below.
        var bossDeath = encounter.Boss is { } armed
            ? materialized.FirstOrDefault(e => e.Type == "death" && e.Get("actorId") is JsInt id && id.Value == armed.Config.BossActorId)
            : null;
        var echo = bossDeath is null
            ? StepEcho(encounter, state, materialized, tick, emit)
            : EndEcho(encounter, state, bossDeath, encounter.Boss!.Config.BossActorId, emit);
        if (echo.Encounter.Boss is not { } boss)
        {
            return echo;
        }
        var post = BossRules.AfterCombat(boss, state, materialized, tick, emit);
        var spawned = post.Spawned.Count == 0 ? echo.Spawned : echo.Spawned.Concat(post.Spawned).ToList();
        return new EchoSpawnResult(echo.Encounter with { Boss = post.Boss }, spawned);
    }

    /// <summary>
    /// The boss died this step: drops every queued Echo request (none realises) and kills each Echo add still standing
    /// on the boss's death tick, with its own <c>death</c> event (<c>sourceId</c> the boss, <c>reason: "boss_death"</c>).
    /// The fallen adds return as Spawned (Health 0, not Alive) so the run root's merge replaces them by id.
    /// </summary>
    private static EchoSpawnResult EndEcho(EncounterState encounter, SimState state, SimEvent bossDeath, int bossActorId, Action<SimEvent> emit)
    {
        var fallen = new List<ActorState>();
        foreach (var add in state.Actors.Values)
        {
            if (add.Id < EchoActorIdBase || add.Kind != ActorKind.Enemy || !add.Alive)
            {
                continue;
            }
            var dead = add.Clone();
            dead.Health = 0;
            dead.Alive = false;
            fallen.Add(dead);
            emit(new SimEvent(bossDeath.Tick, "death",
                ("actorId", JsValue.Int(add.Id)),
                ("sourceId", JsValue.Int(bossActorId)),
                ("reason", JsValue.Str("boss_death"))));
            Combat.Damage.EndCastOnDeath(dead, emit, bossDeath.Tick);
        }
        return new EchoSpawnResult(encounter with { PendingEchoSpawns = Array.Empty<EchoSpawnRequest>() }, fallen);
    }

    private static EchoSpawnResult StepEcho(EncounterState encounter, SimState state, IEnumerable<SimEvent> events, int tick, Action<SimEvent> emit)
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
