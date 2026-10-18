using System;

namespace Keyrun.Sim.Core;

/// <summary>
/// Exact integer equivalents of the JS rounding operations the TS sim applies to <c>n / d</c>
/// (game/sim/ORACLE.md section 8). JS evaluates <c>n / d</c> as a double and then rounds; every sim value is
/// below 2^53, so the double quotient's floor/trunc/ceil/round equal these exact integer results.
/// Never use a bare <c>/</c> on a signed value that can be negative unless the TS used <c>Math.trunc</c> there.
/// </summary>
/// <remarks>Pinned bit-exactly by intdiv.vectors.json (jsIntDiv) and, for <see cref="RoundHalfAwayDiv"/>, by
/// fixed.vectors.json (scale, normaliseTo).</remarks>
public static class IntMath
{
    /// <summary>The largest integer a JS double holds exactly (2^53 - 1).</summary>
    public const long MaxSafeInteger = 9007199254740991L;

    /// <summary><c>Math.floor(n / d)</c>: rounds toward negative infinity.</summary>
    /// <exception cref="DivideByZeroException"><paramref name="d"/> is 0.</exception>
    public static long FloorDiv(long n, long d)
    {
        var q = n / d;
        if ((n % d != 0) && ((n < 0) != (d < 0)))
        {
            q -= 1;
        }
        return q;
    }

    /// <summary><c>Math.trunc(n / d)</c>: rounds toward zero (C# integer <c>/</c>).</summary>
    /// <exception cref="DivideByZeroException"><paramref name="d"/> is 0.</exception>
    public static long TruncDiv(long n, long d) => n / d;

    /// <summary><c>Math.ceil(n / d)</c>: rounds toward positive infinity.</summary>
    /// <exception cref="DivideByZeroException"><paramref name="d"/> is 0.</exception>
    public static long CeilDiv(long n, long d)
    {
        var q = n / d;
        if ((n % d != 0) && ((n < 0) == (d < 0)))
        {
            q += 1;
        }
        return q;
    }

    /// <summary>
    /// <c>Math.round(n / d)</c>: nearest integer, ties toward positive infinity (so -2.5 rounds to -2).
    /// Equal to <c>floor((2n + d) / 2d)</c> once the sign is moved onto the numerator.
    /// </summary>
    /// <exception cref="DivideByZeroException"><paramref name="d"/> is 0.</exception>
    public static long RoundHalfUp(long n, long d)
    {
        if (d < 0)
        {
            n = -n;
            d = -d;
        }
        return FloorDiv((2 * n) + d, 2 * d);
    }

    /// <summary>
    /// sim/core/fixed.ts roundDiv: nearest integer, ties away from zero, with the sign taken from
    /// <c>n &lt; 0 !== d &lt; 0</c>: <c>sign * floor((|n| + floor(|d| / 2)) / |d|)</c>.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="d"/> is 0 (the TS throws "roundDiv: division by zero").</exception>
    public static long RoundHalfAwayDiv(long n, long d)
    {
        if (d == 0)
        {
            throw new ArgumentException("roundDiv: division by zero", nameof(d));
        }
        var sign = (n < 0) != (d < 0) ? -1L : 1L;
        var an = Math.Abs(n);
        var ad = Math.Abs(d);
        return sign * ((an + (ad / 2)) / ad);
    }

    /// <summary>JS <c>n % d</c>: truncating remainder with the sign of <paramref name="n"/> (C# <c>%</c>; JS -0 is 0 here).</summary>
    /// <exception cref="DivideByZeroException"><paramref name="d"/> is 0.</exception>
    public static long JsRem(long n, long d) => n % d;

    /// <summary><c>Math.min(max, Math.max(min, value))</c>.</summary>
    public static long Clamp(long value, long min, long max) => Math.Min(max, Math.Max(min, value));
}
