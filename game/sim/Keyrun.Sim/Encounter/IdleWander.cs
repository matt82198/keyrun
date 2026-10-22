using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;
using Keyrun.Sim.Nav;

namespace Keyrun.Sim.Encounter;

/// <summary>
/// IDLE-WANDER (C#-only, layout runs with <see cref="Runs.RunContent.IdleWander"/>; finding f-c1044aa4, extends f-f0c886eb
/// and R-PATROL-4; WoW Mythic+, where a pack that has not seen you still mills about its post). Matt 2026-10-22, verbatim:
/// "the mobs need to move around in their aggro area while not aggro'd, or they look harmless".
/// <para>
/// An un-engaged pack or pull (never the boss, never the room-gated Seed, never a patrol, which keeps its route) walks as a
/// formation: each member keeps its own spot in the pack (its <see cref="ActorState.WanderAnchor"/>, where it stood when the
/// wander began) and the whole pack shifts by one offset that follows a closed loop of up to three points through the pack's
/// own aggro ring, pausing <see cref="PauseTicks"/> at each. The loop is a pure integer function of (run seed, first member
/// id, anchors, ring layout) and of the ticks since the wander began (<see cref="ActorState.WanderEpoch"/>): no clock, no
/// stored randomness, the same seed walks the same loop every run. Every point and every leg keeps every member inside the
/// pack's own ring, outside every other group's ring and on walkable floor, so wandering never pulls a second pack.
/// </para>
/// <para>
/// Aggro never moves with the wander. The pack ring is fixed, and an enemy's personal sight and assist radii are measured
/// from its anchor (<see cref="EnemyAggro"/>), so a wandering enemy wakes its pack on exactly the tick a standing one does.
/// The wander ends the instant the group engages (its members stop being idle) or leashes home (the anchor clears, and
/// the pack wanders again from where it reset).
/// </para>
/// </summary>
public static class IdleWander
{
    /// <summary>Walk speed between points, mm per tick (a slow patrol-guard stroll, 1 m/s at 20 Hz).</summary>
    public const long SpeedMmPerTick = 50;

    /// <summary>Ticks a pack stands at each point of its loop.</summary>
    public const int PauseTicks = 40;

    /// <summary>The farthest a point may sit from the anchor on either axis, mm.</summary>
    public const long ReachMm = 2000;

    /// <summary>Clearance kept from the own ring's edge and from every other ring, mm.</summary>
    public const long MarginMm = 50;

    private const int Points = 3;
    private const int Tries = 24;
    private const long SampleMm = 100;

    /// <summary>One closed loop: the offsets from the anchors (the first is zero) and the ticks of each leg.</summary>
    public sealed record Loop(IReadOnlyList<Vec3> Offsets, IReadOnlyList<int> LegTicks)
    {
        /// <summary>Ticks of one full lap: a pause at every point plus every leg.</summary>
        public int Period => (Offsets.Count * PauseTicks) + LegTicks.Sum();
    }

    /// <summary>True for the boss pull id and for an earlier segment's boss pull (<c>boss-</c> prefix; R-MULTIBOSS-1).</summary>
    /// <param name="groupId">The group id.</param>
    /// <returns>True for a boss group.</returns>
    public static bool IsBossGroup(string groupId) =>
        string.Equals(groupId, "boss", StringComparison.Ordinal) || groupId.StartsWith("boss-", StringComparison.Ordinal);

    /// <summary>
    /// Walks every un-engaged wandering group to <paramref name="state"/>'s tick, in place (the run root owns this freshly
    /// stepped state): starts a wander for members that have none (anchor = where they stand), then puts each living idle
    /// member on its anchor plus the pack offset of this tick, facing the way it walks.
    /// </summary>
    /// <param name="state">The post-combat state of this tick.</param>
    /// <param name="groups">Every static group (pulls and packs); patrols excluded by the caller.</param>
    /// <param name="encounter">The encounter (its engaged set).</param>
    /// <param name="grid">The layout grid (walkable floor).</param>
    public static void Step(SimState state, IReadOnlyList<AggroGroup> groups, EncounterState encounter, NavGrid grid)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(encounter);
        ArgumentNullException.ThrowIfNull(grid);
        foreach (var g in groups)
        {
            if (IsBossGroup(g.Id) || g.ActivateRoom is not null || g.ActorIds.Count == 0 || Pulls.IsEngaged(encounter, g.Id) || EnemyAggro.Evading(state, g))
            {
                continue;
            }
            var members = g.ActorIds
                .Select(id => state.Actors.TryGetValue(id, out var a) ? a : null)
                .Where(a => a is { Kind: ActorKind.Enemy, Alive: true, Idle: true, Dormant: false, Evading: false, CastId: null })
                .Select(a => a!)
                .ToList();
            if (members.Count == 0)
            {
                continue;
            }
            if (members.Any(m => m.WanderAnchor is null))
            {
                Begin(state, g, members, groups, grid);
            }
            foreach (var m in members)
            {
                if (m.WanderLoop is { Offsets.Count: > 1 } loop && m.WanderAnchor is { } anchor)
                {
                    if (state.Tick > m.WanderEpoch)
                    {
                        // Something else moved this enemy since the last tick (a placement, a shove): the anchor goes with it,
                        // so the wander never snaps it back.
                        var (was, _) = OffsetAt(loop, state.Tick - 1 - m.WanderEpoch);
                        var expected = new Vec3(anchor.X + was.X, anchor.Y, anchor.Z + was.Z);
                        if (m.Pos != expected)
                        {
                            anchor = new Vec3(m.Pos.X - was.X, m.Pos.Y, m.Pos.Z - was.Z);
                            m.WanderAnchor = anchor;
                        }
                    }
                    var (offset, yaw) = OffsetAt(loop, state.Tick - m.WanderEpoch);
                    m.Pos = new Vec3(anchor.X + offset.X, anchor.Y, anchor.Z + offset.Z);
                    if (yaw is { } y)
                    {
                        m.Yaw = y;
                    }
                }
            }
        }
    }

    /// <summary>
    /// The pack offset and facing (null while pausing before the first leg) at <paramref name="ticks"/> since the wander
    /// began: the lap repeats, each point holds <see cref="PauseTicks"/>, then the leg to the next walks at
    /// <see cref="SpeedMmPerTick"/> or less.
    /// </summary>
    /// <param name="loop">The loop.</param>
    /// <param name="ticks">Ticks since the epoch (negative reads as zero).</param>
    /// <returns>The offset from the anchors and the yaw of the current or last leg, or null before any leg.</returns>
    public static (Vec3 Offset, int? Yaw) OffsetAt(Loop loop, int ticks)
    {
        ArgumentNullException.ThrowIfNull(loop);
        var n = loop.Offsets.Count;
        var u = Math.Max(0, ticks) % loop.Period;
        int? yaw = null;
        for (var i = 0; i < n; i++)
        {
            var from = loop.Offsets[i];
            var to = loop.Offsets[(i + 1) % n];
            if (u < PauseTicks)
            {
                return (from, yaw);
            }
            u -= PauseTicks;
            var legYaw = from == to ? yaw : Fixed.YawBetween(from, to);
            var leg = loop.LegTicks[i];
            if (u < leg)
            {
                return (Lerp(from, to, u, leg), legYaw);
            }
            u -= leg;
            yaw = legYaw;
        }
        return (loop.Offsets[0], yaw);
    }

    private static void Begin(SimState state, AggroGroup g, List<ActorState> members, IReadOnlyList<AggroGroup> groups, NavGrid grid)
    {
        var anchors = members.Select(m => m.Pos).ToList();
        var loop = Build(state.Seed, members[0].Id, anchors, g, groups, grid);
        foreach (var m in members)
        {
            m.WanderAnchor = m.Pos;
            m.WanderEpoch = state.Tick;
            m.WanderLoop = loop;
        }
    }

    private static Loop Build(long seed, int firstId, IReadOnlyList<Vec3> anchors, AggroGroup g, IReadOnlyList<AggroGroup> groups, NavGrid grid)
    {
        var others = groups.Where(o => !string.Equals(o.Id, g.Id, StringComparison.Ordinal) && o.ActorIds.Count > 0).ToList();
        var offsets = new List<Vec3> { Vec3.Zero };
        for (var k = 1; k <= Points; k++)
        {
            for (var attempt = 0; attempt < Tries; attempt++)
            {
                var h = Mix(unchecked((ulong)seed), (ulong)firstId, (ulong)k, (ulong)attempt);
                var cand = new Vec3((long)(h % (2 * ReachMm + 1)) - ReachMm, 0, (long)((h >> 20) % (2 * ReachMm + 1)) - ReachMm);
                if (Clear(offsets[^1], cand, anchors, g, others, grid))
                {
                    offsets.Add(cand);
                    break;
                }
            }
        }
        // The lap must close: drop trailing points until the way back to the anchor is clear (the anchor itself always is).
        while (offsets.Count > 1 && !Clear(offsets[^1], Vec3.Zero, anchors, g, others, grid))
        {
            offsets.RemoveAt(offsets.Count - 1);
        }
        var legs = new List<int>(offsets.Count);
        for (var i = 0; i < offsets.Count; i++)
        {
            var a = offsets[i];
            var b = offsets[(i + 1) % offsets.Count];
            var dx = a.X - b.X;
            var dz = a.Z - b.Z;
            var len = Fixed.Isqrt((dx * dx) + (dz * dz));
            legs.Add((int)((len + SpeedMmPerTick - 1) / SpeedMmPerTick));
        }
        return new Loop(offsets, legs);
    }

    // True when walking every member from (anchor + from) to (anchor + to) in a straight line stays in the pack's ring, out of
    // every other ring and on the floor, sampled every SampleMm (both ends included).
    private static bool Clear(Vec3 from, Vec3 to, IReadOnlyList<Vec3> anchors, AggroGroup g, IReadOnlyList<AggroGroup> others, NavGrid grid)
    {
        var dx = to.X - from.X;
        var dz = to.Z - from.Z;
        var len = Fixed.Isqrt((dx * dx) + (dz * dz));
        var steps = (int)Math.Max(1, (len + SampleMm - 1) / SampleMm);
        var own = g.AggroMm - MarginMm;
        for (var s = 0; s <= steps; s++)
        {
            var off = Lerp(from, to, s, steps);
            foreach (var a in anchors)
            {
                var p = new Vec3(a.X + off.X, a.Y, a.Z + off.Z);
                if (D2(p, g.Center) > own * own || !grid.Walkable(p))
                {
                    return false;
                }
                foreach (var o in others)
                {
                    var r = o.AggroMm + MarginMm;
                    if (D2(p, o.Center) <= r * r)
                    {
                        return false;
                    }
                }
            }
        }
        return true;
    }

    private static Vec3 Lerp(Vec3 a, Vec3 b, long step, long steps) =>
        new(a.X + (((b.X - a.X) * step) / steps), 0, a.Z + (((b.Z - a.Z) * step) / steps));

    private static long D2(Vec3 a, Vec3 b)
    {
        var dx = a.X - b.X;
        var dz = a.Z - b.Z;
        return (dx * dx) + (dz * dz);
    }

    // splitmix64 finaliser over the four inputs: integer only, the same on every platform.
    private static ulong Mix(ulong a, ulong b, ulong c, ulong d)
    {
        unchecked
        {
            var x = (a * 0x9E3779B97F4A7C15UL) ^ (b * 0xBF58476D1CE4E5B9UL) ^ (c * 0x94D049BB133111EBUL) ^ (d * 0xD6E8FEB86659FD93UL);
            x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
            x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
            return x ^ (x >> 31);
        }
    }
}
