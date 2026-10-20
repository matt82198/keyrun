using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Keyrun.Sim.Core;
using Xunit;
using static Keyrun.Sim.Tests.OracleFixtures;

namespace Keyrun.Sim.Tests;

/// <summary>
/// Bit-exact parity of Keyrun.Sim.Core against the TS vector fixtures (ORACLE.md section 7). Every row of
/// every in-scope function is checked; a test fails on the first ten mismatching rows and also asserts the
/// number of rows checked equals the fixture's declared count, so an empty or truncated loop cannot pass.
/// Comparisons are exact integer equality: no epsilon, no float.
/// </summary>
public sealed class VectorParityTests
{
    private static readonly Lazy<IReadOnlyDictionary<string, VectorFunction>> FixedVectors = new(() => LoadVectors("fixed.vectors.json"));
    private static readonly Lazy<IReadOnlyDictionary<string, VectorFunction>> AtanVectors = new(() => LoadVectors("atan.vectors.json"));
    private static readonly Lazy<IReadOnlyDictionary<string, VectorFunction>> PrngVectors = new(() => LoadVectors("prng.vectors.json"));
    private static readonly Lazy<IReadOnlyDictionary<string, VectorFunction>> IntDivVectors = new(() => LoadVectors("intdiv.vectors.json"));

    /// <summary>Runs <paramref name="check"/> over every row; it returns null on a match or a mismatch description.</summary>
    private static void CheckAll(VectorFunction fn, Func<JsonElement[], string?> check)
    {
        var shown = new List<string>();
        var mismatchCount = 0;
        var checkedRows = 0;
        foreach (var row in fn.Rows)
        {
            checkedRows++;
            string? problem;
            try
            {
                problem = check(row);
            }
            catch (Exception ex)
            {
                problem = $"unexpected {ex.GetType().Name}: {ex.Message}";
            }
            if (problem is null)
            {
                continue;
            }
            mismatchCount++;
            if (shown.Count < 10)
            {
                shown.Add($"[{string.Join(",", row.Select(c => c.GetRawText()))}] -> {problem}");
            }
        }
        Assert.True(fn.Count > 0, $"{fn.Name}: fixture declares no rows");
        Assert.Equal(fn.Count, checkedRows);
        Assert.True(mismatchCount == 0, $"{fn.Name}: {mismatchCount} mismatches of {checkedRows} rows (first {shown.Count}):{Environment.NewLine}{string.Join(Environment.NewLine, shown)}");
    }

    private static string? ExpectEqual(long expected, long actual, string what) =>
        expected == actual ? null : $"{what}: expected {expected}, got {actual}";

    private static string? ExpectThrow(Action act)
    {
        try
        {
            act();
        }
        catch (ArgumentException)
        {
            return null;
        }
        return "expected an ArgumentException (TS throws), got a value";
    }

    // ---------------- intdiv.vectors.json ----------------

    [Fact]
    public void IntDiv_floor_trunc_ceil_round_rem_match_js()
    {
        CheckAll(IntDivVectors.Value["jsIntDiv"], row =>
        {
            long n = Int(row[0]), d = Int(row[1]);
            return ExpectEqual(Int(row[2]), IntMath.FloorDiv(n, d), "floor")
                ?? ExpectEqual(Int(row[3]), IntMath.TruncDiv(n, d), "trunc")
                ?? ExpectEqual(Int(row[4]), IntMath.CeilDiv(n, d), "ceil")
                ?? ExpectEqual(Int(row[5]), IntMath.RoundHalfUp(n, d), "round")
                ?? ExpectEqual(Int(row[6]), IntMath.JsRem(n, d), "rem");
        });
    }

    // ---------------- fixed.vectors.json ----------------

    [Fact]
    public void Fixed_isqrt_matches()
    {
        CheckAll(FixedVectors.Value["isqrt"], row =>
        {
            var raw = Raw(row[0]);
            if (IsThrow(row[1]))
            {
                // Negative or non-integer n: TS throws. The C# integer entry point for an untrusted JS number is
                // JsNumber.ToInteger, which rejects non-integers; Fixed.Isqrt rejects negatives.
                return ExpectThrow(() => Fixed.Isqrt(JsNumber.ToInteger(raw)));
            }
            return ExpectEqual(Int(row[1]), Fixed.Isqrt(JsNumber.ToInteger(raw)), "isqrt");
        });
    }

    [Fact]
    public void Fixed_length_matches()
    {
        CheckAll(FixedVectors.Value["length"], row =>
            ExpectEqual(Int(row[3]), Fixed.Length(new Vec3(Int(row[0]), Int(row[1]), Int(row[2]))), "length"));
    }

    [Fact]
    public void Fixed_scale_matches()
    {
        CheckAll(FixedVectors.Value["scale"], row =>
        {
            var r = Fixed.Scale(new Vec3(Int(row[0]), Int(row[1]), Int(row[2])), Int(row[3]));
            return ExpectEqual(Int(row[4]), r.X, "x") ?? ExpectEqual(Int(row[5]), r.Y, "y") ?? ExpectEqual(Int(row[6]), r.Z, "z");
        });
    }

    [Fact]
    public void Fixed_normaliseTo_matches()
    {
        CheckAll(FixedVectors.Value["normaliseTo"], row =>
        {
            var r = Fixed.NormaliseTo(new Vec3(Int(row[0]), Int(row[1]), Int(row[2])), Int(row[3]));
            return ExpectEqual(Int(row[4]), r.X, "x") ?? ExpectEqual(Int(row[5]), r.Y, "y") ?? ExpectEqual(Int(row[6]), r.Z, "z");
        });
    }

    [Fact]
    public void Fixed_yawBetween_matches()
    {
        CheckAll(FixedVectors.Value["yawBetween"], row =>
        {
            var from = new Vec3(Int(row[0]), 0, Int(row[1]));
            var to = new Vec3(Int(row[2]), 0, Int(row[3]));
            return ExpectEqual(Int(row[4]), Fixed.YawBetween(from, to), "yaw");
        });
    }

    [Fact]
    public void Fixed_angleDiff_matches()
    {
        CheckAll(FixedVectors.Value["angleDiff"], row =>
            ExpectEqual(Int(row[2]), Fixed.AngleDiff(Int(row[0]), Int(row[1])), "angleDiff"));
    }

    // ---------------- atan.vectors.json ----------------

    [Fact]
    public void Atan_yawFromComponents_matches()
    {
        CheckAll(AtanVectors.Value["yawFromComponents"], row =>
            ExpectEqual(Int(row[2]), Atan.YawFromComponents(Int(row[0]), Int(row[1])), "yaw"));
    }

    // ---------------- prng.vectors.json ----------------

    [Fact]
    public void Prng_seedState_matches()
    {
        CheckAll(PrngVectors.Value["seedState"], row =>
        {
            // TS seedState applies ToUint32 (negatives wrap, fractions truncate, 2^32 wraps to 0): JsNumber.ToUint32.
            var s = Prng.SeedState(JsNumber.ToUint32(Raw(row[0])));
            return ExpectEqual(Int(row[1]), s.S0, "s0") ?? ExpectEqual(Int(row[2]), s.S1, "s1")
                ?? ExpectEqual(Int(row[3]), s.S2, "s2") ?? ExpectEqual(Int(row[4]), s.S3, "s3");
        });
    }

    [Fact]
    public void Prng_nextInt_matches()
    {
        CheckAll(PrngVectors.Value["nextInt"], row =>
        {
            var state = new PrngState((uint)Int(row[0]), (uint)Int(row[1]), (uint)Int(row[2]), (uint)Int(row[3]));
            var raw = Raw(row[4]);
            if (IsThrow(row[5]))
            {
                return ExpectThrow(() => Prng.NextInt(state, JsNumber.ToInteger(raw)));
            }
            var (value, next) = Prng.NextInt(state, JsNumber.ToInteger(raw));
            return ExpectEqual(Int(row[5]), value, "value")
                ?? ExpectEqual(Int(row[6]), next.S0, "n0") ?? ExpectEqual(Int(row[7]), next.S1, "n1")
                ?? ExpectEqual(Int(row[8]), next.S2, "n2") ?? ExpectEqual(Int(row[9]), next.S3, "n3");
        });
    }

    // ---------------- botprng.vectors.json (parity owned by Keyrun.Bots.Tests) ----------------

    /// <summary>
    /// botprng.vectors.json pins bots/src/skill.ts seedPrng/stepPrng. Lane 3A (PR #130, 9c8a5ca) put that code in
    /// Keyrun.Bots, and its row-by-row parity runs in game/bots/Keyrun.Bots.Tests/BotPrngVectorTests.cs
    /// (SeedPrngMatchesOracle, StepPrngMatchesOracle). Keyrun.Sim.Tests does not reference Keyrun.Bots (stepPrng's
    /// value is an IEEE double, which Keyrun.Sim's purity rules forbid), so this side guards the fixture itself:
    /// it loads, its header and row counts check, and it holds exactly the two functions that file covers. A new
    /// function in the fixture turns this red until BotPrngVectorTests checks it too.
    /// </summary>
    [Fact]
    public void BotPrng_seedPrng_and_stepPrng_match()
    {
        var botPrng = LoadVectors("botprng.vectors.json");
        Assert.Equal(new[] { "seedPrng", "stepPrng" }, botPrng.Keys.ToArray());
        foreach (var fn in botPrng.Values)
        {
            Assert.True(fn.Count > 0, $"{fn.Name}: fixture declares no rows");
            Assert.Equal(fn.Count, fn.Rows.Count);
        }
    }

    /// <summary>Guard: every function in each in-scope vector file has a test above (no silently ignored block).</summary>
    [Fact]
    public void Every_in_scope_vector_function_is_covered()
    {
        Assert.Equal(new[] { "isqrt", "length", "scale", "normaliseTo", "yawBetween", "angleDiff" }, FixedVectors.Value.Keys.ToArray());
        Assert.Equal(new[] { "yawFromComponents" }, AtanVectors.Value.Keys.ToArray());
        Assert.Equal(new[] { "seedState", "nextInt" }, PrngVectors.Value.Keys.ToArray());
        Assert.Equal(new[] { "jsIntDiv" }, IntDivVectors.Value.Keys.ToArray());
    }
}
