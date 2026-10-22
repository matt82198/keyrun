using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Dungeon;

/// <summary>One pack the filler may place: a roster entry (content later loads these from a pack directory).</summary>
/// <param name="Id">Pack id, unique in the roster.</param>
/// <param name="Points">Its forces points (the sum of its members), positive.</param>
public sealed record RosterPack(string Id, int Points);

/// <summary>A trash room's forces quota and the slot positions packs may stand on.</summary>
/// <param name="RoomId">The room.</param>
/// <param name="QuotaPoints">Points the filler must place in this room at least.</param>
/// <param name="Slots">Pack centres, mm, in slot order.</param>
public sealed record RoomQuota(int RoomId, int QuotaPoints, IReadOnlyList<Vec3> Slots);

/// <summary>
/// Stage 3, pack slots by forces budget and route. Every route visits exactly one trash room per middle layer, so a
/// per-room floor of ceil((2 * required + biggest pack) / middle layers) puts at least 2 * required + biggest pack on
/// every route. Any set that large splits into two disjoint sets of at least <c>required</c> each (deal the packs one
/// by one to the lighter side: the sides end within one pack of each other), which is D45's "two disjoint pack
/// choices reach 100%". Pure over (graph, grid, params, roster).
/// </summary>
public static class PackSlots
{
    /// <summary>The most packs one room can hold (the six slot positions).</summary>
    public const int MaxSlotsPerRoom = 6;

    /// <summary>The quota and slots of every trash room, in room id order.</summary>
    /// <exception cref="ArgumentException">A bad roster, or a quota no six packs of the roster can meet.</exception>
    public static IReadOnlyList<RoomQuota> Plan(RoomGraph graph, WalkGrid grid, DungeonGenParams p, IReadOnlyList<RosterPack> roster)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(p);
        ValidateRoster(roster);
        var maxPack = roster.Max(r => r.Points);
        var minPack = roster.Min(r => r.Points);
        var middle = graph.LayerCount - 2;
        var quota = (int)IntMath.CeilDiv((2L * p.ForcesRequired) + maxPack, middle);
        var slotCount = (int)Math.Min(MaxSlotsPerRoom, IntMath.CeilDiv(quota, minPack));
        if ((long)slotCount * maxPack < quota)
        {
            throw new ArgumentException($"roster: {slotCount} packs of at most {maxPack} cannot meet a room quota of {quota}", nameof(roster));
        }
        return graph.Nodes
            .Where(n => n.Kind == RoomKind.Trash)
            .Select(n => new RoomQuota(n.Id, quota, SlotCenters(grid, grid.Rooms[n.Id]).Take(slotCount).ToList()))
            .ToList();
    }

    /// <summary>Throws unless the roster is non-empty with unique ids and positive points (fail closed).</summary>
    /// <exception cref="ArgumentException">An empty roster, a duplicate id or a non-positive value.</exception>
    public static void ValidateRoster(IReadOnlyList<RosterPack> roster)
    {
        ArgumentNullException.ThrowIfNull(roster);
        if (roster.Count == 0)
        {
            throw new ArgumentException("roster: no packs", nameof(roster));
        }
        if (roster.Select(r => r.Id).Distinct(StringComparer.Ordinal).Count() != roster.Count)
        {
            throw new ArgumentException("roster: duplicate pack id", nameof(roster));
        }
        if (roster.Any(r => r.Points <= 0))
        {
            throw new ArgumentException("roster: a pack is worth no forces", nameof(roster));
        }
    }

    // An array, not an iterator: a compiler-built iterator reads Environment.CurrentManagedThreadId (PurityTests).
    private static Vec3[] SlotCenters(WalkGrid grid, RoomRect r)
    {
        const int Inset = 2;
        var lo = (X: r.MinCx + Inset, Z: r.MinCz + Inset);
        var hi = (X: r.MaxCx - Inset, Z: r.MaxCz - Inset);
        return new[]
        {
            grid.CellCenter(lo.X, lo.Z),
            grid.CellCenter(hi.X, hi.Z),
            grid.CellCenter(lo.X, hi.Z),
            grid.CellCenter(hi.X, lo.Z),
            grid.CellCenter(r.CenterCx, lo.Z),
            grid.CellCenter(r.CenterCx, hi.Z),
        };
    }
}
