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
    /// <summary>A group's aggro radius when content sets none, mm (BIBLE section 9.1: 4000).</summary>
    public const long DefaultAggroMm = 4000;

    /// <summary>
    /// PRESPAWN-AGGRO: the centre of <paramref name="pull"/>'s aggro ring, <see cref="PullDef.AggroCenter"/> or the
    /// integer mean (truncated toward zero) of its spawn points; the origin for a pull with no spawns.
    /// </summary>
    public static Vec3 RingCenter(PullDef pull)
    {
        ArgumentNullException.ThrowIfNull(pull);
        return pull.AggroCenter ?? MeanOf(pull.Spawns);
    }

    /// <summary>The integer mean of <paramref name="spawns"/>' points (truncated toward zero), or the origin when empty.</summary>
    public static Vec3 MeanOf(IReadOnlyList<PullSpawn> spawns)
    {
        ArgumentNullException.ThrowIfNull(spawns);
        if (spawns.Count == 0)
        {
            return Vec3.Zero;
        }
        long x = 0, y = 0, z = 0;
        foreach (var s in spawns)
        {
            x += s.Pos.X;
            y += s.Pos.Y;
            z += s.Pos.Z;
        }
        return new Vec3(x / spawns.Count, y / spawns.Count, z / spawns.Count);
    }

    /// <summary>
    /// PRESPAWN-AGGRO (BIBLE section 9.1): every pull's actors, idle (<see cref="ActorState.Idle"/>) at their spawn
    /// points, the boss pull included. Emits nothing: a group's <c>pull_start</c> comes on its engage tick.
    /// <para>
    /// A stack (k of a pull's spawns on one x and z, a content <c>count</c>) is placed as one row along x centred on that
    /// point, neighbours <c>2 * actorRadiusMm</c> apart, so no two bodies overlap. Push-out never shoves an idle body
    /// (Rules), so without this the stack would stand on one point until its engage and reach the party as one block.
    /// This is placement, once, before tick 0: nothing moves, nothing is logged, and the ring centre still comes from the
    /// content points (<see cref="RingCenter"/>). Integers in spawn order: deterministic.
    /// </para>
    /// </summary>
    /// <param name="dungeon">The (layout-placed) dungeon.</param>
    /// <param name="actorRadiusMm">The run's actor radius (<c>RunContent.ActorRadiusMm</c>); 0 or less leaves stacks as authored.</param>
    public static IReadOnlyList<ActorState> SpawnAllIdle(DungeonDef dungeon, long actorRadiusMm)
    {
        ArgumentNullException.ThrowIfNull(dungeon);
        var actors = new List<ActorState>();
        foreach (var pull in dungeon.Pulls)
        {
            foreach (var spawn in pull.Spawns)
            {
                var actor = SpawnActor(spawn);
                actor.Idle = true;
                actor.Pos = RowSlot(pull.Spawns, spawn, actorRadiusMm);
                actors.Add(actor);
            }
        }
        return actors;
    }

    // Where `spawn` stands in its stack's row: slot i of k at x + (2i - (k - 1)) * r, so neighbours stand exactly 2r
    // apart (the push-out distance) and the row is centred on the content point. A lone body stays on its point.
    private static Vec3 RowSlot(IReadOnlyList<PullSpawn> spawns, PullSpawn spawn, long radiusMm)
    {
        var k = 0;
        var i = 0;
        foreach (var s in spawns)
        {
            if (s.Pos.X != spawn.Pos.X || s.Pos.Z != spawn.Pos.Z)
            {
                continue;
            }
            if (s.ActorId == spawn.ActorId)
            {
                i = k;
            }
            k++;
        }
        if (k <= 1 || radiusMm <= 0)
        {
            return spawn.Pos;
        }
        return spawn.Pos with { X = spawn.Pos.X + (((2L * i) - (k - 1)) * radiusMm) };
    }

    /// <summary>True once group <paramref name="groupId"/> (a pull or pack id) has engaged.</summary>
    public static bool IsEngaged(EncounterState encounter, string groupId)
    {
        ArgumentNullException.ThrowIfNull(encounter);
        return encounter.Engaged.Contains(groupId, StringComparer.Ordinal);
    }

    /// <summary>True once pull <paramref name="pullIndex"/> has engaged; false for an out-of-range index.</summary>
    public static bool IsEngaged(EncounterState encounter, DungeonDef dungeon, int pullIndex)
    {
        ArgumentNullException.ThrowIfNull(dungeon);
        return pullIndex >= 0 && pullIndex < dungeon.Pulls.Count && IsEngaged(encounter, dungeon.Pulls[pullIndex].Id);
    }

    // pulls.ts:38 spawnActor: an untargeted enemy at full health, role none, no resource, tuning copied only when set.
    internal static ActorState SpawnActor(PullSpawn spawn) => new()
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
    /// D45 (the player chooses the route): first the index steps past every pull of a branch the party did not take
    /// (<see cref="PullDef.Branch"/> set and not the branch of the first branch pull in <see cref="EncounterState.Engaged"/>),
    /// with no event: those pulls are never fought. A dungeon with no branch pull behaves exactly as before.
    /// </summary>
    public static EncounterState CheckPullAdvance(SimState state, DungeonDef dungeon, EncounterState encounter, int tick, Action<SimEvent> emit)
    {
        if (encounter.PullIndex < 0 || encounter.PullIndex >= dungeon.Pulls.Count)
        {
            return encounter;
        }
        if (TakenBranch(dungeon, encounter) is { } taken)
        {
            var skip = encounter.PullIndex;
            while (skip < dungeon.Pulls.Count && dungeon.Pulls[skip].Branch is { } b && !string.Equals(b, taken, StringComparison.Ordinal))
            {
                skip++;
            }
            if (skip != encounter.PullIndex)
            {
                encounter = encounter with { PullIndex = skip };
                if (skip >= dungeon.Pulls.Count)
                {
                    return encounter;
                }
            }
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
    /// D45: the branch the party took, the <see cref="PullDef.Branch"/> of the first engaged pull that has one, or null
    /// while no branch pull has engaged (or the dungeon has none).
    /// </summary>
    /// <param name="dungeon">The dungeon.</param>
    /// <param name="encounter">The encounter.</param>
    /// <returns>The branch, or null.</returns>
    public static string? TakenBranch(DungeonDef dungeon, EncounterState encounter)
    {
        ArgumentNullException.ThrowIfNull(dungeon);
        ArgumentNullException.ThrowIfNull(encounter);
        foreach (var id in encounter.Engaged)
        {
            foreach (var p in dungeon.Pulls)
            {
                if (p.Branch is { } b && string.Equals(p.Id, id, StringComparison.Ordinal))
                {
                    return b;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// True once pull <paramref name="pullIndex"/> has spawned: any of its spawn actor ids is in the state (actors are
    /// never removed, so a spawned pull stays spawned). An out-of-range index or an empty pull is never spawned. On a
    /// layout run every pull is spawned, idle, from tick 0 (PRESPAWN-AGGRO): ask <see cref="IsEngaged(EncounterState, DungeonDef, int)"/>
    /// whether its fight has started.
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
/// <param name="GatePullId">The pull whose clear opens this box, or null = always open. The shipped layout gates no box
/// (PATH-TO-BOSS, STATE.md D44: one open path to the boss); the rule stays for a layout that asks for it.</param>
public sealed record NavBox(string Id, Combat.Bounds Box, bool IsRoom, string? GatePullId);

/// <summary>
/// A pull's trigger box. It names the pull's room (the room box holding <paramref name="Box"/>, <see cref="NavLayout.RoomFor"/>);
/// since PRESPAWN-AGGRO the pull engages on its aggro ring, not on this box, and since STATE.md D44 no gate shuts on it.
/// </summary>
/// <param name="PullId">The pull id.</param>
/// <param name="Box">The trigger box, mm.</param>
public sealed record NavTrigger(string PullId, Combat.Bounds Box);

/// <summary>
/// The rooms-and-corridors layout a host arms a run with (<see cref="Runs.RunContent.Layout"/>; STATE.md D18,
/// design/ART-PLAN.md section 3). New behaviour with no TS oracle: a run without a layout (every oracle run) never reads
/// it. Walkable space is the open boxes (a gated box, which the shipped layout no longer has, opens when its pull
/// clears); every pull stands idle from tick 0 and engages on its aggro ring (PRESPAWN-AGGRO), never on the previous clear.
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

    /// <summary>
    /// R-PATH-1: blocking boxes inside the walkable space (walls and props). Actors path around them
    /// (<see cref="Nav.NavGrid"/>) and the layout step never lets a move end strictly inside one
    /// (<see cref="Nav.NavFollow.SlideOffObstacles"/>). The content layout carries none today, so this is empty on every
    /// shipped run.
    /// </summary>
    public IReadOnlyList<Combat.Bounds> Obstacles { get; init; } = Array.Empty<Combat.Bounds>();

    /// <summary>
    /// D45 (the player chooses the route): the walkable boxes of a seeded dungeon's other branch, beside the route chain
    /// <see cref="Boxes"/>. They are walked (grid, clamp, room_enter, pull rooms) but carry no route point, so the route
    /// follow (box index, <see cref="Route"/>) stays the chain. Never gated. Empty on the hand-built layout.
    /// </summary>
    public IReadOnlyList<NavBox> Side { get; init; } = Array.Empty<NavBox>();

    /// <summary>Every walkable box: the route chain, then <see cref="Side"/>.</summary>
    public IEnumerable<NavBox> AllBoxes => Side.Count == 0 ? Boxes : Boxes.Concat(Side);

    /// <summary>True when some box of the chain or the side holds <paramref name="p"/>.</summary>
    /// <param name="p">The point.</param>
    /// <returns>True when walkable space.</returns>
    public bool InAnyBox(Vec3 p) => AllBoxes.Any(b => Combat.Movement.Contains(b.Box, p));

    /// <summary>The XZ box around every box (the host's coarse arena clamp; the walkable rule does the rest).</summary>
    public Combat.Bounds Extent => new(
        AllBoxes.Min(b => b.Box.MinX), AllBoxes.Max(b => b.Box.MaxX), AllBoxes.Min(b => b.Box.MinZ), AllBoxes.Max(b => b.Box.MaxZ));

    /// <summary>The trigger for <paramref name="pullId"/>, or null.</summary>
    public NavTrigger? TriggerFor(string pullId) => Triggers.FirstOrDefault(t => string.Equals(t.PullId, pullId, StringComparison.Ordinal));

    /// <summary>
    /// The room of <paramref name="pullId"/> (STATE.md D27): the first room box that holds the pull's whole trigger box,
    /// or null when the pull has no trigger or no room holds it (<see cref="RequireTriggers"/> rejects that layout).
    /// </summary>
    /// <param name="pullId">The pull id.</param>
    /// <returns>The room box, or null.</returns>
    public NavBox? RoomFor(string pullId)
    {
        if (TriggerFor(pullId) is not { } trigger)
        {
            return null;
        }
        var t = trigger.Box;
        return AllBoxes.FirstOrDefault(b => b.IsRoom &&
            b.Box.MinX <= t.MinX && t.MaxX <= b.Box.MaxX && b.Box.MinZ <= t.MinZ && t.MaxZ <= b.Box.MaxZ);
    }


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

    /// <summary>The open boxes on pull <paramref name="pullIndex"/>, in layout order, then every <see cref="Side"/> box.</summary>
    public IReadOnlyList<Combat.Bounds> OpenBoxes(DungeonDef dungeon, int pullIndex) =>
        Boxes.Where(b => IsOpen(b, dungeon, pullIndex)).Concat(Side).Select(b => b.Box).ToList();

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
            if (RoomFor(pull.Id) is null)
            {
                throw new ArgumentException($"layout has no room box holding the trigger of pull \"{pull.Id}\"", nameof(dungeon));
            }
        }
    }

    /// <summary>
    /// True when the living party is inside the pull's room (STATE.md D27): at least one living player actor, and every
    /// living player actor stands in <paramref name="room"/> (the pull's room box, <see cref="RoomFor"/>). Dead players and
    /// the actors of <paramref name="openSeats"/> (abandoned, unfilled seats) are ignored.
    /// </summary>
    /// <param name="state">The state.</param>
    /// <param name="room">The pull's room box.</param>
    /// <param name="openSeats">Open seats' actor ids, or null.</param>
    public static bool PartyInside(SimState state, Combat.Bounds room, IReadOnlyList<int>? openSeats = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        var anyLiving = false;
        foreach (var a in state.Actors.Values)
        {
            if (!a.Alive || a.Kind != ActorKind.Player || (openSeats is not null && openSeats.Contains(a.Id)))
            {
                continue;
            }
            if (!Combat.Movement.Contains(room, a.Pos))
            {
                return false;
            }
            anyLiving = true;
        }
        return anyLiving;
    }
}
