using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Encounter;

/// <summary>
/// Spawns a pull's enemies and advances the pull index once every enemy of the active pull is dead
/// (sim/encounter/pulls.ts). Pure: <see cref="SpawnPull"/> returns new actors for the caller to merge; neither
/// function mutates its inputs.
/// </summary>
public static class Pulls
{
    // pulls.ts:38 spawnActor: an untargeted enemy at full health, role none, no resource, tuning copied only when set.
    private static ActorState SpawnActor(PullSpawn spawn) => new()
    {
        Id = spawn.ActorId,
        Kind = ActorKind.Enemy,
        ClassId = spawn.ClassId,
        Role = ActorRole.None,
        Pos = spawn.Pos,
        Yaw = spawn.Yaw,
        Health = spawn.Health,
        MaxHealth = spawn.Health,
        Resource = 0,
        MaxResource = 0,
        Alive = true,
        TargetId = null,
        CastId = null,
        CastEndsTick = null,
        CastStartTick = null,
        GcdReadyTick = 0,
        SpeedMmPerTick = spawn.SpeedMmPerTick,
        ChaseMarginMm = spawn.ChaseMarginMm,
    };

    /// <summary>
    /// pulls.ts:64 spawnPull: the pull's actors, after emitting <c>{tick, type:"pull_start", pullIndex, pullId, actorIds}</c>;
    /// an out-of-range index spawns nothing and emits nothing.
    /// </summary>
    public static IReadOnlyList<ActorState> SpawnPull(DungeonDef dungeon, int pullIndex, int tick, Action<SimEvent> emit)
    {
        if (pullIndex < 0 || pullIndex >= dungeon.Pulls.Count)
        {
            return Array.Empty<ActorState>();
        }
        var pull = dungeon.Pulls[pullIndex];
        emit(new SimEvent(tick, "pull_start",
            ("pullIndex", JsValue.Int(pullIndex)),
            ("pullId", JsValue.Str(pull.Id)),
            ("actorIds", JsValue.Arr(pull.Spawns.Select(s => JsValue.Int(s.ActorId))))));
        return pull.Spawns.Select(SpawnActor).ToList();
    }

    /// <summary>
    /// pulls.ts:82 checkPullAdvance: when the active pull has spawns and every one is dead or absent, emits
    /// <c>{tick, type:"pull_clear", pullIndex, pullId}</c> and returns the encounter on the next pull.
    /// </summary>
    public static EncounterState CheckPullAdvance(SimState state, DungeonDef dungeon, EncounterState encounter, int tick, Action<SimEvent> emit)
    {
        if (encounter.PullIndex < 0 || encounter.PullIndex >= dungeon.Pulls.Count)
        {
            return encounter;
        }
        var pull = dungeon.Pulls[encounter.PullIndex];
        if (pull.Spawns.Count == 0)
        {
            return encounter;
        }
        var allDead = pull.Spawns.All(s => !state.Actors.TryGetValue(s.ActorId, out var a) || !a.Alive);
        if (!allDead)
        {
            return encounter;
        }
        emit(new SimEvent(tick, "pull_clear", ("pullIndex", JsValue.Int(encounter.PullIndex)), ("pullId", JsValue.Str(pull.Id))));
        return encounter with { PullIndex = encounter.PullIndex + 1 };
    }
}
