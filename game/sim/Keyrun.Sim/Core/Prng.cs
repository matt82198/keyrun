using System;

namespace Keyrun.Sim.Core;

/// <summary>The four uint32 words of a xoshiro128** state (sim/core/prng.ts PrngState). Immutable.</summary>
/// <param name="S0">Word 0.</param>
/// <param name="S1">Word 1.</param>
/// <param name="S2">Word 2.</param>
/// <param name="S3">Word 3.</param>
public readonly record struct PrngState(uint S0, uint S1, uint S2, uint S3);

/// <summary>
/// Port of sim/core/prng.ts: xoshiro128** seeded by splitmix32, pure and immutable (every draw returns the
/// advanced state). JS <c>&gt;&gt;&gt; 0</c> and <c>Math.imul</c> are uint32 wrap-around arithmetic here; the TS
/// products <c>s1 * 5</c> and <c>* 9</c> stay below 2^53 so their ToUint32 equals the unchecked uint product.
/// </summary>
/// <remarks>Pinned bit-exactly by prng.vectors.json (seedState, nextInt).</remarks>
public static class Prng
{
    private const ulong TwoPow32 = 0x1_0000_0000UL;

    private static uint Rotl(uint x, int k) => (x << k) | (x >> (32 - k));

    /// <summary>
    /// Derives a state from a uint32 seed (the TS applies ToUint32 first: use <see cref="JsNumber.ToUint32"/>
    /// for an untrusted JS number, or <see cref="SeedState(long)"/> for an integer). Never all-zero.
    /// </summary>
    public static PrngState SeedState(uint seed)
    {
        var a = seed;
        uint Gen()
        {
            unchecked
            {
                a += 0x9e3779b9u;
                var z = a;
                z = (z ^ (z >> 16)) * 0x21f0aaadu;
                z = (z ^ (z >> 15)) * 0x735a2d97u;
                z ^= z >> 15;
                return z;
            }
        }
        var s0 = Gen();
        var s1 = Gen();
        var s2 = Gen();
        var s3 = Gen();
        if (s0 == 0 && s1 == 0 && s2 == 0 && s3 == 0)
        {
            s0 = 1;
        }
        return new PrngState(s0, s1, s2, s3);
    }

    /// <summary>SeedState of an integer seed after JS ToUint32 (negative seeds wrap modulo 2^32).</summary>
    public static PrngState SeedState(long seed) => SeedState(unchecked((uint)seed));

    /// <summary>One xoshiro128** step: the raw uint32 output and the advanced state.</summary>
    public static (uint Raw, PrngState State) Next(PrngState state)
    {
        unchecked
        {
            var (s0, s1, s2, s3) = state;
            var result = Rotl(s1 * 5u, 7) * 9u;
            var t = s1 << 9;
            s2 ^= s0;
            s3 ^= s1;
            s1 ^= s2;
            s0 ^= s3;
            s2 ^= t;
            s3 = Rotl(s3, 11);
            return (result, new PrngState(s0, s1, s2, s3));
        }
    }

    /// <summary>
    /// An unbiased integer in [0, n) by rejection sampling below <c>floor(2^32 / n) * n</c>; n = 2^32 yields the
    /// raw output. Returns the value and the advanced state; the input state is never modified.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="n"/> &lt;= 0 (TS throws), or n &gt; 2^32
    /// (the TS loops forever there because its limit is 0; this port fails closed instead).</exception>
    public static (long Value, PrngState State) NextInt(PrngState state, long n)
    {
        if (n <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(n), "nextInt: n must be a positive integer");
        }
        if ((ulong)n > TwoPow32)
        {
            throw new ArgumentOutOfRangeException(nameof(n), "nextInt: n above 2^32 never returns in the TS oracle");
        }
        var un = (ulong)n;
        var limit = (TwoPow32 / un) * un;
        var s = state;
        for (;;)
        {
            var (raw, advanced) = Next(s);
            s = advanced;
            if (raw < limit)
            {
                return ((long)(raw % un), s);
            }
        }
    }
}
