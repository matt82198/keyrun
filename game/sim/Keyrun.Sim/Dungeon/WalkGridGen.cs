using System;
using System.Collections.Generic;
using System.Linq;

namespace Keyrun.Sim.Dungeon;

/// <summary>
/// Stage 2, room graph -> walkable grid and corridors. Layer L's rooms stand in column band L (room, then a corridor
/// gap), each layer centred across <see cref="DungeonGenParams.MaxWidth"/> row slots. Each edge gets its own vertical
/// lane in the gap after its source layer, so a corridor runs: out of the source room's east wall, along to its lane,
/// up or down to the target room's centre row, and into the target's west wall. Corridors never enter a room column,
/// so the grid never links two rooms more than one layer apart. Pure over (graph, params).
/// </summary>
public static class WalkGridGen
{
    /// <summary>Emits the grid for <paramref name="graph"/>.</summary>
    public static WalkGrid Emit(RoomGraph graph, DungeonGenParams p)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(p);
        p.Validate();
        var pitch = p.CorridorCells + 1;
        // A layer pair has at most 3 * MaxWidth - 1 edges (overlap links plus one diagonal per room), each its own lane.
        var gap = 3 + (pitch * 3 * p.MaxWidth);
        var band = p.RoomCells + gap;
        var rowPitch = p.RoomCells + p.RowGapCells;
        var width = (graph.LayerCount * band) - gap;
        var height = (p.MaxWidth * p.RoomCells) + ((p.MaxWidth - 1) * p.RowGapCells);
        var cells = new byte[width * height];

        var rooms = new List<RoomRect>();
        foreach (var n in graph.Nodes)
        {
            var layerWidth = graph.Nodes.Count(m => m.Layer == n.Layer);
            var minCx = n.Layer * band;
            var minCz = (((2 * n.Lane) + (p.MaxWidth - layerWidth)) * rowPitch) / 2;
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
                Fill(cells, width, a.MaxCx + 1, a.CenterCz, laneX + w, a.CenterCz + w);
                Fill(cells, width, laneX, Math.Min(a.CenterCz, b.CenterCz), laneX + w, Math.Max(a.CenterCz, b.CenterCz) + w);
                Fill(cells, width, laneX, b.CenterCz, b.MinCx - 1, b.CenterCz + w);
                k++;
            }
        }
        return new WalkGrid(width, height, p.CellMm, 0, 0, cells, rooms);
    }

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
