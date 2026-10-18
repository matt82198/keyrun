using System;
using Keyrun.Sim.Core;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// Direct cases for the IntMath sites the vector fixtures cannot reach: every fixed.ts roundDiv call in the
/// fixtures has a positive divisor (BP_SCALE or a length), so the TS sign rule <c>n &lt; 0 !== d &lt; 0</c> for a
/// negative divisor is pinned here. Expected values are the TS formula evaluated by hand.
/// </summary>
public sealed class IntMathTests
{
    [Theory]
    [InlineData(5, 2, 3)]     // 2.5 -> 3 (half away from zero)
    [InlineData(-5, 2, -3)]   // -2.5 -> -3
    [InlineData(5, -2, -3)]   // sign from n<0 !== d<0
    [InlineData(-5, -2, 3)]
    [InlineData(7, -3, -2)]   // -2.33 -> -2
    [InlineData(-8, -3, 3)]   // 2.67 -> 3
    [InlineData(0, -7, 0)]
    [InlineData(4, 8, 1)]     // 0.5 -> 1
    [InlineData(-4, 8, -1)]
    [InlineData(3, 8, 0)]
    public void RoundHalfAwayDiv_matches_fixed_ts_roundDiv(long n, long d, long expected)
    {
        Assert.Equal(expected, IntMath.RoundHalfAwayDiv(n, d));
    }

    [Fact]
    public void RoundHalfAwayDiv_by_zero_throws_like_the_ts()
    {
        Assert.Throws<ArgumentException>(() => IntMath.RoundHalfAwayDiv(1, 0));
    }

    [Theory]
    [InlineData(-5, 2, -2)]   // JS Math.round(-2.5) = -2
    [InlineData(5, 2, 3)]
    [InlineData(5, -2, -2)]   // n/d = -2.5 -> -2
    [InlineData(-5, -2, 3)]
    [InlineData(-7, 2, -3)]   // -3.5 -> -3
    public void RoundHalfUp_ties_go_toward_positive_infinity(long n, long d, long expected)
    {
        Assert.Equal(expected, IntMath.RoundHalfUp(n, d));
    }

    [Fact]
    public void JsNumber_integer_boundary()
    {
        Assert.True(JsNumber.TryParseInteger("-0", out var z) && z == 0);
        Assert.True(JsNumber.TryParseInteger("1.50e1", out var a) && a == 15);
        Assert.True(JsNumber.TryParseInteger("9007199254740991", out var max) && max == IntMath.MaxSafeInteger);
        Assert.False(JsNumber.TryParseInteger("9007199254740992", out _));
        Assert.False(JsNumber.TryParseInteger("1.5", out _));
        Assert.False(JsNumber.TryParseInteger("1e-1", out _));
        Assert.False(JsNumber.TryParseInteger("01", out _));
        Assert.False(JsNumber.TryParseInteger("+1", out _));
        Assert.False(JsNumber.TryParseInteger("1.", out _));
        Assert.False(JsNumber.TryParseInteger("", out _));
        Assert.Equal(4294967295u, JsNumber.ToUint32("-1"));
        Assert.Equal(4294967295u, JsNumber.ToUint32("-1.9"));
        Assert.Equal(0u, JsNumber.ToUint32("-0.5"));
        Assert.Equal(5u, JsNumber.ToUint32("4294967301"));
        Assert.Equal(12u, JsNumber.ToUint32("1.25e1"));
    }
}
