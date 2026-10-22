using System;
using System.Collections.Generic;
using System.Linq;
using C = Keyrun.Content;

namespace Keyrun.Bots.Shared;

/// <summary>
/// R-PACKDENSITY-2 (ruling, event store seq 171): pack density is enforced at placement time. The seeded fill deals one pack
/// per room (or a few far apart), so a chain pull is neither possible nor a choice. This pass re-places a built layout's packs
/// in clusters of two or three, per route branch: each cluster stands in one room, every member has a partner whose nearest
/// member stands within 1.5x its own aggro radius, and no member of another pull stands inside 0.8x it. Clusters keep to
/// themselves (every other pack stands past 1.5x the radius), so a pack's nearest neighbour is always its cluster partner,
/// and each cluster's members are linked (N1 PACK-LINKS), so the body pull of one engages the next. The bounds are the
/// ruling's (PackDensityPropertyTests); nothing here is tuned to pass.
/// </summary>
internal static class PackDensity
{
    /// <summary>The ruling's far bound, as the property test states it (the near bound, 0.8, is <see cref="NearPlaced"/> less its margin).</summary>
    private const double FarBound = 1.5;

    // Placement margins inside the bounds (the property test measures in doubles; a hair of margin keeps it off the edge).
    private const double NearPlaced = 0.83;
    private const double FarPlaced = 1.45;

    // Another cluster's packs stand past this multiple of the radius, so a pack's nearest neighbour is its own partner.
    private const double Independent = 1.6;

    // Two packs of one pull (no near bound between them) still never stack closer than this multiple of the radius.
    private const double SamePullFloor = 0.5;

    private const int Step = 250;
    private const int AnchorStep = 500;
    private const int BandMm = 9000;
    private const int LineMargin = 50;

    /// <summary>
    /// <paramref name="packs"/> re-placed in density clusters. Packs are grouped by <see cref="C.LayoutPack.Branch"/> (a room
    /// path's first room, else the room) in the order given; each group is cut into pairs, the last a triple when the count is
    /// odd. A cluster stands in its first member's room (a later member's room when the first has no space) with its members
    /// linked in a chain.
    /// </summary>
    /// <param name="packs">The packs as the fill placed them: required packs on the line first, then optional packs.</param>
    /// <param name="rooms">The layout's room boxes by box id.</param>
    /// <param name="start">The party's start.</param>
    /// <param name="route">The route points (the walking line is the start then these).</param>
    /// <param name="widestSlotMm">The widest formation slot, mm (an optional pack's ring plus this clears the line).</param>
    /// <returns>The packs, in the order given, re-placed.</returns>
    /// <exception cref="InvalidOperationException">A group of one pack, or a cluster no room has space for.</exception>
    internal static List<C.LayoutPack> Place(
        IReadOnlyList<C.LayoutPack> packs, IReadOnlyDictionary<string, C.LayoutBox> rooms, C.LayoutPoint start, IReadOnlyList<C.LayoutPoint> route, int widestSlotMm)
    {
        var line = new List<C.LayoutPoint> { start };
        line.AddRange(route);
        var ctx = new Ctx(rooms, start, line, widestSlotMm);
        var placed = new Dictionary<string, C.LayoutPack>(StringComparer.Ordinal);
        var all = new List<C.LayoutPack>();
        foreach (var group in packs.GroupBy(p => p.Branch ?? p.Room, StringComparer.Ordinal))
        {
            var members = group.ToList();
            if (members.Count < 2)
            {
                throw new InvalidOperationException($"pack density: branch \"{group.Key}\" holds {members.Count} pack(s), a pack with no partner cannot chain");
            }
            var at = 0;
            while (at < members.Count)
            {
                var size = members.Count - at == 3 ? 3 : 2;
                var cluster = Cluster(members.GetRange(at, size), ctx, all);
                foreach (var p in cluster)
                {
                    placed[p.Id] = p;
                    all.Add(p);
                }
                at += size;
            }
        }
        return packs.Select(p => placed[p.Id]).ToList();
    }

    private sealed record Ctx(IReadOnlyDictionary<string, C.LayoutBox> Rooms, C.LayoutPoint Start, IReadOnlyList<C.LayoutPoint> Line, int WidestSlotMm);

    private static List<C.LayoutPack> Cluster(List<C.LayoutPack> members, Ctx ctx, List<C.LayoutPack> placed)
    {
        var roomIds = members.Select(m => m.Room).Distinct(StringComparer.Ordinal).ToList();
        if (!members[0].Optional)
        {
            roomIds = roomIds.Take(1).ToList(); // a required pack stays in its own room (never the boss's)
        }
        foreach (var roomId in roomIds)
        {
            var room = ctx.Rooms[roomId];
            var near = placed.Where(p => string.Equals(p.Room, roomId, StringComparison.Ordinal)).ToList();
            foreach (var anchor in AnchorCandidates(members[0], room))
            {
                var first = members[0] with { Room = roomId, Center = anchor };
                if (!Fits(first, new List<C.LayoutPack>(), near, room, ctx, relaxBand: false))
                {
                    continue;
                }
                foreach (var relax in new[] { false, true })
                {
                    var trial = new List<C.LayoutPack> { first };
                    var ok = true;
                    for (var k = 1; k < members.Count && ok; k++)
                    {
                        var next = Partner(members[k] with { Room = roomId }, trial, near, room, ctx, relax);
                        if (next is null)
                        {
                            ok = false;
                            break;
                        }
                        trial.Add(next with { Links = new C.ValueList<string>(new[] { trial[^1].Id }) });
                    }
                    if (ok)
                    {
                        return trial;
                    }
                }
            }
        }
        throw new InvalidOperationException($"pack density: no room has space for the cluster {string.Join("+", members.Select(m => m.Id))}");
    }

    // The first member's anchors: a required pack stays on the line where the fill put it (then slides along +x); an optional
    // pack stands along the room's north or south band (the fill's off-line slots), scanning along x.
    private static IEnumerable<C.LayoutPoint> AnchorCandidates(C.LayoutPack pack, C.LayoutBox room)
    {
        if (!pack.Optional)
        {
            for (var x = pack.Center.X; x <= room.MaxX; x += AnchorStep)
            {
                yield return new C.LayoutPoint { X = x, Z = pack.Center.Z };
            }
            yield break;
        }
        var mid = room.MinZ + ((room.MaxZ - room.MinZ) / 2);
        foreach (var side in new[] { -BandMm, BandMm })
        {
            for (var x = room.MinX + 4000; x <= room.MaxX - 2000; x += AnchorStep)
            {
                yield return new C.LayoutPoint { X = x, Z = mid + side };
            }
        }
    }

    // The nearest acceptable spot to the cluster's first member for a partner, or null.
    private static C.LayoutPack? Partner(C.LayoutPack pack, List<C.LayoutPack> cluster, List<C.LayoutPack> near, C.LayoutBox room, Ctx ctx, bool relaxBand)
    {
        var anchor = cluster[0].Center;
        var reach = (int)(FarBound * pack.AggroMm) + 3000;
        var candidates = new List<(long D2, int X, int Z)>();
        for (var x = Math.Max(room.MinX, anchor.X - reach); x <= Math.Min(room.MaxX, anchor.X + reach); x += Step)
        {
            for (var z = Math.Max(room.MinZ, anchor.Z - reach); z <= Math.Min(room.MaxZ, anchor.Z + reach); z += Step)
            {
                var (dx, dz) = ((long)x - anchor.X, (long)z - anchor.Z);
                candidates.Add(((dx * dx) + (dz * dz), x, z));
            }
        }
        foreach (var (_, x, z) in candidates.OrderBy(c => c.D2).ThenBy(c => c.X).ThenBy(c => c.Z))
        {
            var trial = pack with { Center = new C.LayoutPoint { X = x, Z = z } };
            if (Fits(trial, cluster, near, room, ctx, relaxBand))
            {
                return trial;
            }
        }
        return null;
    }

    private static bool Fits(C.LayoutPack pack, List<C.LayoutPack> cluster, List<C.LayoutPack> near, C.LayoutBox room, Ctx ctx, bool relaxBand)
    {
        if (!InsideWithMembers(room, pack))
        {
            return false;
        }
        var mid = room.MinZ + ((room.MaxZ - room.MinZ) / 2);
        if (pack.Optional)
        {
            var clear = pack.AggroMm + ctx.WidestSlotMm + LineMargin;
            if (!relaxBand && Math.Abs(pack.Center.Z - mid) < BandMm)
            {
                return false;
            }
            for (var k = 0; k + 1 < ctx.Line.Count; k++)
            {
                if (DistanceToSegment(ctx.Line[k], ctx.Line[k + 1], pack.Center) <= clear)
                {
                    return false;
                }
            }
        }
        else
        {
            var meets = false;
            for (var k = 0; k + 1 < ctx.Line.Count && !meets; k++)
            {
                meets = DistanceToSegment(ctx.Line[k], ctx.Line[k + 1], pack.Center) <= pack.AggroMm - LineMargin;
            }
            if (!meets || Distance(ctx.Start, pack.Center) <= pack.AggroMm + LineMargin)
            {
                return false;
            }
        }
        foreach (var other in near)
        {
            // Another cluster's pack stands apart, both ways, so the nearest neighbour is the partner.
            if (NearestMember(pack.Center, other) < Independent * pack.AggroMm || NearestMember(other.Center, pack) < Independent * other.AggroMm)
            {
                return false;
            }
        }
        var tied = cluster.Count == 0;
        foreach (var mate in cluster)
        {
            var (ab, ba) = (NearestMember(pack.Center, mate), NearestMember(mate.Center, pack));
            var samePull = string.Equals(PullIdOf(pack), PullIdOf(mate), StringComparison.Ordinal);
            var (floorA, floorB) = samePull ? (SamePullFloor * pack.AggroMm, SamePullFloor * mate.AggroMm) : (NearPlaced * pack.AggroMm, NearPlaced * mate.AggroMm);
            if (ab < floorA || ba < floorB)
            {
                return false;
            }
            tied |= ab <= FarPlaced * pack.AggroMm && ba <= FarPlaced * mate.AggroMm;
        }
        return tied;
    }

    private static bool InsideWithMembers(C.LayoutBox room, C.LayoutPack pack)
    {
        bool In(int x, int z) => x >= room.MinX && x <= room.MaxX && z >= room.MinZ && z <= room.MaxZ;
        return In(pack.Center.X, pack.Center.Z) && pack.Enemies.All(e => In(pack.Center.X + e.Spawn.X, pack.Center.Z + e.Spawn.Z));
    }

    /// <summary>The content pull id of a pack: the prefix of its placement id ("pair@r5s0" is pull "pair").</summary>
    private static string PullIdOf(C.LayoutPack pack) => pack.Id.Split('@')[0];

    /// <summary>Distance, mm, from <paramref name="from"/> to the nearest member centre of <paramref name="pack"/> (the property test's measure).</summary>
    internal static double NearestMember(C.LayoutPoint from, C.LayoutPack pack)
    {
        var best = double.MaxValue;
        foreach (var group in pack.Enemies)
        {
            double dx = (pack.Center.X + group.Spawn.X) - from.X;
            double dz = (pack.Center.Z + group.Spawn.Z) - from.Z;
            best = Math.Min(best, Math.Sqrt((dx * dx) + (dz * dz)));
        }
        return best;
    }

    private static double Distance(C.LayoutPoint a, C.LayoutPoint b)
    {
        double dx = a.X - b.X;
        double dz = a.Z - b.Z;
        return Math.Sqrt((dx * dx) + (dz * dz));
    }

    private static double DistanceToSegment(C.LayoutPoint a, C.LayoutPoint b, C.LayoutPoint p)
    {
        double abx = b.X - a.X, abz = b.Z - a.Z, apx = p.X - a.X, apz = p.Z - a.Z;
        var len2 = (abx * abx) + (abz * abz);
        var t = len2 == 0 ? 0 : Math.Clamp(((apx * abx) + (apz * abz)) / len2, 0, 1);
        double dx = apx - (t * abx), dz = apz - (t * abz);
        return Math.Sqrt((dx * dx) + (dz * dz));
    }
}
