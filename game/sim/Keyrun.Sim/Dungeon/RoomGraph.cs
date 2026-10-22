using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Keyrun.Sim.Dungeon;

/// <summary>What a room is for in the graph.</summary>
public enum RoomKind
{
    /// <summary>Where the party starts (layer 0).</summary>
    Entrance,

    /// <summary>A room on the way that can hold trash packs.</summary>
    Trash,

    /// <summary>The boss room (the last layer).</summary>
    Boss,
}

/// <summary>One room of the graph.</summary>
/// <param name="Id">Room id, dense from 0 in layer then lane order.</param>
/// <param name="Layer">Depth from the entrance (0) to the boss (last).</param>
/// <param name="Lane">Index within its layer, 0-based.</param>
/// <param name="Kind">Entrance, trash or boss.</param>
public readonly record struct RoomNode(int Id, int Layer, int Lane, RoomKind Kind);

/// <summary>A one-way connection from a room to a room exactly one layer deeper (D45: forward-only).</summary>
/// <param name="From">Room id.</param>
/// <param name="To">Room id.</param>
public readonly record struct RoomEdge(int From, int To);

/// <summary>
/// A layered room DAG from one entrance to one boss room (stage 1 of the pipeline). Edges only go one layer deeper,
/// so the graph is forward-only and acyclic by construction; <see cref="RoomGraphGen"/> also guarantees a fork and a
/// single rejoin room just before the boss.
/// </summary>
public sealed class RoomGraph
{
    private readonly IReadOnlyList<int>[] _succ;
    private readonly IReadOnlyList<int>[] _pred;

    /// <summary>Builds a graph; nodes must be dense ids 0..n-1 in id order.</summary>
    /// <param name="nodes">Rooms, in id order.</param>
    /// <param name="edges">Connections, sorted by (from, to).</param>
    /// <exception cref="ArgumentException">Ids not dense, or an edge naming an unknown room.</exception>
    public RoomGraph(IReadOnlyList<RoomNode> nodes, IReadOnlyList<RoomEdge> edges)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        ArgumentNullException.ThrowIfNull(edges);
        for (var i = 0; i < nodes.Count; i++)
        {
            if (nodes[i].Id != i)
            {
                throw new ArgumentException("room ids must be dense and in order", nameof(nodes));
            }
        }
        if (edges.Any(e => e.From < 0 || e.From >= nodes.Count || e.To < 0 || e.To >= nodes.Count))
        {
            throw new ArgumentException("an edge names an unknown room", nameof(edges));
        }
        Nodes = nodes;
        Edges = edges;
        _succ = nodes.Select(n => (IReadOnlyList<int>)edges.Where(e => e.From == n.Id).Select(e => e.To).ToList()).ToArray();
        _pred = nodes.Select(n => (IReadOnlyList<int>)edges.Where(e => e.To == n.Id).Select(e => e.From).ToList()).ToArray();
    }

    /// <summary>Rooms in id order.</summary>
    public IReadOnlyList<RoomNode> Nodes { get; }

    /// <summary>Connections sorted by (from, to).</summary>
    public IReadOnlyList<RoomEdge> Edges { get; }

    /// <summary>The entrance room id (the single entrance node).</summary>
    public int EntranceId => Nodes.First(n => n.Kind == RoomKind.Entrance).Id;

    /// <summary>The boss room id (the single boss node).</summary>
    public int BossId => Nodes.First(n => n.Kind == RoomKind.Boss).Id;

    /// <summary>Layers from entrance to boss inclusive.</summary>
    public int LayerCount => Nodes.Max(n => n.Layer) + 1;

    /// <summary>Rooms one step deeper from <paramref name="id"/>, ascending.</summary>
    public IReadOnlyList<int> Successors(int id) => _succ[id];

    /// <summary>Rooms one step shallower that lead to <paramref name="id"/>, ascending.</summary>
    public IReadOnlyList<int> Predecessors(int id) => _pred[id];

    /// <summary>Every entrance-to-boss route as room ids, in lexicographic order.</summary>
    public IReadOnlyList<IReadOnlyList<int>> Routes()
    {
        var routes = new List<IReadOnlyList<int>>();
        var path = new List<int> { EntranceId };
        Walk(path, routes);
        return routes;
    }

    private void Walk(List<int> path, List<IReadOnlyList<int>> routes)
    {
        var at = path[path.Count - 1];
        if (at == BossId)
        {
            routes.Add(path.ToList());
            return;
        }
        foreach (var next in _succ[at])
        {
            path.Add(next);
            Walk(path, routes);
            path.RemoveAt(path.Count - 1);
        }
    }

    /// <summary>A stable text form of the shape (layer widths, then edges), for equality across seeds.</summary>
    public string Signature()
    {
        var sb = new StringBuilder();
        for (var layer = 0; layer < LayerCount; layer++)
        {
            sb.Append(Nodes.Count(n => n.Layer == layer).ToString(CultureInfo.InvariantCulture)).Append('|');
        }
        foreach (var e in Edges)
        {
            sb.Append(e.From.ToString(CultureInfo.InvariantCulture)).Append('>').Append(e.To.ToString(CultureInfo.InvariantCulture)).Append(',');
        }
        return sb.ToString();
    }
}
