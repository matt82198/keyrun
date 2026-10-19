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

    /// <summary>
    /// True once pull <paramref name="pullIndex"/> has spawned: any of its spawn actor ids is in the state (actors are
    /// never removed, so a spawned pull stays spawned). An out-of-range index or an empty pull is never spawned.
    /// </summary>
    public static bool IsSpawned(SimState state, DungeonDef dungeon, int pullIndex)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(dungeon);
        if (pullIndex < 0 || pullIndex >= dungeon.Pulls.Count)
        {
            return false;
        }
        return dungeon.Pulls[pullIndex].Spawns.Any(s => state.Actors.ContainsKey(s.ActorId));
    }
}

/// <summary>
/// One walkable box of a <see cref="NavLayout"/> (STATE.md D18; no TS counterpart).
/// </summary>
/// <param name="Id">Box id; a room's id is what <c>room_enter</c> names.</param>
/// <param name="Box">The box, mm.</param>
/// <param name="IsRoom">True for a room (entering it emits <c>room_enter</c>), false for a corridor.</param>
/// <param name="GatePullId">The pull whose clear opens this box (forward-only), or null = always open.</param>
public sealed record NavBox(string Id, Combat.Bounds Box, bool IsRoom, string? GatePullId);

/// <summary>A pull's room-entry trigger: the pull spawns the first tick a living party member stands in <paramref name="Box"/>.</summary>
/// <param name="PullId">The pull id.</param>
/// <param name="Box">The trigger box, mm.</param>
public sealed record NavTrigger(string PullId, Combat.Bounds Box);

/// <summary>
/// The rooms-and-corridors layout a host arms a run with (<see cref="Runs.RunContent.Layout"/>; STATE.md D18,
/// design/ART-PLAN.md section 3). New behaviour with no TS oracle: a run without a layout (every oracle run) never reads
/// it. Walkable space is the open boxes; a pull spawns on room entry, never on the previous clear; a gated box opens
/// when its pull clears.
/// </summary>
public sealed record NavLayout
{
    /// <summary>Builds a layout, rejecting one the run rules cannot use.</summary>
    /// <param name="boxes">Rooms and corridors in route order (box k overlaps box k+1).</param>
    /// <param name="triggers">One trigger per pull.</param>
    /// <param name="route">One point per box: the doorway into the next box, the goal point for the last.</param>
    /// <exception cref="ArgumentException">No boxes, a route of another length, or two triggers for one pull.</exception>
    public NavLayout(IReadOnlyList<NavBox> boxes, IReadOnlyList<NavTrigger> triggers, IReadOnlyList<Vec3> route)
    {
        ArgumentNullException.ThrowIfNull(boxes);
        ArgumentNullException.ThrowIfNull(triggers);
        ArgumentNullException.ThrowIfNull(route);
        if (boxes.Count == 0)
        {
            throw new ArgumentException("a layout needs at least one box", nameof(boxes));
        }
        if (route.Count != boxes.Count)
        {
            throw new ArgumentException($"the route needs one point per box ({boxes.Count}), got {route.Count}", nameof(route));
        }
        if (triggers.Select(t => t.PullId).Distinct(StringComparer.Ordinal).Count() != triggers.Count)
        {
            throw new ArgumentException("two triggers name the same pull", nameof(triggers));
        }
        Boxes = boxes;
        Triggers = triggers;
        Route = route;
    }

    /// <summary>Rooms and corridors in route order.</summary>
    public IReadOnlyList<NavBox> Boxes { get; }

    /// <summary>Pull triggers.</summary>
    public IReadOnlyList<NavTrigger> Triggers { get; }

    /// <summary>One route point per box.</summary>
    public IReadOnlyList<Vec3> Route { get; }

    /// <summary>The XZ box around every box (the host's coarse arena clamp; the walkable rule does the rest).</summary>
    public Combat.Bounds Extent => new(
        Boxes.Min(b => b.Box.MinX), Boxes.Max(b => b.Box.MaxX), Boxes.Min(b => b.Box.MinZ), Boxes.Max(b => b.Box.MaxZ));

    /// <summary>The trigger for <paramref name="pullId"/>, or null.</summary>
    public NavTrigger? TriggerFor(string pullId) => Triggers.FirstOrDefault(t => string.Equals(t.PullId, pullId, StringComparison.Ordinal));

    /// <summary>RED STUB.</summary>
    public NavBox? RoomFor(string pullId) => null;

    /// <summary>RED STUB.</summary>
    public static Vec3 GateClamp(Combat.Bounds room, Vec3 pre, Vec3 post) => post;

    /// <summary>The highest-index box holding <paramref name="p"/> (open or not), or -1 when none does.</summary>
    public int BoxIndexAt(Vec3 p)
    {
        for (var i = Boxes.Count - 1; i >= 0; i--)
        {
            if (Combat.Movement.Contains(Boxes[i].Box, p))
            {
                return i;
            }
        }
        return -1;
    }

    /// <summary>
    /// True when <paramref name="box"/> is walkable on pull <paramref name="pullIndex"/> of <paramref name="dungeon"/>:
    /// it has no gate, its gate pull is not in the dungeon (a <c>--pull N</c> start drops earlier pulls, so their gates
    /// count as cleared), or its gate pull is behind the active pull.
    /// </summary>
    public static bool IsOpen(NavBox box, DungeonDef dungeon, int pullIndex)
    {
        ArgumentNullException.ThrowIfNull(box);
        ArgumentNullException.ThrowIfNull(dungeon);
        if (box.GatePullId is not { } gate)
        {
            return true;
        }
        for (var i = 0; i < dungeon.Pulls.Count; i++)
        {
            if (string.Equals(dungeon.Pulls[i].Id, gate, StringComparison.Ordinal))
            {
                return i < pullIndex;
            }
        }
        return true;
    }

    /// <summary>The open boxes on pull <paramref name="pullIndex"/>, in layout order.</summary>
    public IReadOnlyList<Combat.Bounds> OpenBoxes(DungeonDef dungeon, int pullIndex) =>
        Boxes.Where(b => IsOpen(b, dungeon, pullIndex)).Select(b => b.Box).ToList();

    /// <summary>Throws unless every pull of <paramref name="dungeon"/> has a trigger (fail closed: a pull without a room never spawns).</summary>
    /// <exception cref="ArgumentException">A pull has no trigger.</exception>
    public void RequireTriggers(DungeonDef dungeon)
    {
        ArgumentNullException.ThrowIfNull(dungeon);
        foreach (var pull in dungeon.Pulls)
        {
            if (TriggerFor(pull.Id) is null)
            {
                throw new ArgumentException($"layout has no room trigger for pull \"{pull.Id}\"", nameof(dungeon));
            }
        }
    }

    /// <summary>True when a living player actor stands in <paramref name="trigger"/>.</summary>
    public static bool PartyInside(SimState state, NavTrigger trigger)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(trigger);
        return state.Actors.Values.Any(a => a.Alive && a.Kind == ActorKind.Player && Combat.Movement.Contains(trigger.Box, a.Pos));
    }
}
