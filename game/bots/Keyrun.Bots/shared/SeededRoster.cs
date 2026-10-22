using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Dungeon;
using C = Keyrun.Content;

namespace Keyrun.Bots.Shared;

/// <summary>
/// Content -> the branch fill's roster (STATE.md D45/D46, ruling R-PACKS-7: forces ship with the packs that place them).
/// The teaching pulls are the forces route's dungeon pulls in dungeon order; the optional roster is the layout's optional
/// packs. Every price is the sum of the members' forces values (<see cref="C.RouteForces"/>), so the fill adds no data of
/// its own. It lives beside <see cref="NavLayouts"/> because Keyrun.Bots is the Godot-free assembly that sees both
/// Keyrun.Content and Keyrun.Sim.
/// </summary>
public static class SeededRoster
{
    /// <summary>The dungeon's teaching pulls, in dungeon order, priced by forces values.</summary>
    /// <param name="reg">Loaded content.</param>
    public static IReadOnlyList<TeachingPull> Teaching(C.ContentRegistry reg)
    {
        ArgumentNullException.ThrowIfNull(reg);
        var dungeon = reg.Dungeons[reg.Forces.Route.DungeonId];
        return dungeon.Pulls.Select((pull, i) => new TeachingPull(pull.Id, i, C.RouteForces.PullPoints(reg.Forces, pull))).ToList();
    }

    /// <summary>The layout's optional packs, in layout order, priced by forces values.</summary>
    /// <param name="reg">Loaded content.</param>
    public static IReadOnlyList<RosterPack> Optional(C.ContentRegistry reg)
    {
        ArgumentNullException.ThrowIfNull(reg);
        return reg.Layout.Packs.Where(p => p.Optional).Select(p => new RosterPack(p.Id, C.RouteForces.LayoutPackPoints(reg.Forces, p))).ToList();
    }

    /// <summary>
    /// A multi-segment dungeon's pack pool (D65/D67), in its <c>packPool</c> order, each pack priced by the sum of its
    /// members' forces values (R-PACKS-7): what <see cref="SegmentFill"/> deals to the trash rooms.
    /// </summary>
    /// <param name="reg">Loaded content.</param>
    /// <param name="dungeonId">The dungeon, e.g. "sunken-bellfoundry".</param>
    /// <exception cref="InvalidOperationException">An unknown dungeon or a pool id with no roster pack.</exception>
    public static IReadOnlyList<RosterPack> Pool(C.ContentRegistry reg, string dungeonId)
    {
        ArgumentNullException.ThrowIfNull(reg);
        var dungeon = Dungeon(reg, dungeonId);
        return dungeon.PackPool
            .Select(id => reg.Roster.TryGetValue(id, out var pack)
                ? new RosterPack(id, pack.Enemies.Sum(g => g.Count * reg.Forces.PointsOf(g.EnemyId)))
                : throw new InvalidOperationException($"seeded roster: dungeon \"{dungeonId}\" pool names unknown pack \"{id}\""))
            .ToList();
    }

    /// <summary>The boss enemy id of each of the dungeon's segments, in segment order (the last is the final boss).</summary>
    /// <param name="reg">Loaded content.</param>
    /// <param name="dungeonId">The dungeon.</param>
    /// <exception cref="InvalidOperationException">An unknown dungeon.</exception>
    public static IReadOnlyList<string> SegmentBosses(C.ContentRegistry reg, string dungeonId)
    {
        ArgumentNullException.ThrowIfNull(reg);
        return Dungeon(reg, dungeonId).Segments.Select(s => s.Boss.EnemyId).ToList();
    }

    private static C.DungeonDef Dungeon(C.ContentRegistry reg, string dungeonId) =>
        reg.Dungeons.TryGetValue(dungeonId, out var dungeon) ? dungeon : throw new InvalidOperationException($"seeded roster: unknown dungeon \"{dungeonId}\"");

    /// <summary>
    /// The fill params with the forces table's bar, and a branch target of the bar plus the biggest optional pack, so any
    /// one optional pack of a branch is skippable (LayoutValidator's SKIPPABLE-PACKS rule: the route still reaches the
    /// bar without it) while the party still pulls most of its branch (1000 of at least 1420 with today's roster).
    /// </summary>
    /// <param name="reg">Loaded content.</param>
    public static BranchFillParams Params(C.ContentRegistry reg)
    {
        ArgumentNullException.ThrowIfNull(reg);
        var required = reg.Forces.Required;
        var target = (long)required + Optional(reg).Max(o => o.Points);
        return BranchFillParams.Default with { ForcesRequired = required, BranchTargetBp = (int)((target * 10000) + required - 1) / required };
    }
}
