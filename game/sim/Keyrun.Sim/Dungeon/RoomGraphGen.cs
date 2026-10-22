using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Dungeon;

/// <summary>
/// Stage 1, seed -> room graph (game/design/ARCHITECTURE.md section 2). Layer 0 is the entrance, the last layer the
/// boss, the layer before it a single rejoin room, and the middle layers 1..MaxWidth rooms wide with at least one
/// layer two or more wide (the fork; D45 "never linear"). Edges link each layer to the next only, so every graph is a
/// forward-only DAG. Pure: the same seed and params give the same graph.
/// </summary>
public static class RoomGraphGen
{
    /// <summary>Generates the room graph for <paramref name="seed"/>.</summary>
    /// <param name="seed">The run seed.</param>
    /// <param name="p">Pipeline params (validated).</param>
    /// <returns>The graph.</returns>
    public static RoomGraph Generate(long seed, DungeonGenParams p)
    {
        ArgumentNullException.ThrowIfNull(p);
        p.Validate();
        var rng = DungeonPipeline.StageRng(seed, 1);
        var middle = p.MinMiddleLayers + Draw(ref rng, p.MaxMiddleLayers - p.MinMiddleLayers + 1);
        // widths[0] = entrance, widths[1..middle] = middle layers (the last one is the rejoin), widths[middle+1] = boss.
        var widths = new int[middle + 2];
        widths[0] = 1;
        widths[middle] = 1;
        widths[middle + 1] = 1;
        for (var layer = 1; layer < middle; layer++)
        {
            widths[layer] = 1 + Draw(ref rng, p.MaxWidth);
        }
        if (Enumerable.Range(1, middle - 1).All(l => widths[l] == 1))
        {
            widths[1 + Draw(ref rng, middle - 1)] = 2 + Draw(ref rng, p.MaxWidth - 1);
        }

        var nodes = new List<RoomNode>();
        var firstId = new int[widths.Length];
        for (var layer = 0; layer < widths.Length; layer++)
        {
            firstId[layer] = nodes.Count;
            var kind = layer == 0 ? RoomKind.Entrance : layer == widths.Length - 1 ? RoomKind.Boss : RoomKind.Trash;
            for (var lane = 0; lane < widths[layer]; lane++)
            {
                nodes.Add(new RoomNode(nodes.Count, layer, lane, kind));
            }
        }

        var edges = new SortedSet<(int From, int To)>();
        for (var layer = 0; layer + 1 < widths.Length; layer++)
        {
            LinkLayers(widths[layer], widths[layer + 1], firstId[layer], firstId[layer + 1], edges, ref rng);
        }
        return new RoomGraph(nodes, edges.Select(e => new RoomEdge(e.From, e.To)).ToList());
    }

    /// <summary>
    /// Links a layer of <paramref name="a"/> rooms to the next of <paramref name="b"/>: lane i joins lane j when their
    /// spans overlap on a common a*b scale (every room gets at least one edge each way), then each room may gain one
    /// seeded diagonal to the next lane down (a 1-in-4 draw), so equal-width layers still cross over sometimes.
    /// </summary>
    private static void LinkLayers(int a, int b, int fromBase, int toBase, SortedSet<(int, int)> edges, ref PrngState rng)
    {
        for (var i = 0; i < a; i++)
        {
            for (var j = 0; j < b; j++)
            {
                var overlap = (i * b) < ((j + 1) * a) && (j * a) < ((i + 1) * b);
                if (overlap)
                {
                    edges.Add((fromBase + i, toBase + j));
                }
            }
        }
        for (var i = 0; i < a; i++)
        {
            if (Draw(ref rng, 4) != 0)
            {
                continue;
            }
            var maxJ = Enumerable.Range(0, b).Last(j => edges.Contains((fromBase + i, toBase + j)));
            if (maxJ + 1 < b)
            {
                edges.Add((fromBase + i, toBase + maxJ + 1));
            }
        }
    }

    private static int Draw(ref PrngState rng, int n)
    {
        var (value, next) = Prng.NextInt(rng, n);
        rng = next;
        return (int)value;
    }
}
