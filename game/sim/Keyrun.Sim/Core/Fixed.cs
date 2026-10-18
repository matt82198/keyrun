using System;

namespace Keyrun.Sim.Core;

/// <summary>
/// Port of sim/core/fixed.ts: integer-only vector math over <see cref="Vec3"/> (mm) and yaw (1/1024 turn).
/// Every function returns integers; <see cref="Scale"/> and <see cref="NormaliseTo"/> round to nearest, ties
/// away from zero. No trig or sqrt call exists anywhere on this path.
/// </summary>
/// <remarks>Pinned bit-exactly by fixed.vectors.json (isqrt, length, scale, normaliseTo, yawBetween, angleDiff).</remarks>
public static class Fixed
{
    /// <summary>Basis-point denominator: 10000 = 1.0 (fixed.ts BP_SCALE, combat/effects.ts BP_DENOM).</summary>
    public const long BpScale = 10000;

    /// <summary>Yaw units per full turn (fixed.ts YAW_MAX).</summary>
    public const int YawMax = 1024;

    /// <summary>
    /// fixed.ts isqrt: floor(sqrt(n)), exact on perfect squares, by integer Newton iteration with a final
    /// +-1 correction pass (the same steps as the TS, so the same result for every input).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="n"/> is negative (TS throws), or above
    /// 2^53 - 1 (beyond the range where the TS double arithmetic is exact; fail closed).</exception>
    public static long Isqrt(long n)
    {
        if (n < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(n), "isqrt: expected a non-negative integer");
        }
        if (n > IntMath.MaxSafeInteger)
        {
            throw new ArgumentOutOfRangeException(nameof(n), "isqrt: input beyond 2^53 - 1 is outside the TS oracle's exact range");
        }
        if (n == 0)
        {
            return 0;
        }
        long seed = 1;
        while (seed * seed <= n)
        {
            seed *= 2;
        }
        var x = seed;
        for (;;)
        {
            var next = (x + (n / x)) / 2;
            if (next >= x)
            {
                break;
            }
            x = next;
        }
        while (x * x > n)
        {
            x -= 1;
        }
        while ((x + 1) * (x + 1) <= n)
        {
            x += 1;
        }
        return x;
    }

    /// <summary>Componentwise sum.</summary>
    public static Vec3 Add(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    /// <summary>Componentwise difference <c>a - b</c>.</summary>
    public static Vec3 Sub(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    /// <summary>Scales <paramref name="v"/> by basis points (10000 = 1.0), each component rounded half away from zero.</summary>
    public static Vec3 Scale(Vec3 v, long bp) => new(
        IntMath.RoundHalfAwayDiv(v.X * bp, BpScale),
        IntMath.RoundHalfAwayDiv(v.Y * bp, BpScale),
        IntMath.RoundHalfAwayDiv(v.Z * bp, BpScale));

    /// <summary>Dot product.</summary>
    public static long Dot(Vec3 a, Vec3 b) => (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);

    /// <summary>Integer magnitude: isqrt of the squared length.</summary>
    public static long Length(Vec3 v) => Isqrt(Dot(v, v));

    /// <summary>Rescales <paramref name="v"/> to integer length <paramref name="len"/>, preserving direction; the zero vector maps to itself.</summary>
    public static Vec3 NormaliseTo(Vec3 v, long len)
    {
        var l = Length(v);
        if (l == 0)
        {
            return Vec3.Zero;
        }
        return new Vec3(
            IntMath.RoundHalfAwayDiv(v.X * len, l),
            IntMath.RoundHalfAwayDiv(v.Y * len, l),
            IntMath.RoundHalfAwayDiv(v.Z * len, l));
    }

    /// <summary>
    /// Yaw (0..1023) of the direction from <paramref name="from"/> to <paramref name="to"/> in the XZ plane:
    /// 0 = +x, 256 = +z. y is ignored. Coincident points return 0.
    /// </summary>
    public static int YawBetween(Vec3 from, Vec3 to) => Atan.YawFromComponents(to.X - from.X, to.Z - from.Z);

    /// <summary>
    /// Shortest signed distance from yaw <paramref name="a"/> to yaw <paramref name="b"/>, in (-512, 512]:
    /// <c>((b - a) % 1024 + 1024) % 1024</c> with JS truncating <c>%</c>, then minus 1024 above 512.
    /// </summary>
    public static long AngleDiff(long a, long b)
    {
        var d = IntMath.JsRem(b - a, YawMax) + YawMax;
        d = IntMath.JsRem(d, YawMax);
        if (d > YawMax / 2)
        {
            d -= YawMax;
        }
        return d;
    }
}
