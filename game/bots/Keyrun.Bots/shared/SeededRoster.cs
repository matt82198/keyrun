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

    /// <summary>The fill params with the forces table's bar.</summary>
    /// <param name="reg">Loaded content.</param>
    public static BranchFillParams Params(C.ContentRegistry reg)
    {
        ArgumentNullException.ThrowIfNull(reg);
        return BranchFillParams.Default with { ForcesRequired = reg.Forces.Required };
    }
}
