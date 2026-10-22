using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Dungeon;

/// <summary>
/// Stage 1, seed -> room graph (game/design/ARCHITECTURE.md section 2). The contract (STATE.md D45, D63): exactly one
/// fork, immediately after the entrance room, into exactly <see cref="BranchCount"/> branches; each branch is a chain
/// of <see cref="MinBranchRooms"/> to <see cref="MaxBranchRooms"/> trash rooms (a seeded draw per branch); both
/// branches converge on the boss room and nowhere else. Forward-only (BIBLE section 9, D45 "no backtracking"): every
/// edge points deeper, nothing points back into the entrance, no edge joins the two branches, and the edge from the
/// entrance into a branch's first room is a one-way door (<see cref="RoomEdge.OneWayDoor"/>), so once a branch is
/// entered neither the entrance nor the untaken branch is reachable. Pure: the same seed gives the same graph.
/// Segments (STATE.md D65, <see cref="DungeonGenParams.Segments"/>): the shape above repeats S times end to end; each
/// mid boss room is the next segment's fork (its exits are one-way doors too), so no edge leads back into a finished
/// segment, and the final boss room is the last room. A dungeon of S segments has 1 + S * (two branches of 2 or 3
/// rooms + 1 boss room) rooms.
/// </summary>
public static class RoomGraphGen
{
    /// <summary>Branches out of the entrance (the one fork).</summary>
    public const int BranchCount = 2;

    /// <summary>Fewest trash rooms in one branch.</summary>
    public const int MinBranchRooms = 2;

    /// <summary>Most trash rooms in one branch.</summary>
    public const int MaxBranchRooms = 3;

    /// <summary>Generates the room graph for <paramref name="seed"/>.</summary>
    /// <param name="seed">The run seed.</param>
    /// <param name="p">Pipeline params (validated).</param>
    /// <returns>The graph.</returns>
    public static RoomGraph Generate(long seed, DungeonGenParams p)
    {
        ArgumentNullException.ThrowIfNull(p);
        p.Validate();
        var rng = DungeonPipeline.StageRng(seed, 1);

        // Ids are dense in (layer, lane) order: the entrance, then per segment each depth's branch rooms by lane, then
        // that segment's boss, which is the next segment's fork. Segment 0 draws first, so one segment is the
        // single-fork graph draw for draw.
        var nodes = new List<RoomNode> { new(0, 0, 0, RoomKind.Entrance) };
        var edges = new List<RoomEdge>();
        var fork = 0;
        for (var segment = 0; segment < p.Segments; segment++)
        {
            var lengths = new int[BranchCount];
            for (var b = 0; b < BranchCount; b++)
            {
                lengths[b] = MinBranchRooms + Draw(ref rng, MaxBranchRooms - MinBranchRooms + 1);
            }
            var forkLayer = nodes[fork].Layer;
            var bossLayer = forkLayer + lengths.Max() + 1;
            var idOf = new int[BranchCount, MaxBranchRooms + 1];
            for (var depth = 1; forkLayer + depth < bossLayer; depth++)
            {
                for (var b = 0; b < BranchCount; b++)
                {
                    if (depth <= lengths[b])
                    {
                        idOf[b, depth] = nodes.Count;
                        nodes.Add(new RoomNode(nodes.Count, forkLayer + depth, b, RoomKind.Trash, segment, b));
                    }
                }
            }
            var boss = nodes.Count;
            nodes.Add(new RoomNode(boss, bossLayer, 0, RoomKind.Boss, segment));

            for (var b = 0; b < BranchCount; b++)
            {
                edges.Add(new RoomEdge(fork, idOf[b, 1], OneWayDoor: true));
                for (var depth = 1; depth < lengths[b]; depth++)
                {
                    edges.Add(new RoomEdge(idOf[b, depth], idOf[b, depth + 1]));
                }
                edges.Add(new RoomEdge(idOf[b, lengths[b]], boss));
            }
            fork = boss;
        }
        edges.Sort((x, y) => x.From != y.From ? x.From.CompareTo(y.From) : x.To.CompareTo(y.To));
        return new RoomGraph(nodes, edges);
    }

    private static int Draw(ref PrngState rng, int n)
    {
        var (value, next) = Prng.NextInt(rng, n);
        rng = next;
        return (int)value;
    }
}
