using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Keyrun.Sim.Core;
using Keyrun.Sim.Dungeon;
using C = Keyrun.Content;

namespace Keyrun.Bots.Shared;

/// <summary>One seeded dungeon armed for a run: the registry to build the run from, the walked branch and every stage's output.</summary>
/// <param name="Registry">Content with the seeded layout and the walked branch's pulls (feed it to HostRunSetup.Build).</param>
/// <param name="Branch">The branch the party walks (<see cref="SeededLayout.BotBranch"/>).</param>
/// <param name="Graph">Stage 1: the room graph.</param>
/// <param name="Grid">Stage 2: the walkable grid the boxes are cut from.</param>
/// <param name="Fill">Stage 4: the branch fill.</param>
public sealed record SeededDungeon(C.ContentRegistry Registry, int Branch, RoomGraph Graph, WalkGrid Grid, BranchFillResult Fill);

/// <summary>
/// The seeded dungeon (STATE.md D45/D46, f-46043bc6) as the run's layout, in the pipeline's order: seed -> room graph
/// (<see cref="RoomGraphGen"/>) -> walkable grid (<see cref="WalkGridGen"/>) -> fill (<see cref="BranchFill"/> over the
/// content roster, <see cref="SeededRoster"/>) -> layout boxes. The run walks one branch: <see cref="C.LayoutDef.Boxes"/>
/// and <see cref="C.LayoutDef.Route"/> are the entrance, the walked branch's rooms and corridors and the boss room, so the
/// bots' route follow, the pull triggers and the view need no new rules; the other branch is
/// <see cref="C.LayoutDef.SideBoxes"/> with its packs, drawn beside it. Each corridor component of the grid becomes one
/// box (straight) or three (out, along its lane, in), every box overlapping the next at its doorway. The run's pulls are
/// the walked branch's teaching pulls; the other branch's teaching pulls stand there as packs. The layout is validated by
/// the content's own <see cref="C.LayoutValidator"/> (fail closed). Pure over (content, seed).
/// </summary>
public static class SeededLayout
{
    /// <summary>The PRNG stage of the bots' route pick (7; 1 graph, 4 fill, 5 and 6 reserved).</summary>
    public const uint RouteStage = 7;

    /// <summary>
    /// The generator params the run uses: 24 m rooms (a teaching pull stands 11.5 m from its door on the centre row, and
    /// the side packs stand 9 m off it, so a teaching fight does not drift into a pack's ring) and 3 m corridors (the hand
    /// corridors' width: the formation's 1 m side slots stay off the walls).
    /// </summary>
    public static readonly DungeonGenParams Params = DungeonGenParams.Default with { RoomCells = 24, CorridorCells = 3 };

    /// <summary>
    /// Off-line pack slot offsets from a room's west wall (x) and centre row (z), mm, by fill slot: the four corners, then
    /// the two side midpoints. Every slot is 9 m or more off the centre row (a pack's 4 m ring plus the 1 m formation side
    /// clears the walking line, so every placed pack is a skippable one, LayoutValidator SKIPPABLE-PACKS).
    /// </summary>
    private static readonly (int Depth, int Side)[] SlotOffsets =
    {
        (4000, -9000), (4000, 9000), (20000, -9000), (20000, 9000), (12000, -9500), (12000, 9500),
    };

    /// <summary>A teaching pull's spawns stand this far from its room's door (LayoutValidator: 11180..12000).</summary>
    public const int PullDepthMm = 11500;

    /// <summary>A pull room's start (a <c>--pull N</c> run) and the party's start stand this far from the door.</summary>
    public const int StartDepthMm = 4500;

    /// <summary>The box id of graph room <paramref name="roomId"/>.</summary>
    public static string RoomBoxId(int roomId) => string.Create(CultureInfo.InvariantCulture, $"room-{roomId}");

    /// <summary>The branch the autorun party walks for <paramref name="seed"/> (0 or 1): one seeded draw on its own stage.</summary>
    public static int BotBranch(long seed)
    {
        var (value, _) = Prng.NextInt(DungeonPipeline.StageRng(seed, RouteStage), RoomGraphGen.BranchCount);
        return (int)value;
    }

    /// <summary>The pull id of the other branch's copy of a teaching pull both branches field (the Choir): "choir-2".</summary>
    public const string MirrorSuffix = "-2";

    /// <summary>Generates, fills and lays out the dungeon for <paramref name="seed"/>.</summary>
    /// <param name="reg">Loaded content (its hand-built layout supplies the formation and the boss arena's size and ring).</param>
    /// <param name="seed">The run seed.</param>
    /// <param name="walk">
    /// The branch the route chain follows, or null for the bots' branch (<see cref="BotBranch"/>). The geometry is the same
    /// either way (the boss room is centred on the bots' door), so a second layout with the other branch as its chain gives
    /// a seat the route down that branch over the same boxes.
    /// </param>
    /// <returns>The armed dungeon.</returns>
    /// <exception cref="C.ContentLoadException">The seeded layout breaks a layout invariant.</exception>
    /// <exception cref="InvalidOperationException">The grid does not cut into boxes (a corridor that is no straight or three-leg run).</exception>
    public static SeededDungeon Build(C.ContentRegistry reg, long seed, int? walk = null)
    {
        ArgumentNullException.ThrowIfNull(reg);
        var graph = RoomGraphGen.Generate(seed, Params);
        var grid = WalkGridGen.Emit(graph, Params);
        var fill = BranchFill.Fill(seed, graph, SeededRoster.Teaching(reg), SeededRoster.Optional(reg), SeededRoster.Params(reg));
        var bots = BotBranch(seed);
        var branch = walk ?? bots;
        var corridors = Corridors(graph, grid);
        var hand = reg.Layout;
        var arena = hand.Boxes.First(b => string.Equals(b.Pull, C.LayoutDef.BossPullId, StringComparison.Ordinal));
        var dungeonId = reg.Forces.Route.DungeonId;
        var content = reg.Dungeons[dungeonId];
        var pullById = content.Pulls.ToDictionary(p => p.Id, StringComparer.Ordinal);
        var walked = fill.Branches[branch];
        var side = fill.Branches[1 - branch];

        var bossDoor = corridors[(fill.Branches[bots].Rooms[^1], graph.BossId)][^1];
        var doorZ = Mid(bossDoor.MinZ, bossDoor.MaxZ);
        var rooms = new Dictionary<int, C.LayoutBox>();
        foreach (var n in graph.Nodes)
        {
            rooms[n.Id] = RoomBox(grid, n, n.Kind == RoomKind.Boss ? arena : null, doorZ);
        }
        // D45 (the player chooses the route): every branch room carries its branch, and both branches' teaching pulls hold
        // their rooms as pulls (the walked branch's under their content ids, the other's mirror of a shared pull as id-2);
        // the boss holds the boss room.
        foreach (var br in fill.Branches)
        {
            foreach (var r in br.Rooms)
            {
                rooms[r] = rooms[r] with { Branch = BranchTag(br.Index) };
            }
        }
        foreach (var p in fill.Packs.Where(p => p.PullIndex >= 0))
        {
            rooms[p.RoomId] = PullRoom(rooms[p.RoomId], PullIdOf(p, branch), OriginDepth(pullById[p.PackId]), null);
        }
        rooms[graph.BossId] = PullRoom(rooms[graph.BossId], C.LayoutDef.BossPullId, PullDepthMm, arena.AggroMm, doorZ);

        var boxes = new List<C.LayoutBox> { rooms[graph.EntranceId] };
        var chain = new List<int> { graph.EntranceId };
        chain.AddRange(walked.Rooms);
        chain.Add(graph.BossId);
        for (var k = 0; k + 1 < chain.Count; k++)
        {
            boxes.AddRange(corridors[(chain[k], chain[k + 1])]);
            boxes.Add(rooms[chain[k + 1]]);
        }
        var sideBoxes = new List<C.LayoutBox>();
        var sideChain = new List<int> { graph.EntranceId };
        sideChain.AddRange(side.Rooms);
        sideChain.Add(graph.BossId);
        for (var k = 0; k + 1 < sideChain.Count; k++)
        {
            sideBoxes.AddRange(corridors[(sideChain[k], sideChain[k + 1])]);
            if (k + 2 < sideChain.Count)
            {
                sideBoxes.Add(rooms[sideChain[k + 1]]);
            }
        }

        var route = new List<C.LayoutPoint>();
        for (var k = 0; k + 1 < boxes.Count; k++)
        {
            route.Add(DoorPoint(boxes[k], boxes[k + 1]));
        }
        route.Add(boxes[^1].Origin!);

        var walkedPacks = fill.Packs.Where(p => p.Branch == branch && p.PullIndex < 0).Select(p => Pack(reg, p, rooms[p.RoomId], walked.Rooms[0])).ToList();
        var sidePacks = fill.Packs.Where(p => p.Branch != branch && p.PullIndex < 0).Select(p => Pack(reg, p, rooms[p.RoomId], side.Rooms[0])).ToList();
        var entrance = rooms[graph.EntranceId];
        var layout = new C.LayoutDef
        {
            Dungeon = hand.Dungeon,
            Start = new C.LayoutPoint { X = entrance.MinX + StartDepthMm, Z = Mid(entrance.MinZ, entrance.MaxZ) },
            Boxes = new C.ValueList<C.LayoutBox>(boxes),
            Route = new C.ValueList<C.LayoutPoint>(route),
            Packs = new C.ValueList<C.LayoutPack>(walkedPacks.Concat(sidePacks)),
            Formation = hand.Formation,
            SideBoxes = new C.ValueList<C.LayoutBox>(sideBoxes),
        };
        // The run's pulls: the walked branch's teaching pulls in order, then the other branch's (PlaceDungeon tags each with
        // its room's branch; Pulls.CheckPullAdvance skips the branch the party did not take), then the host's boss.
        C.PullDef PullOf(FilledPack p) => pullById[p.PackId] with { Id = PullIdOf(p, branch) };
        var walkedPulls = fill.Packs.Where(p => p.Branch == branch && p.PullIndex >= 0).OrderBy(p => p.PullIndex).Select(PullOf).ToList();
        var sidePulls = fill.Packs.Where(p => p.Branch != branch && p.PullIndex >= 0).OrderBy(p => p.PullIndex).Select(PullOf).ToList();
        var dungeon = content with { Pulls = new C.ValueList<C.PullDef>(walkedPulls.Concat(sidePulls)) };
        var armed = reg with
        {
            Layout = layout,
            Dungeons = new C.ValueMap<C.DungeonDef>(reg.Dungeons.Select(kv => kv.Key == dungeonId ? new KeyValuePair<string, C.DungeonDef>(kv.Key, dungeon) : kv)),
        };
        // Fail closed: the walked chain, with its own pulls and packs, obeys every layout invariant (the other branch is the
        // same construction mirrored; Build(reg, seed, walk: other) validates it as a chain).
        var walkedOnly = content with { Pulls = new C.ValueList<C.PullDef>(walkedPulls) };
        C.LayoutValidator.Check(armed with
        {
            Layout = layout with { Packs = new C.ValueList<C.LayoutPack>(walkedPacks), SideBoxes = new() },
            Dungeons = new C.ValueMap<C.DungeonDef>(reg.Dungeons.Select(kv => kv.Key == dungeonId ? new KeyValuePair<string, C.DungeonDef>(kv.Key, walkedOnly) : kv)),
        });
        return new SeededDungeon(armed, branch, graph, grid, fill);
    }

    // A grid room as a layout box: its cells in mm. The boss room is widened and deepened to the hand arena's size (its
    // fight needs the space), anchored on its west wall and centred on the walked door's row (doorZ) while still holding
    // its grid cells, so both branches' doors stay on its wall and the walk in is straight along +x, like the hand route
    // (the formation stands behind the leader along -x, so a diagonal turn at the door would put a slot in the wall).
    private static C.LayoutBox RoomBox(WalkGrid grid, RoomNode n, C.LayoutBox? arena, int doorZ)
    {
        var r = grid.Rooms[n.Id];
        var c = grid.CellMm;
        var minX = (int)(grid.OriginX + ((long)r.MinCx * c));
        var maxX = (int)(grid.OriginX + ((long)(r.MaxCx + 1) * c));
        var minZ = (int)(grid.OriginZ + ((long)r.MinCz * c));
        var maxZ = (int)(grid.OriginZ + ((long)(r.MaxCz + 1) * c));
        if (arena is not null)
        {
            maxX = Math.Max(maxX, minX + (arena.MaxX - arena.MinX));
            var half = (arena.MaxZ - arena.MinZ) / 2;
            (minZ, maxZ) = (Math.Min(minZ, doorZ - half), Math.Max(maxZ, doorZ + half));
        }
        return new C.LayoutBox { Id = RoomBoxId(n.Id), Kind = C.LayoutBoxKind.Room, MinX = minX, MaxX = maxX, MinZ = minZ, MaxZ = maxZ };
    }

    // A pull room (LayoutValidator.CheckPullRoom): trigger = the room less 2000 mm from its west door; the spawns stand
    // PullDepthMm from the door on row z (the centre row, or the boss room's walked door row); the start 4500 mm in.
    private static C.LayoutBox PullRoom(C.LayoutBox room, string pullId, int originDepth, int? aggroMm, int? originZ = null)
    {
        var z = originZ ?? Mid(room.MinZ, room.MaxZ);
        return room with
        {
            Pull = pullId,
            Trigger = new C.LayoutRect { MinX = room.MinX + C.LayoutValidator.TriggerInsetMm, MaxX = room.MaxX, MinZ = room.MinZ, MaxZ = room.MaxZ },
            Origin = new C.LayoutPoint { X = room.MinX + originDepth, Z = z },
            Start = new C.LayoutPoint { X = room.MinX + StartDepthMm, Z = z },
            AggroMm = aggroMm,
        };
    }

    // The origin's depth so the pull's spawns (one x offset per pull in content) stand PullDepthMm from the door.
    private static int OriginDepth(C.PullDef pull) => PullDepthMm - pull.Enemies.Max(e => e.Spawn.X);

    private static C.LayoutPoint SlotPoint(C.LayoutBox room, int slot) =>
        new() { X = room.MinX + SlotOffsets[slot].Depth, Z = Mid(room.MinZ, room.MaxZ) + SlotOffsets[slot].Side };

    private static C.LayoutPack Pack(C.ContentRegistry reg, FilledPack p, C.LayoutBox room, int branchRoom)
    {
        var def = reg.Layout.Packs.First(k => string.Equals(k.Id, p.PackId, StringComparison.Ordinal));
        return def with { Id = p.InstanceId, Room = room.Id, Center = SlotPoint(room, p.Slot), Optional = true, Branch = RoomBoxId(branchRoom) };
    }

    // A branch's tag on its rooms and pulls ("0" or "1").
    private static string BranchTag(int branch) => branch.ToString(CultureInfo.InvariantCulture);

    // A teaching placement's pull id: its content id on the walked branch, the mirror id on the other branch for a pull
    // both branches field (the first teaching pull, the Choir), its content id otherwise (the Pair or the Seed appears once).
    private static string PullIdOf(FilledPack p, int walked) =>
        p.Branch != walked && p.PullIndex == 0 ? p.PackId + MirrorSuffix : p.PackId;

    // The doorway point between consecutive boxes: the centre of their overlap.
    private static C.LayoutPoint DoorPoint(C.LayoutBox a, C.LayoutBox b) => new()
    {
        X = Mid(Math.Max(a.MinX, b.MinX), Math.Min(a.MaxX, b.MaxX)),
        Z = Mid(Math.Max(a.MinZ, b.MinZ), Math.Min(a.MaxZ, b.MaxZ)),
    };

    private static int Mid(int lo, int hi) => lo + ((hi - lo) / 2);

    /// <summary>
    /// Every graph edge's corridor as layout boxes, cut from the grid: the walkable cells outside every room, in 4-connected
    /// components, each touching exactly the edge's two rooms. A component is one straight run (one box) or out, along a
    /// lane, in (three boxes); boxes overlap the next by a cell (rooms) or by the shared lane column, so each box
    /// starts east of the one before it (LayoutValidator: the route continues along +x).
    /// </summary>
    internal static IReadOnlyDictionary<(int From, int To), IReadOnlyList<C.LayoutBox>> Corridors(RoomGraph graph, WalkGrid grid)
    {
        var owner = new int[grid.Width * grid.Height];
        Array.Fill(owner, -1);
        foreach (var r in grid.Rooms)
        {
            for (var z = r.MinCz; z <= r.MaxCz; z++)
            {
                for (var x = r.MinCx; x <= r.MaxCx; x++)
                {
                    owner[(z * grid.Width) + x] = r.RoomId;
                }
            }
        }
        var seen = new bool[owner.Length];
        var result = new Dictionary<(int, int), IReadOnlyList<C.LayoutBox>>();
        for (var start = 0; start < owner.Length; start++)
        {
            if (seen[start] || owner[start] >= 0 || grid.Cells[start] == 0)
            {
                continue;
            }
            var cells = new List<(int X, int Z)>();
            var touches = new SortedSet<int>();
            var queue = new Queue<int>();
            queue.Enqueue(start);
            seen[start] = true;
            while (queue.Count > 0)
            {
                var i = queue.Dequeue();
                var (x, z) = (i % grid.Width, i / grid.Width);
                cells.Add((x, z));
                foreach (var (nx, nz) in new[] { (x + 1, z), (x - 1, z), (x, z + 1), (x, z - 1) })
                {
                    if (!grid.IsWalkable(nx, nz))
                    {
                        continue;
                    }
                    var j = (nz * grid.Width) + nx;
                    if (owner[j] >= 0)
                    {
                        touches.Add(owner[j]);
                    }
                    else if (!seen[j])
                    {
                        seen[j] = true;
                        queue.Enqueue(j);
                    }
                }
            }
            if (touches.Count != 2)
            {
                throw new InvalidOperationException($"seeded layout: a corridor touches {touches.Count} rooms, want 2");
            }
            var (a, b) = (touches.Min, touches.Max);
            if (graph.Nodes[a].Layer > graph.Nodes[b].Layer)
            {
                (a, b) = (b, a);
            }
            if (!graph.Successors(a).Contains(b))
            {
                throw new InvalidOperationException($"seeded layout: a corridor links rooms {a} and {b}, which no edge joins");
            }
            var oneWay = graph.Edges.Single(e => e.From == a && e.To == b).OneWayDoor;
            result.Add((a, b), Cut(grid, cells, grid.Rooms[a], grid.Rooms[b], a, b, oneWay));
        }
        if (result.Count != graph.Edges.Count)
        {
            throw new InvalidOperationException($"seeded layout: {result.Count} corridors for {graph.Edges.Count} edges");
        }
        return result;
    }

    private static IReadOnlyList<C.LayoutBox> Cut(WalkGrid grid, List<(int X, int Z)> cells, RoomRect from, RoomRect to, int a, int b, bool oneWay)
    {
        var exit = cells.Where(c => c.X == from.MaxCx + 1).Select(c => c.Z).ToList();
        var entry = cells.Where(c => c.X == to.MinCx - 1).Select(c => c.Z).ToList();
        if (exit.Count == 0 || entry.Count == 0)
        {
            throw new InvalidOperationException($"seeded layout: corridor {a}>{b} does not leave the east wall and enter the west wall");
        }
        var (e0, e1, i0, i1) = (exit.Min(), exit.Max(), entry.Min(), entry.Max());
        var rects = new List<(int X0, int Z0, int X1, int Z1)>();
        if (e0 == i0 && e1 == i1)
        {
            rects.Add((from.MaxCx + 1, e0, to.MinCx - 1, e1));
        }
        else
        {
            // The lane's columns are the ones taller than a band (the out and in runs are one band tall).
            var band = e1 - e0 + 1;
            var laneCols = cells.GroupBy(c => c.X).Where(g => g.Count() > band).Select(g => g.Key).ToList();
            var (l0, l1) = (laneCols.Min(), laneCols.Max());
            rects.Add((from.MaxCx + 1, e0, l1, e1));
            rects.Add((l0, Math.Min(e0, i0), l1, Math.Max(e1, i1)));
            rects.Add((l0, i0, to.MinCx - 1, i1));
        }
        var covered = new HashSet<(int, int)>();
        foreach (var (x0, z0, x1, z1) in rects)
        {
            for (var z = z0; z <= z1; z++)
            {
                for (var x = x0; x <= x1; x++)
                {
                    covered.Add((x, z));
                }
            }
        }
        if (!covered.SetEquals(cells))
        {
            throw new InvalidOperationException($"seeded layout: corridor {a}>{b} is no straight or three-leg run");
        }

        var c = grid.CellMm;
        var boxes = new List<C.LayoutBox>();
        for (var k = 0; k < rects.Count; k++)
        {
            var (x0, z0, x1, z1) = rects[k];
            var minX = (int)(grid.OriginX + ((long)x0 * c));
            var maxX = (int)(grid.OriginX + ((long)(x1 + 1) * c));
            if (k == 0)
            {
                minX -= c; // a cell into the room it leaves: the doorway point stands half a cell (more than Follow.ArriveMm) in each box
            }
            if (k == rects.Count - 1)
            {
                maxX += c; // a cell into the room it enters
            }
            if (rects.Count == 3 && k == 2)
            {
                minX = (int)(grid.OriginX + ((long)rects[1].X1 * c)); // start on the lane's last column, east of the lane's start
            }
            boxes.Add(new C.LayoutBox
            {
                Id = string.Create(CultureInfo.InvariantCulture, $"hall-{a}-{b}-{k}"),
                Kind = C.LayoutBoxKind.Corridor,
                MinX = minX,
                MaxX = maxX,
                MinZ = (int)(grid.OriginZ + ((long)z0 * c)),
                MaxZ = (int)(grid.OriginZ + ((long)(z1 + 1) * c)),
                OneWay = oneWay && k == 0,
            });
        }
        return boxes;
    }
}
