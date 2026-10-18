using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Keyrun.Sim;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// Determinism gate for Keyrun.Sim (ENGINE-PLAN section 4): the rules engine must
/// be a pure function of (seed, command stream). It may not reference Godot, use
/// floating point, read a clock, or use System.Random. These tests run the
/// <see cref="PurityScanner"/> over the real Keyrun.Sim assembly, and also over
/// known-impure control types so a scanner that silently finds nothing goes red.
/// </summary>
public sealed class PurityTests
{
    private static readonly Assembly SimAssembly = typeof(SimInfo).Assembly;

    private static string Describe(IReadOnlyList<string> violations) =>
        violations.Count == 0 ? "(none)" : string.Join(Environment.NewLine, violations);

    [Fact]
    public void Sim_assembly_is_the_one_under_test()
    {
        Assert.Equal(SimInfo.Name, SimAssembly.GetName().Name);
        // Guard against a vacuous pass: the scan must see at least one type.
        Assert.NotEmpty(PurityScanner.TypesOf(SimAssembly));
    }

    [Fact]
    public void Sim_has_no_godot_assembly_reference()
    {
        var violations = PurityScanner.ScanAssemblyReferences(SimAssembly);
        Assert.True(violations.Count == 0, Describe(violations));
    }

    [Fact]
    public void Sim_metadata_references_no_forbidden_type()
    {
        var violations = PurityScanner.ScanTypeReferences(SimAssembly);
        Assert.True(violations.Count == 0, Describe(violations));
    }

    [Fact]
    public void Sim_members_and_method_bodies_are_integer_only_and_clock_free()
    {
        var violations = PurityScanner.ScanTypes(PurityScanner.TypesOf(SimAssembly));
        Assert.True(violations.Count == 0, Describe(violations));
    }

    // ---- Controls: the scanner must catch each class of impurity. ----

    [Theory]
    [InlineData(typeof(ImpureSamples.FloatField), "System.Single")]
    [InlineData(typeof(ImpureSamples.DoubleProperty), "System.Double")]
    [InlineData(typeof(ImpureSamples.DecimalParameter), "System.Decimal")]
    [InlineData(typeof(ImpureSamples.NullableFloatReturn), "System.Single")]
    [InlineData(typeof(ImpureSamples.FloatArrayField), "System.Single")]
    [InlineData(typeof(ImpureSamples.RandomLocal), "System.Random")]
    [InlineData(typeof(ImpureSamples.DateTimeCall), "System.DateTime")]
    [InlineData(typeof(ImpureSamples.StopwatchCall), "System.Diagnostics.Stopwatch")]
    [InlineData(typeof(ImpureSamples.FloatLiteral), "ldc.r8")]
    [InlineData(typeof(ImpureSamples.IntToDouble), "conv.r8")]
    [InlineData(typeof(ImpureSamples.MathSqrtCall), "System.Double")]
    [InlineData(typeof(ImpureSamples.EnvironmentTick), "System.Environment")]
    [InlineData(typeof(ImpureSamples.HashCodeCombine), "System.HashCode")]
    public void Scanner_flags_impure_control(Type sample, string expectedFragment)
    {
        var violations = PurityScanner.ScanTypes(new[] { sample });
        Assert.True(
            violations.Any(v => v.Contains(expectedFragment, StringComparison.Ordinal)),
            $"expected a violation mentioning '{expectedFragment}' for {sample.Name}; got:{Environment.NewLine}{Describe(violations)}");
    }

    [Fact]
    public void Scanner_passes_pure_control()
    {
        var violations = PurityScanner.ScanTypes(new[] { typeof(ImpureSamples.PureIntegerMath) });
        Assert.True(violations.Count == 0, Describe(violations));
    }

    [Fact]
    public void Scanner_flags_godot_named_assembly_reference()
    {
        Assert.True(PurityScanner.IsGodotAssemblyName("GodotSharp"));
        Assert.True(PurityScanner.IsGodotAssemblyName("Godot.SourceGenerators"));
        Assert.False(PurityScanner.IsGodotAssemblyName("Keyrun.Sim"));
        Assert.False(PurityScanner.IsGodotAssemblyName("System.Runtime"));
    }
}
