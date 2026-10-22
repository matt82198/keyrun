using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;

namespace Keyrun.Sim.Nav;

/// <summary>
/// R-PATH-1 (core dungeon loop, WoW decides: actors path around walls and props the way WoW units do, following a
/// computed path to their goal). The sim-side walkable grid of a <see cref="NavLayout"/>, built once per layout
/// (<see cref="Of"/>) and never from Godot's NavigationServer: the sim is authoritative and integer-only, Godot only
/// renders. A cell is <see cref="CellMm"/> square over the layout <see cref="NavLayout.Extent"/>; it is walkable when its
/// centre is walkable space (<see cref="Walkable"/>: inside some layout box, edges included, and strictly inside no
/// <see cref="NavLayout.Obstacles"/> box). <see cref="FindPath"/> walks straight when the segment is clear and otherwise
/// runs an integer A* over the grid with a fixed tiebreak, so the same layout, start and goal always give the same path.
/// </summary>
public sealed class NavGrid
{
    /// <summary>Grid cell edge, mm. A 3000 mm corridor is six cells wide; a 1000 mm doorway overlap is two.</summary>
    public const long CellMm = 500;

    /// <summary>Segment sampling step for the line-of-sight test, mm (half a cell).</summary>
    public const long SightStepMm = 250;

    /// <summary>Orthogonal step cost (octile metric: 10 straight, 14 diagonal).</summary>
    public const int StraightCost = 10;

    /// <summary>Diagonal step cost.</summary>
    public const int DiagonalCost = 14;

    /// <summary>How many cells around an unwalkable start or goal cell are searched for the nearest walkable one.</summary>
    public const int SnapRadiusCells = 6;

    /// <summary>Memo bound: the A* cache is dropped whole once it holds this many paths (it is a pure-function cache).</summary>
    public const int MemoCap = 4096;

    /// <summary>
    /// Planning clearance around an obstacle, mm: paths keep this far off its faces (cells and sight lines treat the
    /// obstacle as this much larger), so an integer step along a path never clips a corner. The movement rule
    /// (<see cref="NavFollow.SlideOffObstacles"/>) blocks only the obstacle itself.
    /// </summary>
    public const long ClearanceMm = 300;

    // Neighbour order is part of the tiebreak: east, west, north (+z), south, then the four diagonals.
    private static readonly (int Dx, int Dz)[] Steps =
    {
        (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1),
    };

    private static readonly ConditionalWeakTable<NavLayout, NavGrid> Grids = new();

    private readonly IReadOnlyList<Bounds> _boxes;
    private readonly IReadOnlyList<Bounds> _obstacles;
    private readonly Bounds[] _planObstacles;
    private readonly bool[] _walkable;
    private readonly Dictionary<long, Vec3[]> _memo = new();
    private readonly object _gate = new();

    /// <summary>Builds the grid of <paramref name="boxes"/> (walkable space) minus <paramref name="obstacles"/>.</summary>
    /// <param name="boxes">Walkable boxes (a layout's rooms and corridors); must not be empty.</param>
    /// <param name="obstacles">Blocking boxes (walls and props inside the walkable space), or empty.</param>
    /// <exception cref="ArgumentException"><paramref name="boxes"/> is empty.</exception>
    public NavGrid(IReadOnlyList<Bounds> boxes, IReadOnlyList<Bounds> obstacles)
    {
        ArgumentNullException.ThrowIfNull(boxes);
        ArgumentNullException.ThrowIfNull(obstacles);
        if (boxes.Count == 0)
        {
            throw new ArgumentException("a nav grid needs at least one walkable box", nameof(boxes));
        }
        _boxes = boxes;
        _obstacles = obstacles;
        _planObstacles = new Bounds[obstacles.Count];
        for (var i = 0; i < obstacles.Count; i++)
        {
            var o = obstacles[i];
            _planObstacles[i] = new Bounds(o.MinX - ClearanceMm, o.MaxX + ClearanceMm, o.MinZ - ClearanceMm, o.MaxZ + ClearanceMm);
        }
        long minX = long.MaxValue, maxX = long.MinValue, minZ = long.MaxValue, maxZ = long.MinValue;
        foreach (var b in boxes)
        {
            minX = Math.Min(minX, b.MinX);
            maxX = Math.Max(maxX, b.MaxX);
            minZ = Math.Min(minZ, b.MinZ);
            maxZ = Math.Max(maxZ, b.MaxZ);
        }
        OriginX = minX;
        OriginZ = minZ;
        Width = (int)((maxX - minX + CellMm - 1) / CellMm);
        Depth = (int)((maxZ - minZ + CellMm - 1) / CellMm);
        Width = Math.Max(1, Width);
        Depth = Math.Max(1, Depth);
        _walkable = new bool[Width * Depth];
        for (var cz = 0; cz < Depth; cz++)
        {
            for (var cx = 0; cx < Width; cx++)
            {
                var c = Center(cx, cz);
                _walkable[(cz * Width) + cx] = InBoxes(c) && !Blocked(_planObstacles, c);
            }
        }
    }

    /// <summary>Lowest x of the grid, mm.</summary>
    public long OriginX { get; }

    /// <summary>Lowest z of the grid, mm.</summary>
    public long OriginZ { get; }

    /// <summary>Cells along x.</summary>
    public int Width { get; }

    /// <summary>Cells along z.</summary>
    public int Depth { get; }

    /// <summary>
    /// Optional host observer, called once per path the A* actually computes (a cache miss, never a straight walk):
    /// actor id, goal, waypoint count. The host prints it as the capture log's <c>PATH</c> line. It never changes a path.
    /// </summary>
    public Action<int, Vec3, int>? Observer { get; set; }

    /// <summary>
    /// The grid of <paramref name="layout"/>: built on first use (run start) from its boxes and obstacles and kept for the
    /// layout's lifetime. Gates are not walls here: the shipped layout gates no box (PATH-TO-BOSS, STATE.md D44), and a
    /// gated box a layout still holds shut is enforced by the walkable clamp (Run.StepLayout), as before.
    /// </summary>
    /// <param name="layout">The layout.</param>
    /// <returns>Its grid.</returns>
    public static NavGrid Of(NavLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        return Grids.GetValue(layout, static l =>
        {
            var boxes = new List<Bounds>(l.Boxes.Count + l.Side.Count);
            foreach (var b in l.AllBoxes)
            {
                boxes.Add(b.Box);
            }
            return new NavGrid(boxes, l.Obstacles);
        });
    }

    /// <summary>True when <paramref name="p"/> is walkable space: in some box (edges included), strictly inside no obstacle.</summary>
    /// <param name="p">The point.</param>
    /// <returns>True when an actor may stand there.</returns>
    public bool Walkable(Vec3 p) => InBoxes(p) && !Blocked(_obstacles, p);

    private bool InBoxes(Vec3 p)
    {
        foreach (var b in _boxes)
        {
            if (Movement.Contains(b, p))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// True when the segment <paramref name="a"/>-<paramref name="b"/> passes through the open interior of
    /// <paramref name="box"/> (touching a face or a corner does not count). Exact integer Liang-Barsky clip: the entry
    /// and exit parameters are kept as fractions and compared by cross-multiplication in 128-bit integers.
    /// </summary>
    /// <param name="a">One end.</param>
    /// <param name="b">The other end.</param>
    /// <param name="box">The box.</param>
    /// <returns>True when the segment crosses the interior.</returns>
    public static bool SegmentCrosses(Vec3 a, Vec3 b, Bounds box)
    {
        // Parameters t = num / den with den > 0; the clip window starts as [0, 1].
        Int128 enterN = 0, enterD = 1, exitN = 1, exitD = 1;
        bool Axis(long p, long d, long min, long max, ref Int128 inN, ref Int128 inD, ref Int128 outN, ref Int128 outD)
        {
            if (d == 0)
            {
                return p > min && p < max;
            }
            Int128 n1 = min - p, n2 = max - p, den = d;
            if (d < 0)
            {
                n1 = -n1;
                n2 = -n2;
                den = -den;
            }
            var (loN, hiN) = n1 < n2 ? (n1, n2) : (n2, n1);
            if (loN * inD > inN * den)
            {
                inN = loN;
                inD = den;
            }
            if (hiN * outD < outN * den)
            {
                outN = hiN;
                outD = den;
            }
            return true;
        }
        if (!Axis(a.X, b.X - a.X, box.MinX, box.MaxX, ref enterN, ref enterD, ref exitN, ref exitD) ||
            !Axis(a.Z, b.Z - a.Z, box.MinZ, box.MaxZ, ref enterN, ref enterD, ref exitN, ref exitD))
        {
            return false;
        }
        return enterN * exitD < exitN * enterD;
    }

    /// <summary>True when <paramref name="p"/> lies strictly inside one of <paramref name="obstacles"/> (a face is walkable).</summary>
    /// <param name="obstacles">The obstacles.</param>
    /// <param name="p">The point.</param>
    /// <returns>True when blocked.</returns>
    public static bool Blocked(IReadOnlyList<Bounds> obstacles, Vec3 p)
    {
        ArgumentNullException.ThrowIfNull(obstacles);
        foreach (var o in obstacles)
        {
            if (p.X > o.MinX && p.X < o.MaxX && p.Z > o.MinZ && p.Z < o.MaxZ)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// True when a walker can go straight from <paramref name="a"/> to <paramref name="b"/>: the segment crosses no
    /// obstacle grown by <see cref="ClearanceMm"/> (exact, <see cref="SegmentCrosses"/>) and stays inside the layout boxes,
    /// sampled every <see cref="SightStepMm"/> or less (integer interpolation, both ends included).
    /// </summary>
    /// <param name="a">One end.</param>
    /// <param name="b">The other end.</param>
    /// <returns>True when clear.</returns>
    public bool LineOfSight(Vec3 a, Vec3 b)
    {
        foreach (var o in _planObstacles)
        {
            if (SegmentCrosses(a, b, o))
            {
                return false;
            }
        }
        var dx = b.X - a.X;
        var dz = b.Z - a.Z;
        var len = Movement.Isqrt((dx * dx) + (dz * dz));
        var n = Math.Max(1, (len + SightStepMm - 1) / SightStepMm);
        for (long i = 0; i <= n; i++)
        {
            if (!InBoxes(new Vec3(a.X + IntMath.TruncDiv(dx * i, n), a.Y, a.Z + IntMath.TruncDiv(dz * i, n))))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// The path from <paramref name="start"/> to <paramref name="goal"/>, start and goal included: just the two when the
    /// straight segment is clear (<see cref="LineOfSight"/>); otherwise the A* cell path (<see cref="CellPath"/>) pulled
    /// tight to its corners (each waypoint the farthest next cell centre in sight). When no grid path exists (a goal in
    /// a sealed pocket) it is the straight two-point path, which the walkable clamp then stops at the wall.
    /// </summary>
    /// <param name="start">Where the walker stands.</param>
    /// <param name="goal">Where it is going.</param>
    /// <param name="actorId">The walker, for the <see cref="Observer"/> only.</param>
    /// <returns>The waypoints, at least two.</returns>
    public IReadOnlyList<Vec3> FindPath(Vec3 start, Vec3 goal, int actorId = 0)
    {
        if (LineOfSight(start, goal))
        {
            return new[] { start, goal };
        }
        var sc = Snap(start);
        var gc = Snap(goal);
        if (sc < 0 || gc < 0 || sc == gc)
        {
            return new[] { start, goal };
        }
        var key = ((long)sc * _walkable.Length) + gc;
        Vec3[]? inner;
        bool computed;
        lock (_gate)
        {
            computed = !_memo.TryGetValue(key, out inner);
        }
        if (computed)
        {
            inner = Pull(CellPath(sc, gc));
            lock (_gate)
            {
                if (_memo.Count >= MemoCap)
                {
                    _memo.Clear();
                }
                _memo[key] = inner;
            }
        }
        // The corners were pulled from the start cell's centre; from the walker's exact position walk at the farthest one
        // in sight (or the goal), and only when none is in sight at the start cell's centre first.
        var ahead = new List<Vec3>(inner!.Length + 1);
        foreach (var w in inner)
        {
            ahead.Add(w with { Y = start.Y });
        }
        ahead.Add(goal);
        var from = -1;
        for (var k = ahead.Count - 1; k >= 0; k--)
        {
            if (LineOfSight(start, ahead[k]))
            {
                from = k;
                break;
            }
        }
        var path = new List<Vec3>(ahead.Count + 2) { start };
        if (from < 0)
        {
            path.Add(CenterOf(sc) with { Y = start.Y });
            from = 0;
        }
        for (var k = from; k < ahead.Count; k++)
        {
            path.Add(ahead[k]);
        }
        if (computed)
        {
            Observer?.Invoke(actorId, goal, path.Count);
        }
        return path;
    }

    /// <summary>
    /// The point a walker at <paramref name="start"/> walks straight at to follow <see cref="FindPath"/> to
    /// <paramref name="goal"/>: the first waypoint after the start that is not the start itself.
    /// </summary>
    /// <param name="start">Where the walker stands.</param>
    /// <param name="goal">Where it is going.</param>
    /// <param name="actorId">The walker (observer only).</param>
    /// <returns>The next waypoint.</returns>
    public Vec3 NextWaypoint(Vec3 start, Vec3 goal, int actorId = 0)
    {
        var path = FindPath(start, goal, actorId);
        for (var i = 1; i < path.Count; i++)
        {
            if (path[i].X != start.X || path[i].Z != start.Z)
            {
                return path[i];
            }
        }
        return goal;
    }

    /// <summary>
    /// Integer A* from cell <paramref name="from"/> to cell <paramref name="to"/> over the 8-connected grid (no corner
    /// cutting: a diagonal needs both orthogonal cells walkable), octile costs 10/14 and the octile heuristic. Fixed
    /// tiebreak for determinism: the open set pops the lowest (f, h, cell index) triple, cell index = z * Width + x;
    /// neighbours are expanded in <see cref="Steps"/> order and a parent is replaced only by a strictly cheaper g.
    /// </summary>
    /// <param name="from">Start cell index.</param>
    /// <param name="to">Goal cell index.</param>
    /// <returns>The cell indexes from start to goal, or empty when unreachable.</returns>
    public IReadOnlyList<int> CellPath(int from, int to)
    {
        var count = _walkable.Length;
        var g = new int[count];
        var parent = new int[count];
        var closed = new bool[count];
        Array.Fill(g, int.MaxValue);
        Array.Fill(parent, -1);
        var open = new PriorityQueue<int, (int F, int H, int Index)>();
        g[from] = 0;
        var h0 = Heuristic(from, to);
        open.Enqueue(from, (h0, h0, from));
        while (open.TryDequeue(out var cur, out _))
        {
            if (closed[cur])
            {
                continue;
            }
            closed[cur] = true;
            if (cur == to)
            {
                break;
            }
            var cx = cur % Width;
            var cz = cur / Width;
            foreach (var (sx, sz) in Steps)
            {
                var nx = cx + sx;
                var nz = cz + sz;
                if (nx < 0 || nz < 0 || nx >= Width || nz >= Depth)
                {
                    continue;
                }
                var n = (nz * Width) + nx;
                if (!_walkable[n] || closed[n])
                {
                    continue;
                }
                var diagonal = sx != 0 && sz != 0;
                if (diagonal && (!_walkable[(cz * Width) + nx] || !_walkable[(nz * Width) + cx]))
                {
                    continue;
                }
                var cost = g[cur] + (diagonal ? DiagonalCost : StraightCost);
                if (cost < g[n])
                {
                    g[n] = cost;
                    parent[n] = cur;
                    var h = Heuristic(n, to);
                    open.Enqueue(n, (cost + h, h, n));
                }
            }
        }
        if (!closed[to])
        {
            return Array.Empty<int>();
        }
        var cells = new List<int>();
        for (var c = to; c >= 0; c = parent[c])
        {
            cells.Add(c);
        }
        cells.Reverse();
        return cells;
    }

    /// <summary>The cell holding <paramref name="p"/>, or -1 outside the grid.</summary>
    /// <param name="p">The point.</param>
    /// <returns>The cell index.</returns>
    public int CellAt(Vec3 p)
    {
        var cx = IntMath.FloorDiv(p.X - OriginX, CellMm);
        var cz = IntMath.FloorDiv(p.Z - OriginZ, CellMm);
        if (cx < 0 || cz < 0 || cx >= Width || cz >= Depth)
        {
            return -1;
        }
        return (int)((cz * Width) + cx);
    }

    /// <summary>The centre of cell <paramref name="index"/>.</summary>
    /// <param name="index">Cell index.</param>
    /// <returns>Its centre (y 0).</returns>
    public Vec3 CenterOf(int index) => Center(index % Width, index / Width);

    /// <summary>True when cell <paramref name="index"/> is walkable.</summary>
    /// <param name="index">Cell index.</param>
    /// <returns>True when walkable.</returns>
    public bool CellWalkable(int index) => index >= 0 && index < _walkable.Length && _walkable[index];

    private Vec3 Center(long cx, long cz) =>
        new(OriginX + (cx * CellMm) + (CellMm / 2), 0, OriginZ + (cz * CellMm) + (CellMm / 2));

    private int Heuristic(int a, int b)
    {
        var dx = Math.Abs((a % Width) - (b % Width));
        var dz = Math.Abs((a / Width) - (b / Width));
        return (StraightCost * Math.Max(dx, dz)) + ((DiagonalCost - StraightCost) * Math.Min(dx, dz));
    }

    // The walkable cell for p: its own cell when walkable, else the walkable cell within SnapRadiusCells whose centre is
    // nearest p (lowest index on a tie); -1 when none.
    private int Snap(Vec3 p)
    {
        var own = CellAt(p);
        if (own >= 0 && _walkable[own])
        {
            return own;
        }
        var px = IntMath.FloorDiv(p.X - OriginX, CellMm);
        var pz = IntMath.FloorDiv(p.Z - OriginZ, CellMm);
        var best = -1;
        var bestD2 = long.MaxValue;
        for (var cz = pz - SnapRadiusCells; cz <= pz + SnapRadiusCells; cz++)
        {
            for (var cx = px - SnapRadiusCells; cx <= px + SnapRadiusCells; cx++)
            {
                if (cx < 0 || cz < 0 || cx >= Width || cz >= Depth)
                {
                    continue;
                }
                var i = (int)((cz * Width) + cx);
                if (!_walkable[i])
                {
                    continue;
                }
                var c = Center(cx, cz);
                var d2 = ((c.X - p.X) * (c.X - p.X)) + ((c.Z - p.Z) * (c.Z - p.Z));
                if (d2 < bestD2 || (d2 == bestD2 && i < best))
                {
                    best = i;
                    bestD2 = d2;
                }
            }
        }
        return best;
    }

    // String pulling: from each kept point, keep extending to the next cell centre while it stays in sight; the interior
    // corners (start and goal cells dropped) are the waypoints.
    private Vec3[] Pull(IReadOnlyList<int> cells)
    {
        if (cells.Count <= 2)
        {
            return Array.Empty<Vec3>();
        }
        var corners = new List<Vec3>();
        var anchor = CenterOf(cells[0]);
        var last = cells.Count - 1;
        var j = 1;
        while (j < last)
        {
            var next = j;
            while (next + 1 <= last && LineOfSight(anchor, CenterOf(cells[next + 1])))
            {
                next++;
            }
            if (next == last)
            {
                break;
            }
            anchor = CenterOf(cells[next]);
            corners.Add(anchor);
            j = next + 1;
        }
        return corners.ToArray();
    }
}
