using System;
using System.Collections.Generic;

namespace Keyrun.Content;

/// <summary>Where the follow camera may sit: the clamped point and how much of the boom survived.</summary>
/// <param name="X">Camera x, mm.</param>
/// <param name="Z">Camera z, mm.</param>
/// <param name="Ppm">Boom length kept, parts per <see cref="CameraBoom.Whole"/> (Whole = unchanged).</param>
public readonly record struct BoomFit(int X, int Z, int Ppm);

/// <summary>
/// The follow camera's pure rules (CAMERA-FEEL, design/ART-PLAN.md section 8 lane 10). No Godot types and integers only
/// (the Keyrun.Content purity rule), unit-tested in Keyrun.Content.Tests/CameraBoomTests.cs; game/src/View/CameraRig.cs
/// only applies them. Units: plan millimetres, XZ is the floor plane, Y is up.
/// <list type="bullet">
/// <item><see cref="StartHeading"/>: the run starts looking down the route (the Entrance's way out), read from the plan.</item>
/// <item><see cref="Clamp"/>: the boom from the pivot to the desired camera point is cut short where it would enter a
/// wall box grown by <see cref="MarginMm"/>, so the camera never leaves the walkable boxes. Walls count as full-height
/// prisms here: a camera above a 1.6 m wall but outside it in XZ is still outside the dungeon.</item>
/// <item><see cref="Occluding"/>: the wall boxes (in 3D, with their real height) a sight line still crosses, for the
/// view to cut away (corners and doorways the clamp cannot fix).</item>
/// </list>
/// </summary>
public static class CameraBoom
{
    /// <summary>Clearance kept between the camera and a wall's face, mm (larger than the near plane's reach).</summary>
    public const int MarginMm = 300;

    /// <summary>XZ padding added to a wall box when testing a sight line, mm (a body is wider than its centre line).</summary>
    public const int OcclusionPadMm = 250;

    /// <summary>The fixed-point unit of <see cref="BoomFit.Ppm"/>: a whole boom.</summary>
    public const int Whole = 1_000_000;

    /// <summary>
    /// The heading the camera starts with (no mouse input yet): from the first box's centre toward its route point (the
    /// doorway into the next box), so a re-laid dungeon keeps facing its own first corridor. If that is zero, the first
    /// leg of the route.
    /// </summary>
    /// <param name="boxes">Layout boxes in route order (the first is the Entrance).</param>
    /// <param name="route">Route points, one per box.</param>
    /// <returns>An unnormalised XZ direction, mm.</returns>
    public static (int Dx, int Dz) StartHeading(IReadOnlyList<PlanBox> boxes, IReadOnlyList<(int X, int Z)> route)
    {
        ArgumentNullException.ThrowIfNull(boxes);
        ArgumentNullException.ThrowIfNull(route);
        if (boxes.Count == 0 || route.Count == 0)
        {
            throw new ArgumentException("the start heading needs a first box and its route point");
        }
        var first = boxes[0];
        var cx = (int)(((long)first.MinX + first.MaxX) / 2);
        var cz = (int)(((long)first.MinZ + first.MaxZ) / 2);
        var (dx, dz) = (route[0].X - cx, route[0].Z - cz);
        if (dx == 0 && dz == 0 && route.Count > 1)
        {
            (dx, dz) = (route[1].X - route[0].X, route[1].Z - route[0].Z);
        }
        return (dx, dz);
    }

    /// <summary>
    /// The nearest point to <paramref name="desired"/> along the segment from <paramref name="pivot"/> that keeps
    /// <paramref name="marginMm"/> clear of every wall box (XZ). A point the segment reaches without nearing a wall is
    /// returned unchanged. When the pivot itself is within the margin of a wall (the body hugs it), the boom stops the
    /// margin short of that wall's face, never behind the pivot.
    /// </summary>
    /// <param name="pivot">The boom's root (the followed body), mm.</param>
    /// <param name="desired">Where the camera would sit with a full boom, mm.</param>
    /// <param name="walls">Wall boxes (<see cref="DungeonPlan.WallBoxes"/>).</param>
    /// <param name="marginMm">Clearance, mm.</param>
    /// <returns>The clamped camera point and the boom fraction kept.</returns>
    public static BoomFit Clamp((int X, int Z) pivot, (int X, int Z) desired, IReadOnlyList<WallBox> walls, int marginMm = MarginMm)
    {
        ArgumentNullException.ThrowIfNull(walls);
        long dx = desired.X - pivot.X;
        long dz = desired.Z - pivot.Z;
        if (dx == 0 && dz == 0)
        {
            return new BoomFit(desired.X, desired.Z, Whole);
        }
        var len = Isqrt(dx * dx + dz * dz);
        var best = new Frac(1, 1);
        foreach (var w in walls)
        {
            var grown = (MinX: (long)w.MinX - marginMm, MaxX: (long)w.MaxX + marginMm, MinZ: (long)w.MinZ - marginMm, MaxZ: (long)w.MaxZ + marginMm);
            Frac? t;
            if (pivot.X > grown.MinX && pivot.X < grown.MaxX && pivot.Z > grown.MinZ && pivot.Z < grown.MaxZ)
            {
                // The body stands within the margin: only a boom that runs into the wall itself is cut, a margin short.
                var raw = Enter(new[] { ((long)pivot.X, dx, (long)w.MinX, (long)w.MaxX), ((long)pivot.Z, dz, (long)w.MinZ, (long)w.MaxZ) });
                t = raw is { } r ? r.Minus(marginMm, len) : null;
            }
            else
            {
                t = Enter(new[] { ((long)pivot.X, dx, grown.MinX, grown.MaxX), ((long)pivot.Z, dz, grown.MinZ, grown.MaxZ) });
            }
            if (t is { } hit && hit.LessThan(best))
            {
                best = hit;
            }
        }
        var x = pivot.X + (int)(dx * best.N / best.D);
        var z = pivot.Z + (int)(dz * best.N / best.D);
        var ppm = (int)(best.N * Whole / best.D);
        return new BoomFit(x, z, ppm);
    }

    /// <summary>
    /// The indices of the wall boxes the sight line from <paramref name="from"/> to <paramref name="to"/> passes through
    /// (3D, the boxes grown by <paramref name="padMm"/> in XZ only; a line over a wall's top is clear).
    /// </summary>
    /// <param name="from">The camera, mm (X, Y, Z).</param>
    /// <param name="to">A body point, mm (X, Y, Z).</param>
    /// <param name="walls">Wall boxes.</param>
    /// <param name="padMm">XZ padding, mm.</param>
    /// <returns>Indices into <paramref name="walls"/>, ascending.</returns>
    public static IReadOnlyList<int> Occluding((int X, int Y, int Z) from, (int X, int Y, int Z) to, IReadOnlyList<WallBox> walls, int padMm = OcclusionPadMm)
    {
        ArgumentNullException.ThrowIfNull(walls);
        var hits = new List<int>();
        long dx = to.X - from.X, dy = to.Y - from.Y, dz = to.Z - from.Z;
        for (var i = 0; i < walls.Count; i++)
        {
            var w = walls[i];
            var axes = new[]
            {
                ((long)from.X, dx, (long)w.MinX - padMm, (long)w.MaxX + padMm),
                ((long)from.Y, dy, (long)w.MinY, (long)w.MaxY),
                ((long)from.Z, dz, (long)w.MinZ - padMm, (long)w.MaxZ + padMm),
            };
            if (Enter(axes, fromInsideCounts: true) is not null)
            {
                hits.Add(i);
            }
        }
        return hits;
    }

    // Slab test: the parameter in [0, 1] where the segment a + t d enters the box, or null if it never passes through
    // it with positive length. A start already inside gives null for the boom (nothing to stop before) unless
    // fromInsideCounts (a sight line that starts in a wall is blocked by it).
    private static Frac? Enter((long A, long D, long Min, long Max)[] axes, bool fromInsideCounts = false)
    {
        var lo = new Frac(0, 1);
        var hi = new Frac(1, 1);
        var inside = true;
        foreach (var (a, d, min, max) in axes)
        {
            inside &= a > min && a < max;
            if (d == 0)
            {
                if (a <= min || a >= max)
                {
                    return null;
                }
                continue;
            }
            var (t0, t1) = d > 0 ? (new Frac(min - a, d), new Frac(max - a, d)) : (new Frac(a - max, -d), new Frac(a - min, -d));
            if (lo.LessThan(t0))
            {
                lo = t0;
            }
            if (t1.LessThan(hi))
            {
                hi = t1;
            }
        }
        if (inside && !fromInsideCounts)
        {
            return null;
        }
        return lo.LessThan(hi) ? lo : null;
    }

    private static long Isqrt(long v)
    {
        if (v <= 0)
        {
            return 0;
        }
        var x = v;
        var y = (x + 1) / 2;
        while (y < x)
        {
            x = y;
            y = (x + v / x) / 2;
        }
        return x;
    }

    // A non-negative-denominator fraction for exact slab comparisons (mm values keep every product inside long).
    private readonly record struct Frac(long N, long D)
    {
        public bool LessThan(Frac o) => N * o.D < o.N * D;

        // This fraction minus margin/len (len > 0), floored at zero.
        public Frac Minus(long margin, long len)
        {
            var n = N * len - margin * D;
            return n <= 0 ? new Frac(0, 1) : new Frac(n, D * len);
        }
    }
}
