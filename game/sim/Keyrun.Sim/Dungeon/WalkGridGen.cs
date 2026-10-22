using System;
using System.Collections.Generic;
using System.Linq;

namespace Keyrun.Sim.Dungeon;

/// <summary>
/// Stage 2, room graph -> walkable grid and corridors. Layer L's rooms stand in column band L (room, then a corridor
/// gap). A branch room keeps its branch's row (lane 0 top, lane 1 bottom) at every depth; the entrance and the boss
/// sit centred between the two rows. Each edge gets its own vertical lane in the gap after its source layer, so a
/// corridor runs: out of the source room's east wall, along to its lane, up or down to its entry row, and into the
/// target's west wall. A branch room's ports are on its centre row; on the entrance and the boss, branch 0's corridor
/// uses the top half of the wall and branch 1's the bottom half (<see cref="PortRow"/>), so the shorter branch's
/// corridor to the boss crosses the longer branch's last column band on its own row side, clear of the other
/// branch's room. The two branches share no walkable cell outside the entrance and boss rooms (SeededDungeonTests).
/// Pure over (graph, params).
/// </summary>
public static class WalkGridGen
{
    /// <summary>Emits the grid for <paramref name="graph"/>.</summary>
    public static WalkGrid Emit(RoomGraph graph, DungeonGenParams p)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(p);
        p.Validate();
        const int Rows = RoomGraphGen.BranchCount;
        var pitch = p.CorridorCells + 1;
        // A source layer has at most one edge per branch (the entrance's fork has BranchCount), each its own lane.
        var gap = 3 + (pitch * 2 * Rows);
        var band = p.RoomCells + gap;
        var rowPitch = p.RoomCells + p.RowGapCells;
        var width = (graph.LayerCount * band) - gap;
        var height = (Rows * p.RoomCells) + ((Rows - 1) * p.RowGapCells);
        var cells = new byte[width * height];

        var rooms = new List<RoomRect>();
        foreach (var n in graph.Nodes)
        {
            var rowsUsed = n.Kind == RoomKind.Trash ? Rows : 1;
            var minCx = n.Layer * band;
            var minCz = (((2 * n.Lane) + (Rows - rowsUsed)) * rowPitch) / 2;
            var rect = new RoomRect(n.Id, minCx, minCz, minCx + p.RoomCells - 1, minCz + p.RoomCells - 1);
            rooms.Add(rect);
            Fill(cells, width, rect.MinCx, rect.MinCz, rect.MaxCx, rect.MaxCz);
        }

        foreach (var group in graph.Edges.GroupBy(e => graph.Nodes[e.From].Layer))
        {
            var k = 0;
            foreach (var e in group)
            {
                var a = rooms[e.From];
                var b = rooms[e.To];
                var laneX = a.MaxCx + 2 + (k * pitch);
                var w = p.CorridorCells - 1;
                if (laneX + w >= b.MinCx - 1)
                {
                    throw new InvalidOperationException($"walk grid: corridor lane {k} of layer {graph.Nodes[e.From].Layer} does not fit its gap");
                }
                var exit = PortRow(graph.Nodes[e.From], a, graph.Nodes[e.To].Lane, p.CorridorCells);
                var entry = PortRow(graph.Nodes[e.To], b, graph.Nodes[e.From].Lane, p.CorridorCells);
                Fill(cells, width, a.MaxCx + 1, exit, laneX + w, exit + w);
                Fill(cells, width, laneX, Math.Min(exit, entry), laneX + w, Math.Max(exit, entry) + w);
                Fill(cells, width, laneX, entry, b.MinCx - 1, entry + w);
                k++;
            }
        }
        return new WalkGrid(width, height, p.CellMm, 0, 0, cells, rooms);
    }

    /// <summary>
    /// The first row of a corridor's port on room <paramref name="r"/>: a branch room's centre row; on the entrance
    /// and the boss (shared by both branches), branch 0's corridor uses the top half of the wall and branch 1's the
    /// bottom half, so the two branches' corridors never touch.
    /// </summary>
    private static int PortRow(RoomNode n, RoomRect r, int otherLane, int corridorCells) =>
        n.Kind == RoomKind.Trash ? r.CenterCz : otherLane == 0 ? r.MinCz + 1 : r.MaxCz - corridorCells;

    private static void Fill(byte[] cells, int width, int x0, int z0, int x1, int z1)
    {
        for (var z = z0; z <= z1; z++)
        {
            for (var x = x0; x <= x1; x++)
            {
                cells[(z * width) + x] = 1;
            }
        }
    }
}
