using System;
using System.Collections.Generic;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Encounter;

/// <summary>
/// One patrol pack of a layout run (MPLUS-PATROLS; layout.json <c>patrols</c>): its members stand idle on a point that
/// walks a closed loop of axis-aligned legs from tick 0. The point is a pure function of the tick (<see cref="Patrols.PointAt"/>),
/// so the patrol is never in two places and never depends on who arrived when.
/// </summary>
/// <param name="Id">Group id (shares the pull and pack namespace: <c>pack_engage</c> names it).</param>
/// <param name="Spawns">Members; each <see cref="PullSpawn.Pos"/> is the member's offset from the patrol's point, mm.</param>
/// <param name="Waypoints">The loop's corners, at least two (two = out and back), mm.</param>
/// <param name="SpeedMmPerTick">Walk speed while idle, mm per tick (positive).</param>
public sealed record PatrolPack(string Id, IReadOnlyList<PullSpawn> Spawns, IReadOnlyList<Vec3> Waypoints, long SpeedMmPerTick)
{
    /// <summary>Aggro radius, mm: the same rule and default as a prespawned idle pack (<see cref="Pulls.DefaultAggroMm"/>).</summary>
    public long AggroMm { get; init; } = Pulls.DefaultAggroMm;
}

/// <summary>
/// MPLUS-PATROLS (C#-only, layout runs; BIBLE section 9.1 "it does not move unless it is a patrol"; Matt 2026-10-20
/// "lean into m+ design heavily while in the dungeon"). A patrol stands idle like any prespawned group, but its point walks
/// the loop, so where it stands on a tick is exact integer mm. It engages on the shared aggro rule (<see cref="Aggro"/>) as
/// a pack with pull index -1: a party member in its ring at that tick's point, or a member taking damage. After that its
/// members are ordinary engaged enemies, so a patrol that walks into an ongoing fight joins it. Integers only, no randomness.
/// </summary>
public static class Patrols
{
    /// <summary>The length of <paramref name="patrol"/>'s closed loop, mm (the sum of its axis-aligned legs).</summary>
    /// <exception cref="ArgumentException">Fewer than two waypoints, a diagonal leg, or a zero-length loop (fail closed).</exception>
    public static long LoopMm(PatrolPack patrol)
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
    /// Where <paramref name="patrol"/>'s point stands on <paramref name="tick"/>: <c>tick * speed</c> mm along the loop from
    /// waypoint 0 (modulo the loop length), exact integer mm.
    /// </summary>
    /// <exception cref="ArgumentException">A loop <see cref="LoopMm"/> rejects, or a speed that is not positive.</exception>
    public static Vec3 PointAt(PatrolPack patrol, int tick)
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
    public static Vec3 MemberAt(PatrolPack patrol, PullSpawn spawn, int tick)
    {
        ArgumentNullException.ThrowIfNull(spawn);
        var p = PointAt(patrol, tick);
        return new Vec3(p.X + spawn.Pos.X, p.Y + spawn.Pos.Y, p.Z + spawn.Pos.Z);
    }

    /// <summary>Every patrol's members, idle, at their tick-0 places (merged into the run by the run root).</summary>
    /// <param name="patrols">The run's patrols, or null for none.</param>
    public static IReadOnlyList<ActorState> SpawnIdle(IReadOnlyList<PatrolPack>? patrols)
    {
        var actors = new List<ActorState>();
        if (patrols is null)
        {
            return actors;
        }
        foreach (var patrol in patrols)
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
    /// Walks every not-yet-engaged patrol to <paramref name="state"/>'s tick: each living idle member is put on
    /// <see cref="MemberAt"/> (mutated in place: the run root owns this freshly stepped state). An engaged patrol is left to
    /// the fight.
    /// </summary>
    /// <param name="state">The post-combat state of this tick.</param>
    /// <param name="patrols">The run's patrols, or null for none.</param>
    /// <param name="encounter">The encounter (its engaged set).</param>
    public static void Walk(SimState state, IReadOnlyList<PatrolPack>? patrols, EncounterState encounter)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(encounter);
        if (patrols is null)
        {
            return;
        }
        foreach (var patrol in patrols)
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

    private static long LegMm(PatrolPack patrol, Vec3 from, Vec3 to)
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
