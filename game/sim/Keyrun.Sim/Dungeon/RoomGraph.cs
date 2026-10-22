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
/// <param name="Layer">Depth from the entrance (0) to the boss (last); a branch room's layer is its depth in its branch.</param>
/// <param name="Lane">The branch a trash room belongs to (0 or 1); 0 for the entrance and the boss.</param>
/// <param name="Kind">Entrance, trash or boss.</param>
/// <param name="Segment">
/// The fork-and-boss segment the room belongs to (STATE.md D65): 0 for the entrance; a trash room's segment is the
/// segment whose boss its branch reaches; a boss room is its own segment's boss (and the next segment's fork).
/// </param>
/// <param name="Branch">A trash room's branch within its segment (0 or 1, equal to <paramref name="Lane"/>); -1 for the entrance and bosses.</param>
public readonly record struct RoomNode(int Id, int Layer, int Lane, RoomKind Kind, int Segment = 0, int Branch = -1);

/// <summary>A one-way connection from a room to a deeper room (D45: forward-only).</summary>
/// <param name="From">Room id.</param>
/// <param name="To">Room id.</param>
/// <param name="OneWayDoor">
/// True on the fork-to-branch edges (out of the entrance and out of every mid boss room): the door closes behind the party once the branch's first room is entered,
/// so the entrance and the untaken branch are no longer reachable (BIBLE section 9, D45 "no backtracking"). The run
/// enforces it at runtime in slice G6; the graph carries it so every consumer reads one fact.
/// </param>
public readonly record struct RoomEdge(int From, int To, bool OneWayDoor = false);

/// <summary>
/// A room DAG from one entrance to the final boss room (stage 1 of the pipeline). Edges only point deeper, so the
/// graph is forward-only and acyclic by construction; <see cref="RoomGraphGen"/> guarantees its shape: per segment,
/// one fork (the entrance, then each mid boss room) into two branches of two or three rooms that converge on that
/// segment's boss (STATE.md D45, D63, D65).
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
        BossRooms = nodes.Where(n => n.Kind == RoomKind.Boss).Select(n => n.Id).ToList();
        _succ = nodes.Select(n => (IReadOnlyList<int>)edges.Where(e => e.From == n.Id).Select(e => e.To).ToList()).ToArray();
        _pred = nodes.Select(n => (IReadOnlyList<int>)edges.Where(e => e.To == n.Id).Select(e => e.From).ToList()).ToArray();
    }

    /// <summary>Rooms in id order.</summary>
    public IReadOnlyList<RoomNode> Nodes { get; }

    /// <summary>Connections sorted by (from, to).</summary>
    public IReadOnlyList<RoomEdge> Edges { get; }

    /// <summary>The entrance room id (the single entrance node).</summary>
    public int EntranceId => Nodes.First(n => n.Kind == RoomKind.Entrance).Id;

    /// <summary>The final boss room id (the last boss node; the only one when there is one segment).</summary>
    public int BossId => BossRooms[BossRooms.Count - 1];

    /// <summary>Rooms in the dungeon.</summary>
    public int RoomCount => Nodes.Count;

    /// <summary>Every boss room id, ascending: segment s's boss is <c>BossRooms[s]</c>, the last is <see cref="BossId"/>.</summary>
    public IReadOnlyList<int> BossRooms { get; }

    /// <summary>Fork-and-boss segments (one boss each).</summary>
    public int SegmentCount => BossRooms.Count;

    /// <summary>The fork that opens segment <paramref name="segment"/>: the entrance for 0, else the previous segment's boss.</summary>
    public int SegmentEntry(int segment) => segment == 0 ? EntranceId : BossRooms[segment - 1];

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
            sb.Append(e.From.ToString(CultureInfo.InvariantCulture)).Append(e.OneWayDoor ? "|>" : ">").Append(e.To.ToString(CultureInfo.InvariantCulture)).Append(',');
        }
        return sb.ToString();
    }
}
