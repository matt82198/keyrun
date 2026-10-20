using System;
using System.Collections.Generic;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Encounter;

/// <summary>
/// MPLUS-PATROLS (C#-only, layout runs; BIBLE section 9.1 "it does not move unless it is a patrol"; Matt 2026-10-20
/// "lean into m+ design heavily while in the dungeon"). A patrol pack stands idle like any prespawned group, but its
/// point walks a closed loop of axis-aligned legs from tick 0, so where it stands is a pure function of the tick: integer
/// mm, no randomness, no dependence on who arrived when. It engages on the shared aggro rule (<see cref="Aggro"/>): a
/// party member in its ring at that tick's point, or a member taking damage; after that its members are ordinary engaged
/// enemies. A patrol that walks into an ongoing fight therefore joins it (WoW's accidental double pull).
/// </summary>
public static class Patrols
{
    /// <summary>The length of <paramref name="patrol"/>'s closed loop, mm (the sum of its axis-aligned legs).</summary>
    /// <exception cref="ArgumentException">Fewer than two waypoints, a diagonal leg, or a zero-length loop (fail closed).</exception>
    public static long LoopMm(PatrolDef patrol)
    {
        ArgumentNullException.ThrowIfNull(patrol);
        var w = patrol.Waypoints;
        if (w.Count < 2)
        {
            throw new ArgumentException($"patrol \"{patrol.Id}\" needs at least two waypoints, got {w.Count}", nameof(patrol));
        }
        long total = 0;
        for (var i = 0; i < w.Count; i++)
        {
            total += LegMm(patrol, w[i], w[(i + 1) % w.Count]);
        }
        if (total <= 0)
        {
            throw new ArgumentException($"patrol \"{patrol.Id}\" has a zero-length loop", nameof(patrol));
        }
        return total;
    }

    /// <summary>
    /// Where <paramref name="patrol"/>'s point stands on <paramref name="tick"/>: <c>tick * speed</c> mm along the loop
    /// from waypoint 0 (modulo the loop length), integer and exact.
    /// </summary>
    /// <exception cref="ArgumentException">A loop <see cref="LoopMm"/> rejects, or a speed that is not positive.</exception>
    public static Vec3 PointAt(PatrolDef patrol, int tick)
    {
        ArgumentNullException.ThrowIfNull(patrol);
        if (patrol.SpeedMmPerTick <= 0)
        {
            throw new ArgumentException($"patrol \"{patrol.Id}\" speed must be positive, got {patrol.SpeedMmPerTick}", nameof(patrol));
        }
        var loop = LoopMm(patrol);
        var d = (Math.Max(0, tick) * patrol.SpeedMmPerTick) % loop;
        var w = patrol.Waypoints;
        for (var i = 0; i < w.Count; i++)
        {
            var from = w[i];
            var to = w[(i + 1) % w.Count];
            var leg = LegMm(patrol, from, to);
            if (d < leg)
            {
                return new Vec3(from.X + (Math.Sign(to.X - from.X) * d), from.Y, from.Z + (Math.Sign(to.Z - from.Z) * d));
            }
            d -= leg;
        }
        return w[0];
    }

    /// <summary>Where member <paramref name="spawn"/> of <paramref name="patrol"/> stands on <paramref name="tick"/>: the point plus its offset.</summary>
    public static Vec3 MemberAt(PatrolDef patrol, PullSpawn spawn, int tick)
    {
        ArgumentNullException.ThrowIfNull(spawn);
        var p = PointAt(patrol, tick);
        return new Vec3(p.X + spawn.Pos.X, p.Y + spawn.Pos.Y, p.Z + spawn.Pos.Z);
    }

    /// <summary>Every patrol's members, idle, at their tick-0 places (merged with the pulls by <see cref="Pulls.SpawnAllIdle"/>).</summary>
    public static IReadOnlyList<ActorState> SpawnIdle(DungeonDef dungeon)
    {
        ArgumentNullException.ThrowIfNull(dungeon);
        var actors = new List<ActorState>();
        foreach (var patrol in dungeon.Patrols)
        {
            foreach (var spawn in patrol.Spawns)
            {
                var actor = Pulls.SpawnActor(spawn);
                actor.Idle = true;
                actor.Pos = MemberAt(patrol, spawn, 0);
                actors.Add(actor);
            }
        }
        return actors;
    }

    /// <summary>
    /// Walks every not-yet-engaged patrol of <paramref name="dungeon"/> to <paramref name="state"/>'s tick: each living
    /// idle member is put on <see cref="MemberAt"/> (mutated in place: the run root owns this freshly stepped state).
    /// An engaged patrol is left to the fight.
    /// </summary>
    public static void Walk(SimState state, DungeonDef dungeon, EncounterState encounter)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(dungeon);
        ArgumentNullException.ThrowIfNull(encounter);
        foreach (var patrol in dungeon.Patrols)
        {
            if (Pulls.IsEngaged(encounter, patrol.Id))
            {
                continue;
            }
            foreach (var spawn in patrol.Spawns)
            {
                if (state.Actors.TryGetValue(spawn.ActorId, out var a) && a.Alive && a.Idle)
                {
                    a.Pos = MemberAt(patrol, spawn, state.Tick);
                }
            }
        }
    }

    private static long LegMm(PatrolDef patrol, Vec3 from, Vec3 to)
    {
        var dx = Math.Abs(to.X - from.X);
        var dz = Math.Abs(to.Z - from.Z);
        if (dx != 0 && dz != 0)
        {
            throw new ArgumentException($"patrol \"{patrol.Id}\" leg {from} -> {to} is not axis-aligned", nameof(patrol));
        }
        return dx + dz;
    }
}
