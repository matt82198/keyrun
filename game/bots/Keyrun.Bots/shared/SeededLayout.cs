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

/// <summary>A walked chain through one branch per segment (<see cref="SeededLayout.Walk"/>).</summary>
/// <param name="Chain">Room ids in walking order: the entrance, each segment's walked branch rooms and boss.</param>
/// <param name="Boxes">The route boxes: rooms and corridors in walking order, each overlapping the next at its doorway.</param>
/// <param name="SideBoxes">The untaken branches' corridors and rooms, segment then branch then walking order.</param>
public sealed record SegmentWalk(IReadOnlyList<int> Chain, IReadOnlyList<C.LayoutBox> Boxes, IReadOnlyList<C.LayoutBox> SideBoxes);

/// <summary>A multi-segment seeded dungeon filled and walked (<see cref="SeededLayout.BuildSegmented"/>).</summary>
/// <param name="DungeonId">The dungeon.</param>
/// <param name="Graph">Stage 1: the room graph (one segment per boss).</param>
/// <param name="Grid">Stage 2: the walkable grid.</param>
/// <param name="Fill">Stage 4: the segment fill (a pack per trash room, a boss per boss room).</param>
/// <param name="Branches">The branch the party walks in each segment.</param>
/// <param name="Walk">The walked chain, route boxes and side boxes.</param>
public sealed record SegmentedDungeon(string DungeonId, RoomGraph Graph, WalkGrid Grid, SegmentFillResult Fill, IReadOnlyList<int> Branches, SegmentWalk Walk);

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
    /// the side packs stand 9 m off it, so a teaching fight does not drift into a pack's ring) and 9 m corridors (f-9607c9c0,
    /// Matt 2026-10-22 "the corridors are too narrow": three times the largest pack footprint the content ships, 2.7 m, so
    /// three seats and a pack fit abreast; the formation's 1 m side slots stay off the walls).
    /// </summary>
    public static readonly DungeonGenParams Params = DungeonGenParams.Default with { RoomCells = 24, CorridorCells = 9 };

    /// <summary>
    /// f-1232d0aa: a required pack or a teaching pull stands this far off its room's walking row (z), mm, alternating sides by
    /// room: more than the largest pack footprint (2.7 m) from the room centre, and inside every pack's 4 m ring so walking
    /// forward still pulls it. A player can pull it alone, or step round it.
    /// </summary>
    public const int OffsetMm = 3000;

    /// <summary>A required pack stands this far from its room's west wall, mm: past the door, flanking the far exit.</summary>
    public const int RequiredDepthMm = 14000;

    // The side of the walking row a room's required pack or pull stands on: +1 or -1, alternating by room id.
    private static int OffsetSide(int roomId) => roomId % 2 == 0 ? 1 : -1;

    // A pack placed in a corridor stands mid-hall on its first leg (a cell in from the room it leaves), on the walking row.
    private static C.LayoutPoint HallPoint(C.LayoutBox hall) => new()
    {
        X = Mid(hall.MinX + Params.CellMm, hall.MaxX),
        Z = Mid(hall.MinZ, hall.MaxZ),
    };

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
    public static int BotBranch(long seed) => BotBranches(seed, 1)[0];

    /// <summary>
    /// The branch the autorun party walks in each of <paramref name="segments"/> segments (STATE.md D65): consecutive
    /// draws on the route stage, so segment 0's pick is <see cref="BotBranch"/>.
    /// </summary>
    public static IReadOnlyList<int> BotBranches(long seed, int segments)
    {
        var rng = DungeonPipeline.StageRng(seed, RouteStage);
        var picks = new int[segments];
        for (var s = 0; s < segments; s++)
        {
            var (value, next) = Prng.NextInt(rng, RoomGraphGen.BranchCount);
            picks[s] = (int)value;
            rng = next;
        }
        return picks;
    }

    /// <summary>
    /// The walked chain of a (multi-segment) graph through one branch per segment, as layout boxes (STATE.md D65):
    /// <see cref="SegmentWalk.Boxes"/> is the entrance, then per segment the corridors and rooms of the walked branch up
    /// to and including that segment's boss room; <see cref="SegmentWalk.SideBoxes"/> is every untaken branch's
    /// corridors and rooms (the fork and boss rooms they share are on the route). Room boxes come from
    /// <paramref name="roomBox"/>, or are the plain grid rooms when it is null.
    /// </summary>
    /// <param name="graph">The room graph.</param>
    /// <param name="grid">Its walk grid.</param>
    /// <param name="branches">The walked branch per segment (0 or 1), one entry per segment.</param>
    /// <param name="roomBox">The layout box of a room, or null for its grid rectangle in mm.</param>
    /// <returns>The walk.</returns>
    /// <exception cref="ArgumentException">A branch list that is not one 0-or-1 per segment.</exception>
    public static SegmentWalk Walk(RoomGraph graph, WalkGrid grid, IReadOnlyList<int> branches, Func<RoomNode, C.LayoutBox>? roomBox = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(branches);
        if (branches.Count != graph.SegmentCount || branches.Any(b => b < 0 || b >= RoomGraphGen.BranchCount))
        {
            throw new ArgumentException($"seeded layout: need one branch (0 or 1) per segment, {graph.SegmentCount} segments", nameof(branches));
        }
        roomBox ??= n => RoomBox(grid, n, null, 0);
        var corridors = Corridors(graph, grid);
        var chain = new List<int> { graph.EntranceId };
        var boxes = new List<C.LayoutBox> { roomBox(graph.Nodes[graph.EntranceId]) };
        var sideBoxes = new List<C.LayoutBox>();
        for (var s = 0; s < graph.SegmentCount; s++)
        {
            var fork = graph.SegmentEntry(s);
            var boss = graph.BossRooms[s];
            var roots = graph.Successors(fork);
            for (var b = 0; b < roots.Count; b++)
            {
                var rooms = BranchRooms(graph, roots[b], boss);
                var hops = new List<int> { fork };
                hops.AddRange(rooms);
                hops.Add(boss);
                for (var k = 0; k + 1 < hops.Count; k++)
                {
                    var legs = corridors[(hops[k], hops[k + 1])];
                    if (b == branches[s])
                    {
                        boxes.AddRange(legs);
                        boxes.Add(roomBox(graph.Nodes[hops[k + 1]]));
                        chain.Add(hops[k + 1]);
                    }
                    else
                    {
                        sideBoxes.AddRange(legs);
                        if (k + 2 < hops.Count)
                        {
                            sideBoxes.Add(roomBox(graph.Nodes[hops[k + 1]]));
                        }
                    }
                }
            }
        }
        return new SegmentWalk(chain, boxes, sideBoxes);
    }

    // The trash rooms from a fork's successor up to (not including) its segment's boss.
    private static List<int> BranchRooms(RoomGraph graph, int root, int boss)
    {
        var rooms = new List<int>();
        for (var at = root; at != boss; at = graph.Successors(at).Single())
        {
            rooms.Add(at);
        }
        return rooms;
    }

    /// <summary>
    /// A multi-segment dungeon (the Sunken Bellfoundry, D65/D67) generated, filled and walked for <paramref name="seed"/>:
    /// one segment per entry of the dungeon's segment list, every trash room filled from its pack pool, each segment's boss
    /// in its boss room, and the walk the bots take (one seeded branch per segment, <see cref="BotBranches"/>) through the
    /// segment 1 boss, the segment 2 boss and the final boss in order. The walk's room boxes are the plain grid rooms.
    /// </summary>
    /// <param name="reg">Loaded content.</param>
    /// <param name="dungeonId">The dungeon.</param>
    /// <param name="seed">The run seed.</param>
    /// <param name="leaderBranch">
    /// The branch (0 or 1) the leader is forced onto in every segment (the playtest's LeaderBranch option), or null
    /// (the default) for the seeded <see cref="BotBranches"/> pick.
    /// </param>
    /// <returns>The dungeon.</returns>
    /// <exception cref="InvalidOperationException">An unknown dungeon, or a pool that cannot lift every route to the forces bar.</exception>
    public static SegmentedDungeon BuildSegmented(C.ContentRegistry reg, string dungeonId, long seed, int? leaderBranch = null)
    {
        ArgumentNullException.ThrowIfNull(reg);
        var bosses = SeededRoster.SegmentBosses(reg, dungeonId);
        var p = Params with { Segments = bosses.Count };
        var graph = RoomGraphGen.Generate(seed, p);
        var grid = WalkGridGen.Emit(graph, p);
        var fill = SegmentFill.Fill(seed, graph, bosses, SeededRoster.Pool(reg, dungeonId), reg.ForcesFor(dungeonId).Required);
        var branches = leaderBranch is { } forced
            ? Enumerable.Repeat(forced, graph.SegmentCount).ToList()
            : BotBranches(seed, graph.SegmentCount);
        // R-BRANCH-WALK-1: the run's nav set is both branches (NavLayouts.ToSim: route boxes plus NavLayout.SideBoxes), so the
        // dungeon's Walk.Boxes is the walkable set, the walked chain first; SideBoxes stays the untaken branch alone.
        var walk = Walk(graph, grid, branches);
        return new SegmentedDungeon(dungeonId, graph, grid, fill, branches, walk with { Boxes = walk.Boxes.Concat(walk.SideBoxes).ToList() });
    }

    /// <summary>
    /// R-MULTIBOSS-1: a multi-segment dungeon (<see cref="BuildSegmented"/>) as the run's content: <paramref name="reg"/> with its
    /// layout replaced by the seeded one, so the host's setup (HostRunSetup.Build) and the bots need no new rules. The layout is
    /// the walked chain (<see cref="SegmentWalk.Boxes"/>): the entrance, then per segment the walked branch's rooms and
    /// corridors up to that segment's boss room. Each boss room holds its boss pull (<see cref="C.LayoutDef.BossPullIds"/>:
    /// "boss-s1", "boss-s2", then "boss"), the arena's size in z and, for the final boss, in x. The walked branch's packs are
    /// required packs on the walking line (the party pulls each as it walks through, so the forces bar fills on the way to the
    /// last boss); the untaken branches' packs are optional packs off the line, beside the route. The dungeon's own pulls
    /// stay empty (trash is packs). The walked chain obeys every layout invariant (<see cref="C.LayoutValidator"/>, fail closed).
    /// </summary>
    /// <param name="reg">Loaded content (the hand layout supplies the formation and the boss arena's size and ring).</param>
    /// <param name="dungeonId">A multi-segment dungeon, e.g. "sunken-bellfoundry".</param>
    /// <param name="seed">The run seed.</param>
    /// <param name="leaderBranch">The branch forced in every segment (<see cref="BuildSegmented"/>), or null for the seeded pick.</param>
    /// <returns>The content with the seeded layout.</returns>
    /// <exception cref="C.ContentLoadException">The seeded layout breaks a layout invariant.</exception>
    /// <exception cref="InvalidOperationException">An unknown dungeon or a grid that does not cut into boxes.</exception>
    public static C.ContentRegistry ArmSegmented(C.ContentRegistry reg, string dungeonId, long seed, int? leaderBranch = null)
    {
        ArgumentNullException.ThrowIfNull(reg);
        var d = BuildSegmented(reg, dungeonId, seed, leaderBranch);
        var dungeon = reg.Dungeons[dungeonId];
        var bossPulls = C.LayoutDef.BossPullIds(dungeon);
        var hand = reg.Layout;
        var arena = hand.Boxes.First(b => string.Equals(b.Pull, C.LayoutDef.BossPullId, StringComparison.Ordinal));
        var graph = d.Graph;
        var corridors = Corridors(graph, d.Grid);
        var chain = d.Walk.Chain.ToList();

        var rooms = new Dictionary<int, C.LayoutBox>();
        foreach (var n in graph.Nodes)
        {
            if (n.Kind != RoomKind.Boss)
            {
                rooms[n.Id] = RoomBox(d.Grid, n, null, 0);
                continue;
            }
            // The boss fills its segment's arena: the room widened to the arena's depth (z; x too for the final boss, which has
            // no corridor out), centred on the row of the walked door in, and a pull room whose spawns stand 11.5 m from it.
            var segment = graph.BossRooms.ToList().IndexOf(n.Id);
            var door = corridors[(chain[chain.IndexOf(n.Id) - 1], n.Id)][^1];
            var doorZ = Mid(door.MinZ, door.MaxZ);
            rooms[n.Id] = PullRoom(RoomBox(d.Grid, n, arena, doorZ, widenX: segment == graph.SegmentCount - 1), bossPulls[segment], PullDepthMm, arena.AggroMm, doorZ);
        }

        var walk = Walk(graph, d.Grid, d.Branches, n => rooms[n.Id]);
        var boxes = walk.Boxes;
        var route = new List<C.LayoutPoint>();
        for (var k = 0; k + 1 < boxes.Count; k++)
        {
            route.Add(DoorPoint(boxes[k], boxes[k + 1]));
        }
        route.Add(boxes[^1].Origin!);

        // The first room of each (segment, branch): the box id a pack's Branch names.
        string FirstRoom(int segment, int branch) => RoomBoxId(d.Fill.Branches.Single(b => b.Segment == segment && b.Branch == branch).Rooms[0]);
        // The walked branch's packs are required, in route order, until the forces bar is covered (the party pulls what the
        // bar needs); every pack past that point, and every untaken branch's pack, is an optional pack beside the route.
        var walkedPacks = new List<C.LayoutPack>();
        var sidePacks = new List<C.LayoutPack>();
        var required = 0;
        var neededPacks = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in d.Fill.Packs)
        {
            if (p.Branch == d.Branches[p.Segment] && required < reg.ForcesFor(dungeonId).Required)
            {
                required += p.Points;
                neededPacks.Add(p.InstanceId);
            }
        }
        // f-9607c9c0: on every route some trash stands in a hall. Per (segment, branch) the pack of the branch's last room steps out
        // into the corridor that leaves it (the hall is wide enough to fight in); on the walked branch only a required pack moves
        // (a required pack in a hall is pulled in passing, an optional one could not be skipped there).
        var inHall = new HashSet<string>(StringComparer.Ordinal);
        for (var s = 0; s < graph.SegmentCount; s++)
        {
            for (var b = 0; b < RoomGraphGen.BranchCount; b++)
            {
                var pick = d.Fill.Packs.Where(p => p.Segment == s && p.Branch == b && (b != d.Branches[s] || neededPacks.Contains(p.InstanceId)))
                    .OrderBy(p => d.Fill.Branches.Single(x => x.Segment == s && x.Branch == b).Rooms.ToList().IndexOf(p.RoomId))
                    .LastOrDefault();
                if (pick is not null)
                {
                    inHall.Add(pick.InstanceId);
                }
            }
        }
        foreach (var p in d.Fill.Packs)
        {
            var roster = reg.Roster[p.PackId];
            var needed = neededPacks.Contains(p.InstanceId);
            var room = rooms[p.RoomId];
            var hall = inHall.Contains(p.InstanceId) ? corridors[(p.RoomId, graph.Successors(p.RoomId).Single())][0] : null;
            var pack = new C.LayoutPack
            {
                Id = p.InstanceId,
                Name = roster.Name,
                Room = hall?.Id ?? room.Id,
                Optional = !needed,
                Center = hall is not null
                    ? HallPoint(hall)
                    : needed
                        ? new C.LayoutPoint { X = room.MinX + RequiredDepthMm, Z = Mid(room.MinZ, room.MaxZ) + (OffsetSide(p.RoomId) * OffsetMm) }
                        : SlotPoint(room, p.Slot),
                AggroMm = roster.AggroMm,
                Enemies = roster.Enemies,
                Branch = FirstRoom(p.Segment, p.Branch),
            };
            (needed ? walkedPacks : sidePacks).Add(pack);
        }

        var entrance = rooms[graph.EntranceId];
        var layout = new C.LayoutDef
        {
            Dungeon = dungeonId,
            Start = new C.LayoutPoint { X = entrance.MinX + StartDepthMm, Z = Mid(entrance.MinZ, entrance.MaxZ) },
            Boxes = new C.ValueList<C.LayoutBox>(boxes),
            Route = new C.ValueList<C.LayoutPoint>(route),
            Packs = new C.ValueList<C.LayoutPack>(walkedPacks.Concat(sidePacks)),
            Formation = hand.Formation,
            SideBoxes = new C.ValueList<C.LayoutBox>(walk.SideBoxes),
        };
        var armed = reg with { Layout = layout };
        // Fail closed: the walked chain obeys every layout invariant (the side branches are drawn, never walked or validated).
        C.LayoutValidator.Check(armed with { Layout = layout with { Packs = new C.ValueList<C.LayoutPack>(walkedPacks), SideBoxes = new() } });
        return armed;
    }

    /// <summary>Generates, fills and lays out the dungeon for <paramref name="seed"/>.</summary>
    /// <param name="reg">Loaded content (its hand-built layout supplies the formation and the boss arena's size and ring).</param>
    /// <param name="seed">The run seed.</param>
    /// <returns>The armed dungeon.</returns>
    /// <exception cref="C.ContentLoadException">The seeded layout breaks a layout invariant.</exception>
    /// <exception cref="InvalidOperationException">The grid does not cut into boxes (a corridor that is no straight or three-leg run).</exception>
    public static SeededDungeon Build(C.ContentRegistry reg, long seed)
    {
        ArgumentNullException.ThrowIfNull(reg);
        var graph = RoomGraphGen.Generate(seed, Params);
        var grid = WalkGridGen.Emit(graph, Params);
        var fill = BranchFill.Fill(seed, graph, SeededRoster.Teaching(reg), SeededRoster.Optional(reg), SeededRoster.Params(reg));
        var branch = BotBranch(seed);
        var corridors = Corridors(graph, grid);
        var hand = reg.Layout;
        var arena = hand.Boxes.First(b => string.Equals(b.Pull, C.LayoutDef.BossPullId, StringComparison.Ordinal));
        var dungeonId = reg.Forces.Route.DungeonId;
        var content = reg.Dungeons[dungeonId];
        var pullById = content.Pulls.ToDictionary(p => p.Id, StringComparer.Ordinal);
        var walked = fill.Branches[branch];
        var side = fill.Branches[1 - branch];

        var bossDoor = corridors[(walked.Rooms[^1], graph.BossId)][^1];
        var doorZ = Mid(bossDoor.MinZ, bossDoor.MaxZ);
        var rooms = new Dictionary<int, C.LayoutBox>();
        foreach (var n in graph.Nodes)
        {
            rooms[n.Id] = RoomBox(grid, n, n.Kind == RoomKind.Boss ? arena : null, doorZ);
        }
        // The walked branch's teaching pulls hold their rooms; the boss holds the boss room.
        foreach (var p in fill.Packs.Where(p => p.Branch == branch && p.PullIndex >= 0))
        {
            rooms[p.RoomId] = PullRoom(rooms[p.RoomId], p.PackId, OriginDepth(pullById[p.PackId]), null, originOffsetZ: OffsetSide(p.RoomId) * OffsetMm);
        }
        rooms[graph.BossId] = PullRoom(rooms[graph.BossId], C.LayoutDef.BossPullId, PullDepthMm, arena.AggroMm, doorZ);

        var walk = Walk(graph, grid, new[] { branch }, n => rooms[n.Id]);
        var boxes = walk.Boxes;
        var sideBoxes = walk.SideBoxes;

        var route = new List<C.LayoutPoint>();
        for (var k = 0; k + 1 < boxes.Count; k++)
        {
            route.Add(DoorPoint(boxes[k], boxes[k + 1]));
        }
        route.Add(boxes[^1].Origin!);

        // f-9607c9c0: on each branch the last optional pack steps out into the hall that leaves its room. On the walked branch it is a
        // required pack there (a hall pack cannot be skipped, so it is pulled in passing); on the other branch it stays optional.
        C.LayoutBox? HallOf(FilledPack p) => corridors[(p.RoomId, graph.Successors(p.RoomId).Single())][0];
        FilledPack? LastPack(DungeonBranch b) => fill.Packs.Where(p => p.Branch == b.Index && p.PullIndex < 0).OrderBy(p => b.Rooms.ToList().IndexOf(p.RoomId)).ThenBy(p => p.Slot).LastOrDefault();
        var walkedHall = LastPack(walked);
        var sideHall = LastPack(side);
        var walkedPacks = fill.Packs.Where(p => p.Branch == branch && p.PullIndex < 0).Select(p => p == walkedHall
            ? Pack(reg, p, rooms[p.RoomId], walked.Rooms[0]) with { Room = HallOf(p)!.Id, Center = HallPoint(HallOf(p)!), Optional = false }
            : Pack(reg, p, rooms[p.RoomId], walked.Rooms[0])).ToList();
        var sidePacks = fill.Packs.Where(p => p.Branch != branch).Select(p => p.PullIndex >= 0
            ? PullAsPack(pullById[p.PackId], p, rooms[p.RoomId], side.Rooms[0])
            : p == sideHall
                ? Pack(reg, p, rooms[p.RoomId], side.Rooms[0]) with { Room = HallOf(p)!.Id, Center = HallPoint(HallOf(p)!) }
                : Pack(reg, p, rooms[p.RoomId], side.Rooms[0])).ToList();
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
        var walkedPulls = fill.Packs.Where(p => p.Branch == branch && p.PullIndex >= 0).OrderBy(p => p.PullIndex).Select(p => pullById[p.PackId]);
        var dungeon = content with { Pulls = new C.ValueList<C.PullDef>(walkedPulls) };
        var armed = reg with
        {
            Layout = layout,
            Dungeons = new C.ValueMap<C.DungeonDef>(reg.Dungeons.Select(kv => kv.Key == dungeonId ? new KeyValuePair<string, C.DungeonDef>(kv.Key, dungeon) : kv)),
        };
        // Fail closed: the walked chain obeys every layout invariant (the side branch is drawn, never walked or validated).
        C.LayoutValidator.Check(armed with { Layout = layout with { Packs = new C.ValueList<C.LayoutPack>(walkedPacks), SideBoxes = new() } });
        return new SeededDungeon(armed, branch, graph, grid, fill);
    }

    // A grid room as a layout box: its cells in mm. The boss room is widened and deepened to the hand arena's size (its
    // fight needs the space), anchored on its west wall and centred on the walked door's row (doorZ) while still holding
    // its grid cells, so both branches' doors stay on its wall and the walk in is straight along +x, like the hand route
    // (the formation stands behind the leader along -x, so a diagonal turn at the door would put a slot in the wall).
    private static C.LayoutBox RoomBox(WalkGrid grid, RoomNode n, C.LayoutBox? arena, int doorZ, bool widenX = true)
    {
        var r = grid.Rooms[n.Id];
        var c = grid.CellMm;
        var minX = (int)(grid.OriginX + ((long)r.MinCx * c));
        var maxX = (int)(grid.OriginX + ((long)(r.MaxCx + 1) * c));
        var minZ = (int)(grid.OriginZ + ((long)r.MinCz * c));
        var maxZ = (int)(grid.OriginZ + ((long)(r.MaxCz + 1) * c));
        if (arena is not null)
        {
            if (widenX)
            {
                maxX = Math.Max(maxX, minX + (arena.MaxX - arena.MinX));
            }
            var half = (arena.MaxZ - arena.MinZ) / 2;
            (minZ, maxZ) = (Math.Min(minZ, doorZ - half), Math.Max(maxZ, doorZ + half));
        }
        return new C.LayoutBox { Id = RoomBoxId(n.Id), Kind = C.LayoutBoxKind.Room, MinX = minX, MaxX = maxX, MinZ = minZ, MaxZ = maxZ };
    }

    // A pull room (LayoutValidator.CheckPullRoom): trigger = the room less 2000 mm from its west door; the spawns stand
    // PullDepthMm from the door on row z (the centre row, or the boss room's walked door row); the start 4500 mm in.
    // originOffsetZ (f-1232d0aa) stands the spawns off that row, to a side; the start stays on it.
    private static C.LayoutBox PullRoom(C.LayoutBox room, string pullId, int originDepth, int? aggroMm, int? originZ = null, int originOffsetZ = 0)
    {
        var z = originZ ?? Mid(room.MinZ, room.MaxZ);
        return room with
        {
            Pull = pullId,
            Trigger = new C.LayoutRect { MinX = room.MinX + C.LayoutValidator.TriggerInsetMm, MaxX = room.MaxX, MinZ = room.MinZ, MaxZ = room.MaxZ },
            Origin = new C.LayoutPoint { X = room.MinX + originDepth, Z = z + originOffsetZ },
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

    // The other branch's teaching pull as a pack: its members around the pull's spawn mean, standing where the pull would.
    private static C.LayoutPack PullAsPack(C.PullDef pull, FilledPack p, C.LayoutBox room, int branchRoom)
    {
        var n = pull.Enemies.Sum(e => e.Count);
        var mx = pull.Enemies.Sum(e => e.Count * e.Spawn.X) / n;
        var mz = pull.Enemies.Sum(e => e.Count * e.Spawn.Z) / n;
        var depth = OriginDepth(pull);
        return new C.LayoutPack
        {
            Id = p.InstanceId,
            Name = pull.Name,
            Room = room.Id,
            Optional = true,
            Center = new C.LayoutPoint { X = room.MinX + depth + mx, Z = Mid(room.MinZ, room.MaxZ) + mz + (OffsetSide(p.RoomId) * OffsetMm) },
            AggroMm = (int)Keyrun.Sim.Encounter.Pulls.DefaultAggroMm,
            Enemies = new C.ValueList<C.PullEnemy>(pull.Enemies.Select(e => e with { Spawn = new C.SpawnPoint { X = e.Spawn.X - mx, Z = e.Spawn.Z - mz } })),
            Branch = RoomBoxId(branchRoom),
        };
    }

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
