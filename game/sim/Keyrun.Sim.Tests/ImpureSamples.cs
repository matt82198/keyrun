using System;
using System.Diagnostics;

namespace Keyrun.Sim.Tests;

#pragma warning disable CS0649 // fields never assigned: they exist only to be scanned
#pragma warning disable CA1822

/// <summary>
/// Control types for <see cref="PurityTests"/>. Each nested type carries exactly
/// one impurity the scanner must report; <see cref="PureIntegerMath"/> carries none.
/// They live in the test assembly so Keyrun.Sim itself stays pure.
/// </summary>
internal static class ImpureSamples
{
    internal sealed class FloatField { public float Speed; }

    internal sealed class DoubleProperty { public double Ratio { get; set; } }

    internal sealed class DecimalParameter { public int Take(decimal amount) => amount > 0m ? 1 : 0; }

    internal sealed class NullableFloatReturn { public float? Maybe() => null; }

    internal sealed class FloatArrayField { public float[]? Samples; }

    internal sealed class RandomLocal
    {
        public int Roll()
        {
            var rng = new Random(7);
            return rng.Next(6);
        }
    }

    internal sealed class DateTimeCall { public long Ticks() => DateTime.UtcNow.Ticks; }

    internal sealed class StopwatchCall { public long Now() => Stopwatch.GetTimestamp(); }

    internal sealed class FloatLiteral
    {
        public int Half(int x) => (int)(x * 0.5);
    }

    internal sealed class IntToDouble
    {
        public long Widen(int x)
        {
            double d = x;
            return (long)d;
        }
    }

    internal sealed class MathSqrtCall { public int Root(int x) => (int)Math.Sqrt(x); }

    internal sealed class EnvironmentTick { public int Tick() => Environment.TickCount; }

    internal sealed class HashCodeCombine { public int Hash(int a, int b) => HashCode.Combine(a, b); }

    internal sealed class PureIntegerMath
    {
        private readonly int[] _table = { 1, 2, 3 };

        public long Distance2(int dx, int dz) => (long)dx * dx + (long)dz * dz;

        public int Clamp(int v) => Math.Clamp(v, 0, 1000);

        public int Sum()
        {
            var total = 0;
            foreach (var t in _table)
            {
                total += t;
            }
            return total;
        }
    }
}
