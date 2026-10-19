using System;
using System.Collections.Generic;

namespace Keyrun.Content;

/// <summary>Where the follow camera may sit: the clamped point and how much of the boom survived.</summary>
/// <param name="X">Camera x, mm.</param>
/// <param name="Z">Camera z, mm.</param>
/// <param name="Ppm">Boom length kept, parts per <see cref="CameraBoom.Whole"/> (Whole = unchanged).</param>
public readonly record struct BoomFit(int X, int Z, int Ppm);

/// <summary>The boom and pitch that keep the party and the target inside the padded frustum (<see cref="CameraBoom.FitParty"/>).</summary>
/// <param name="BoomMm">Boom length, mm, within the given bounds.</param>
/// <param name="PitchMrad">Pitch change, milliradians, added to the camera's pitch (positive looks flatter, negative looks
/// further down); 0 when the boom alone fits, never past the given room.</param>
/// <param name="Solved">Every point is inside the padded frustum at this boom and pitch.</param>
/// <param name="Worst">Index of the point furthest outside when not solved, else -1.</param>
public readonly record struct PartyFit(int BoomMm, int PitchMrad, bool Solved, int Worst);

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

    /// <summary>The pitch search step of <see cref="FitParty"/>, milliradians.</summary>
    public const int PitchStepMrad = 25;

    private const long Ppm = 1_000_000;

    // cos and sin of PitchStepMrad (0.025 rad), parts per million.
    private const long StepCosPpm = 999_688;
    private const long StepSinPpm = 24_997;

    /// <summary>
    /// The boom and pitch change that keep every point (party bodies and the current target) inside the camera's
    /// frustum shrunk by <paramref name="padMm"/> (PARTY-FRAMING, design/ART-PLAN.md section 8 lane 10). The camera sits
    /// at <c>pivot - B f</c> looking along <c>f</c>; for a point <c>v = p - pivot</c> with depth <c>d = v.f</c>, side
    /// <c>l = v.r</c> (<c>r = f x u</c>) and height <c>h = v.u</c> it is inside when <c>|l| + pad &lt;= tanH (d + B)</c> and
    /// <c>h + pad &lt;= tanUp (d + B)</c> above the axis or <c>-h + pad &lt;= tanDown (d + B)</c> below it. The boom is the
    /// smallest B meeting all of them, clamped to [min, max]. If that still leaves a point out, the pitch is turned in
    /// <see cref="PitchStepMrad"/> steps, smallest change first (down before up on a tie), within the given room each way;
    /// when no step fits, the step that leaves the worst point least far out is returned with
    /// <see cref="PartyFit.Solved"/> false. Never turns yaw. Integers only: vectors and tangents permille, lengths mm.
    /// </summary>
    /// <param name="pivot">The boom's root, mm.</param>
    /// <param name="forwardPermille">The camera's unit view direction, permille.</param>
    /// <param name="upPermille">The camera's unit up direction (perpendicular to forward), permille.</param>
    /// <param name="points">Points to frame, mm.</param>
    /// <param name="tanHalfHPermille">tan of the half horizontal FOV, permille.</param>
    /// <param name="tanUpPermille">tan of the usable angle above the view axis, permille.</param>
    /// <param name="tanDownPermille">tan of the usable angle below the view axis (the HUD may trim it), permille.</param>
    /// <param name="padMm">Margin kept around each point, mm.</param>
    /// <param name="minBoomMm">Shortest boom, mm.</param>
    /// <param name="maxBoomMm">Longest boom, mm.</param>
    /// <param name="pitchDownRoomMrad">How far the pitch may turn down before its clamp, mrad.</param>
    /// <param name="pitchUpRoomMrad">How far the pitch may turn up (flatter) before its clamp, mrad.</param>
    /// <returns>The fit.</returns>
    public static PartyFit FitParty(
        (int X, int Y, int Z) pivot,
        (int X, int Y, int Z) forwardPermille,
        (int X, int Y, int Z) upPermille,
        IReadOnlyList<(int X, int Y, int Z)> points,
        int tanHalfHPermille,
        int tanUpPermille,
        int tanDownPermille,
        int padMm,
        int minBoomMm,
        int maxBoomMm,
        int pitchDownRoomMrad,
        int pitchUpRoomMrad)
    {
        ArgumentNullException.ThrowIfNull(points);
        if (tanHalfHPermille <= 0 || tanUpPermille <= 0 || tanDownPermille <= 0)
        {
            throw new ArgumentException("frustum tangents must be positive");
        }
        if (minBoomMm > maxBoomMm || padMm < 0 || pitchDownRoomMrad < 0 || pitchUpRoomMrad < 0)
        {
            throw new ArgumentException("boom bounds, pad and pitch room must be ordered and non-negative");
        }
        if (points.Count == 0)
        {
            return new PartyFit(minBoomMm, 0, true, -1);
        }
        var f = (X: forwardPermille.X * 1000L, Y: forwardPermille.Y * 1000L, Z: forwardPermille.Z * 1000L);
        var u = (X: upPermille.X * 1000L, Y: upPermille.Y * 1000L, Z: upPermille.Z * 1000L);
        var tans = (H: (long)tanHalfHPermille, Up: (long)tanUpPermille, Down: (long)tanDownPermille);

        long need = minBoomMm;
        var r = Cross(f, u);
        foreach (var p in points)
        {
            var (d, l, h) = Frame(p, pivot, f, u, r);
            need = Math.Max(need, CeilDiv((Math.Abs(l) + padMm) * 1000, tans.H) - d);
            need = Math.Max(need, h >= 0
                ? CeilDiv((h + padMm) * 1000, tans.Up) - d
                : CeilDiv((-h + padMm) * 1000, tans.Down) - d);
        }
        var boom = (int)Math.Clamp(need, minBoomMm, maxBoomMm);

        var (over, worst) = Overflow(points, pivot, f, u, boom, tans, padMm);
        if (over <= 0)
        {
            return new PartyFit(boom, 0, true, -1);
        }
        var best = (Over: over, Mrad: 0, Worst: worst);
        long c = Ppm, s = 0;
        for (var k = 1; k * PitchStepMrad <= Math.Max(pitchDownRoomMrad, pitchUpRoomMrad); k++)
        {
            (c, s) = ((c * StepCosPpm - s * StepSinPpm) / Ppm, (s * StepCosPpm + c * StepSinPpm) / Ppm);
            foreach (var sign in new[] { -1, 1 })
            {
                if (k * PitchStepMrad > (sign < 0 ? pitchDownRoomMrad : pitchUpRoomMrad))
                {
                    continue;
                }
                // pitch + delta: f' = f cos + u sin, u' = u cos - f sin (d forward / d pitch = up).
                var ss = sign * s;
                var f2 = ((f.X * c + u.X * ss) / Ppm, (f.Y * c + u.Y * ss) / Ppm, (f.Z * c + u.Z * ss) / Ppm);
                var u2 = ((u.X * c - f.X * ss) / Ppm, (u.Y * c - f.Y * ss) / Ppm, (u.Z * c - f.Z * ss) / Ppm);
                var (o, w) = Overflow(points, pivot, f2, u2, boom, tans, padMm);
                var mrad = sign * k * PitchStepMrad;
                if (o <= 0)
                {
                    return new PartyFit(boom, mrad, true, -1);
                }
                if (o < best.Over)
                {
                    best = (o, mrad, w);
                }
            }
        }
        return new PartyFit(boom, best.Mrad, false, best.Worst);
    }

    // The largest amount any point is outside the padded frustum (mm x permille; <= 0 means all inside) and its index.
    private static (long Over, int Worst) Overflow(
        IReadOnlyList<(int X, int Y, int Z)> points,
        (int X, int Y, int Z) pivot,
        (long X, long Y, long Z) f,
        (long X, long Y, long Z) u,
        int boom,
        (long H, long Up, long Down) tans,
        int padMm)
    {
        var r = Cross(f, u);
        var worst = (Over: long.MinValue, Index: -1);
        for (var i = 0; i < points.Count; i++)
        {
            var (d, l, h) = Frame(points[i], pivot, f, u, r);
            var depth = d + boom;
            var side = (Math.Abs(l) + padMm) * 1000 - tans.H * depth;
            var vertical = h >= 0 ? (h + padMm) * 1000 - tans.Up * depth : (-h + padMm) * 1000 - tans.Down * depth;
            var o = Math.Max(side, vertical);
            if (o > worst.Over)
            {
                worst = (o, i);
            }
        }
        return worst.Over <= 0 ? (worst.Over, -1) : worst;
    }

    // Depth, side and height of a point in the camera frame (f, u, r in ppm), mm.
    private static (long D, long L, long H) Frame(
        (int X, int Y, int Z) p,
        (int X, int Y, int Z) pivot,
        (long X, long Y, long Z) f,
        (long X, long Y, long Z) u,
        (long X, long Y, long Z) r)
    {
        var v = ((long)p.X - pivot.X, (long)p.Y - pivot.Y, (long)p.Z - pivot.Z);
        return (Dot(v, f) / Ppm, Dot(v, r) / Ppm, Dot(v, u) / Ppm);
    }

    private static long Dot((long X, long Y, long Z) a, (long X, long Y, long Z) b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    private static (long X, long Y, long Z) Cross((long X, long Y, long Z) a, (long X, long Y, long Z) b) =>
        ((a.Y * b.Z - a.Z * b.Y) / Ppm, (a.Z * b.X - a.X * b.Z) / Ppm, (a.X * b.Y - a.Y * b.X) / Ppm);

    // Ceiling of a / b for b > 0 (for a <= 0, truncation toward zero already is the ceiling).
    private static long CeilDiv(long a, long b) => a > 0 ? (a + b - 1) / b : a / b;

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
