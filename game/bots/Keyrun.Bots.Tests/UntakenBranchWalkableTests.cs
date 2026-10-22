using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Dungeon;
using Keyrun.Sim.Nav;
using Xunit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// f-16a6b655 receipt: a human seat must be able to walk the branch the bots did not take. The run's walkable nav set is
/// the walked chain's boxes only (<see cref="SeededLayout.Walk"/>: the untaken branch is <see cref="SegmentWalk.SideBoxes"/>,
/// never in <c>NavLayout.Boxes</c>, so the layout clamp puts a move back). These tests measure, per seed, whether every
/// generated room of the Sunken Bellfoundry (both branches, every segment) lies in the walkable nav set of the run, and
/// whether a move into an untaken-branch room is clamped back.
/// </summary>
public sealed class UntakenBranchWalkableTests
{
    private const string DungeonId = "sunken-bellfoundry";

    private static readonly long[] Seeds = Enumerable.Range(1, 12).Select(i => (long)i).ToArray();

    [Fact(DisplayName = "Untaken branch: every generated room of the Bellfoundry lies inside the run's walkable nav set, both branches, every segment")]
    public void Every_generated_room_is_walkable_in_the_run()
    {
        var reg = ContentLoader.Load();
        var uncoveredTotal = 0;
        foreach (var seed in Seeds)
        {
            var d = SeededLayout.BuildSegmented(reg, DungeonId, seed);
            var nav = WalkableNav(d.Walk);
            var uncovered = d.Graph.Nodes
                .Where(n => !RoomFullyWalkable(d.Grid, nav, n.Id))
                .Select(n => n.Id)
                .ToList();
            uncoveredTotal += uncovered.Count;
            Assert.True(
                uncovered.Count == 0,
                $"seed {seed}: {uncovered.Count} of {d.Graph.Nodes.Count} rooms not walkable in this run (walked chain {string.Join(">", d.Walk.Chain)}; "
                + $"untaken rooms {string.Join(",", UntakenRooms(d).Select(id => id.ToString()))}; nav boxes {d.Walk.Boxes.Count})");
        }
        Assert.Equal(0, uncoveredTotal);
    }

    [Fact(DisplayName = "Untaken branch: a move from the walked chain into an untaken-branch room is not clamped back")]
    public void Move_into_untaken_branch_room_is_not_clamped()
    {
        var reg = ContentLoader.Load();
        foreach (var seed in Seeds)
        {
            var d = SeededLayout.BuildSegmented(reg, DungeonId, seed);
            var open = WalkableBounds(d.Walk);
            foreach (var untaken in UntakenRooms(d))
            {
                var from = Centre(d.Grid, d.Graph.EntranceId);
                var to = Centre(d.Grid, untaken);
                var clamped = Movement.ClampToWalkable(from, to, open);
                Assert.True(clamped == to, $"seed {seed}: move into untaken room {untaken} clamped to {clamped} (target {to})");
            }
        }
    }

    // The run's walkable nav set: the walked chain's boxes, the same boxes NavLayouts.ToSim hands the sim.
    private static NavGrid WalkableNav(SegmentWalk walk) =>
        new(WalkableBounds(walk), Array.Empty<Bounds>());

    private static List<Bounds> WalkableBounds(SegmentWalk walk) =>
        walk.Boxes.Select(b => new Bounds(b.MinX, b.MaxX, b.MinZ, b.MaxZ)).ToList();

    private static bool RoomFullyWalkable(WalkGrid grid, NavGrid nav, int roomId)
    {
        var r = grid.Rooms[roomId];
        for (var cz = r.MinCz; cz <= r.MaxCz; cz++)
        {
            for (var cx = r.MinCx; cx <= r.MaxCx; cx++)
            {
                if (!nav.Walkable(CellCentre(grid, cx, cz)))
                {
                    return false;
                }
            }
        }
        return true;
    }

    // Trash rooms of no segment's walked chain: the untaken branches' rooms.
    private static List<int> UntakenRooms(SegmentedDungeon d) =>
        d.Graph.Nodes
            .Where(n => n.Kind == RoomKind.Trash && !d.Walk.Chain.Contains(n.Id))
            .Select(n => n.Id)
            .ToList();

    private static Vec3 Centre(WalkGrid grid, int roomId)
    {
        var r = grid.Rooms[roomId];
        return CellCentre(grid, r.CenterCx, r.CenterCz);
    }

    private static Vec3 CellCentre(WalkGrid grid, int cx, int cz) =>
        new(grid.OriginX + ((long)cx * grid.CellMm) + (grid.CellMm / 2), 0, grid.OriginZ + ((long)cz * grid.CellMm) + (grid.CellMm / 2));
}
