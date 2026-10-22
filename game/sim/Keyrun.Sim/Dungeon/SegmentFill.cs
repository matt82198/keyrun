using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Dungeon;

/// <summary>One pack the segment fill placed, keyed by its room's (segment, branch).</summary>
/// <param name="RoomId">Its trash room.</param>
/// <param name="Segment">Its segment (0 .. SegmentCount - 1).</param>
/// <param name="Branch">Its branch within the segment (0 or 1).</param>
/// <param name="Slot">Its slot in the room (always 0: one pack per room).</param>
/// <param name="PackId">A pool pack id.</param>
/// <param name="Points">Its forces points (the sum of its members' values).</param>
public sealed record SegmentPack(int RoomId, int Segment, int Branch, int Slot, string PackId, int Points)
{
    /// <summary>A placement id unique in the dungeon: pack id, room and slot (e.g. "slag-haulers@r3s0").</summary>
    public string InstanceId => string.Create(CultureInfo.InvariantCulture, $"{PackId}@r{RoomId}s{Slot}");
}

/// <summary>A segment's boss and the room it stands in.</summary>
/// <param name="Segment">The segment (0 .. SegmentCount - 1).</param>
/// <param name="RoomId">The segment's boss room (<see cref="RoomGraph.BossRooms"/>).</param>
/// <param name="BossId">The boss enemy id (the dungeon's segment list).</param>
public sealed record SegmentBoss(int Segment, int RoomId, string BossId);

/// <summary>One branch of one segment: its trash rooms and the forces its packs carry.</summary>
/// <param name="Segment">The segment.</param>
/// <param name="Branch">The branch within the segment (0 or 1).</param>
/// <param name="Rooms">Its trash room ids in walking order.</param>
/// <param name="Points">Forces points placed in it.</param>
public sealed record SegmentBranch(int Segment, int Branch, IReadOnlyList<int> Rooms, int Points);

/// <summary>The segment fill's output.</summary>
/// <param name="Branches">Every (segment, branch), segment then branch order.</param>
/// <param name="Packs">Every placement, segment then branch then walking order.</param>
/// <param name="Bosses">One boss per segment, in segment order (the last is the final boss).</param>
/// <param name="TotalPoints">Forces points across the dungeon (every branch of every segment).</param>
public sealed record SegmentFillResult(IReadOnlyList<SegmentBranch> Branches, IReadOnlyList<SegmentPack> Packs, IReadOnlyList<SegmentBoss> Bosses, int TotalPoints)
{
    /// <summary>The forces points of the route that walks branch <c>pick[s]</c> in segment s.</summary>
    /// <param name="pick">One branch (0 or 1) per segment.</param>
    /// <exception cref="ArgumentException">A pick list that is not one valid branch per segment.</exception>
    public int RoutePoints(IReadOnlyList<int> pick)
    {
        ArgumentNullException.ThrowIfNull(pick);
        if (pick.Count != Bosses.Count || pick.Any(b => b < 0 || b >= RoomGraphGen.BranchCount))
        {
            throw new ArgumentException($"route: need one branch (0 or 1) per segment, {Bosses.Count} segments", nameof(pick));
        }
        return pick.Select((b, s) => Branches.Single(x => x.Segment == s && x.Branch == b).Points).Sum();
    }

    /// <summary>The fewest points any route carries: each segment's lighter branch (what the party can count on).</summary>
    public int MinRoutePoints => Bosses.Select((_, s) => Branches.Where(x => x.Segment == s).Min(x => x.Points)).Sum();

    /// <summary>The most points any route carries: each segment's heavier branch.</summary>
    public int MaxRoutePoints => Bosses.Select((_, s) => Branches.Where(x => x.Segment == s).Max(x => x.Points)).Sum();
}

/// <summary>
/// Stage 4 for a multi-segment dungeon (STATE.md D65/D67: entrance, fork, boss, fork, boss, ..., final boss; the Sunken
/// Bellfoundry). <see cref="BranchFill"/> stays the single-fork fill of the Hollow Vault's teaching pulls (byte-identical
/// oracle); this fill is keyed by (segment, branch) and has no teaching pulls. Rule:
/// <list type="number">
/// <item>Every trash room of every segment holds exactly one pack of the dungeon's pool, so every segment has filled rooms on both branches.
/// A segment deals its rooms from one seeded shuffle of the pool, so it repeats no pack while the pool is at least as big as its rooms.</item>
/// <item>Each segment's boss, from the dungeon's segment list, stands in that segment's boss room.</item>
/// <item>Forces: every route (one branch per segment) reaches <c>forcesRequired</c>, so the bar is reachable on either branch of any
/// segment and the other branch's packs are optional on top of it (WoW Mythic+). A route short of the bar upgrades its weakest branch's
/// lightest pack to the heaviest pack the segment does not yet hold, and the fill fails closed when no upgrade is left.</item>
/// </list>
/// Pure over (seed, graph, bosses, pool, bar).
/// </summary>
public static class SegmentFill
{
    /// <summary>The PRNG stage this fill draws from (4, the fill stage; <see cref="DungeonPipeline.StageRng"/>).</summary>
    public const uint Stage = 4;

    /// <summary>Fills every trash room of <paramref name="graph"/> and places each segment's boss.</summary>
    /// <param name="seed">The run seed.</param>
    /// <param name="graph">A graph from <see cref="RoomGraphGen"/> (one or more segments).</param>
    /// <param name="bosses">The boss enemy id of each segment, in segment order.</param>
    /// <param name="pool">The dungeon's pack pool, priced by forces points.</param>
    /// <param name="forcesRequired">The bar every route must reach, points (BIBLE s9.2: 1000 = 100%).</param>
    /// <returns>The fill.</returns>
    /// <exception cref="ArgumentException">A boss list that is not one non-empty id per segment, a bad pool, a bar below 1, or a trash room outside every (segment, branch).</exception>
    /// <exception cref="InvalidOperationException">A pool that cannot lift some route to the bar.</exception>
    public static SegmentFillResult Fill(long seed, RoomGraph graph, IReadOnlyList<string> bosses, IReadOnlyList<RosterPack> pool, int forcesRequired = 1000)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(bosses);
        PackSlots.ValidateRoster(pool);
        if (forcesRequired < 1)
        {
            throw new ArgumentException("segment fill: the forces bar must be at least 1", nameof(forcesRequired));
        }
        if (bosses.Count != graph.SegmentCount || bosses.Any(string.IsNullOrEmpty))
        {
            throw new ArgumentException($"segment fill: need one non-empty boss id per segment, {graph.SegmentCount} segments", nameof(bosses));
        }

        var rng = DungeonPipeline.StageRng(seed, Stage);
        var held = new SegmentPack[graph.SegmentCount][][];
        var roomsOf = new List<int>[graph.SegmentCount][];
        for (var s = 0; s < graph.SegmentCount; s++)
        {
            roomsOf[s] = new List<int>[RoomGraphGen.BranchCount];
            held[s] = new SegmentPack[RoomGraphGen.BranchCount][];
            var order = new List<int>();
            for (var b = 0; b < RoomGraphGen.BranchCount; b++)
            {
                roomsOf[s][b] = graph.Nodes.Where(n => n.Kind == RoomKind.Trash && n.Segment == s && n.Branch == b).OrderBy(n => n.Layer).Select(n => n.Id).ToList();
                if (roomsOf[s][b].Count == 0)
                {
                    throw new ArgumentException($"segment fill: segment {s} branch {b} has no trash room", nameof(graph));
                }
                order.AddRange(roomsOf[s][b]);
            }
            if (graph.Nodes.Count(n => n.Kind == RoomKind.Trash && n.Segment == s) != order.Count)
            {
                throw new ArgumentException($"segment fill: segment {s} has a trash room on neither branch", nameof(graph));
            }
            var deck = Shuffle(ref rng, pool.Count);
            var next = 0;
            for (var b = 0; b < RoomGraphGen.BranchCount; b++)
            {
                held[s][b] = roomsOf[s][b].Select(room =>
                {
                    var pick = pool[deck[next++ % deck.Length]];
                    return new SegmentPack(room, s, b, 0, pick.Id, pick.Points);
                }).ToArray();
            }
        }

        while (MinRoute(held) < forcesRequired)
        {
            if (!Lift(held, pool))
            {
                throw new InvalidOperationException($"segment fill: no pack upgrade left and the lightest route carries {MinRoute(held)} of {forcesRequired}");
            }
        }

        var branches = new List<SegmentBranch>();
        var packs = new List<SegmentPack>();
        for (var s = 0; s < graph.SegmentCount; s++)
        {
            for (var b = 0; b < RoomGraphGen.BranchCount; b++)
            {
                branches.Add(new SegmentBranch(s, b, roomsOf[s][b], held[s][b].Sum(p => p.Points)));
                packs.AddRange(held[s][b]);
            }
        }
        var placed = bosses.Select((id, s) => new SegmentBoss(s, graph.BossRooms[s], id)).ToList();
        return new SegmentFillResult(branches, packs, placed, packs.Sum(p => p.Points));
    }

    private static int BranchPoints(SegmentPack[] branch) => branch.Sum(p => p.Points);

    private static int MinRoute(SegmentPack[][][] held) => held.Sum(seg => seg.Min(BranchPoints));

    // Upgrades the lightest pack of the weakest branch that can still gain; false when no branch can.
    private static bool Lift(SegmentPack[][][] held, IReadOnlyList<RosterPack> pool)
    {
        var candidates = new List<(int Segment, int Branch)>();
        for (var s = 0; s < held.Length; s++)
        {
            var weakest = held[s].Min(BranchPoints);
            for (var b = 0; b < held[s].Length; b++)
            {
                if (BranchPoints(held[s][b]) == weakest)
                {
                    candidates.Add((s, b));
                }
            }
        }
        foreach (var (s, b) in candidates.OrderBy(c => BranchPoints(held[c.Segment][c.Branch])).ThenBy(c => c.Segment).ThenBy(c => c.Branch))
        {
            var inSegment = held[s].SelectMany(x => x).Select(p => p.PackId).ToHashSet(StringComparer.Ordinal);
            var best = pool.Where(r => !inSegment.Contains(r.Id)).OrderByDescending(r => r.Points).FirstOrDefault();
            var lightest = held[s][b].Select((p, i) => (p, i)).OrderBy(x => x.p.Points).ThenBy(x => x.i).First();
            if (best is not null && best.Points > lightest.p.Points)
            {
                held[s][b][lightest.i] = lightest.p with { PackId = best.Id, Points = best.Points };
                return true;
            }
        }
        return false;
    }

    // A Fisher-Yates shuffle of 0 .. n - 1 from the stage stream (an array, not an iterator: PurityTests).
    private static int[] Shuffle(ref PrngState rng, int n)
    {
        var deck = new int[n];
        for (var i = 0; i < n; i++)
        {
            deck[i] = i;
        }
        for (var i = n - 1; i > 0; i--)
        {
            var (value, next) = Prng.NextInt(rng, i + 1);
            rng = next;
            (deck[i], deck[(int)value]) = (deck[(int)value], deck[i]);
        }
        return deck;
    }
}
