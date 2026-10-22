using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Xunit;
using Xunit.Abstractions;
using Fork = System.ValueTuple<string, string, System.Collections.Generic.IReadOnlyList<System.ValueTuple<string, System.Collections.Generic.IReadOnlyList<Keyrun.Bots.Tests.ForkParityTests.RunMeasures>>>>;
using RunMeasures = Keyrun.Bots.Tests.ForkParityTests.RunMeasures;

namespace Keyrun.Bots.Tests;

/// <summary>
/// The fork parity tolerance comes from the checked-in seed-noise study (finding f-8ae6250a), never from a constant:
/// the file must be present and complete (fail closed), its tolerance must be K x its own recorded sds, and a sibling
/// that is worse by 3 sd on one axis must still gate while one worse by 1 sd must not.
/// </summary>
public sealed class ForkParityNoiseStudyTests
{
    private readonly ITestOutputHelper _output;

    public ForkParityNoiseStudyTests(ITestOutputHelper output) => _output = output;

    private static string StudyPath => ForkParityNoiseStudy.FilePath(RolesOracleTraceTests.OracleDir());

    private static string StudyText() => File.ReadAllText(StudyPath);

    [Fact(DisplayName = "fork parity noise study > the checked-in study is complete and its tolerance is 2 x its own measured sds (re-run with KEYRUN_FORKPARITY_NOISESTUDY=1)")]
    public void Study_file_derives_the_tolerance()
    {
        if (Environment.GetEnvironmentVariable(ForkParityNoiseStudy.StudyVariable) == "1")
        {
            File.WriteAllText(StudyPath, ForkParityNoiseStudy.Run(ForkParityNoiseStudy.Seeds, ForkParityNoiseStudy.HeadSha()));
            _output.WriteLine("STUDY REWRITTEN " + StudyPath);
        }
        Assert.True(File.Exists(StudyPath), $"{StudyPath} is missing: run with {ForkParityNoiseStudy.StudyVariable}=1 (fail closed)");
        var json = StudyText();
        var tolerance = ForkParityNoiseStudy.Load(json);
        foreach (var line in ForkParityNoiseStudy.HeaderLines(json))
        {
            _output.WriteLine(line);
        }
        _output.WriteLine("tolerance " + tolerance);
        Assert.Equal(tolerance, NoiseTolerance.Measured);
        var p = JsonNode.Parse(json)!["params"]!;
        Assert.True((int)p["seedLast"]! - (int)p["seedFirst"]! + 1 >= ForkParityNoiseStudy.MinSeeds);
        // A tolerance of zero on every axis would be the literal rule again, i.e. a study that measured no noise.
        Assert.True(tolerance.ClearBp > 0 && tolerance.Combos > 0, "a study that measures no clear or combo noise is vacuous: " + tolerance);
    }

    [Fact(DisplayName = "fork parity noise study > a missing, incomplete or hand-edited study file is refused (fail closed)")]
    public void Bad_study_files_fail_closed()
    {
        var good = StudyText();
        ForkParityNoiseStudy.Load(good);

        Assert.Throws<InvalidOperationException>(() => ForkParityNoiseStudy.Load("{}"));
        Assert.Throws<InvalidOperationException>(() => ForkParityNoiseStudy.Load(Edit(good, n => n.AsObject().Remove("mainSha"))));
        Assert.Throws<InvalidOperationException>(() => ForkParityNoiseStudy.Load(Edit(good, n => n["mainSha"] = "main")));
        Assert.Throws<InvalidOperationException>(() => ForkParityNoiseStudy.Load(Edit(good, n => n.AsObject().Remove("params"))));
        Assert.Throws<InvalidOperationException>(() => ForkParityNoiseStudy.Load(Edit(good, n => n["params"]!["seedLast"] = 12)));
        Assert.Throws<InvalidOperationException>(() => ForkParityNoiseStudy.Load(Edit(good, n => n["params"]!["k"] = 1)));
        Assert.Throws<InvalidOperationException>(() => ForkParityNoiseStudy.Load(Edit(good, n => n["keys"]!.AsObject().Remove("k10"))));
        // A hand-widened tolerance, or a median that is not its pairs' median, is refused.
        Assert.Throws<InvalidOperationException>(() => ForkParityNoiseStudy.Load(Edit(good, n => n["tolerance"]!["combos"] = (int)n["tolerance"]!["combos"]! + 1)));
        Assert.Throws<InvalidOperationException>(() => ForkParityNoiseStudy.Load(Edit(good, n => n["keys"]!["k2"]!["medianPairedMarginSd"]!["deaths"] = 9.0)));
        Assert.Throws<InvalidOperationException>(() => ForkParityNoiseStudy.Load(Edit(good, n => n.AsObject().Remove("tolerance"))));
    }

    private static string Edit(string json, Action<JsonNode> edit)
    {
        var node = JsonNode.Parse(json)!;
        edit(node);
        return node.ToJsonString();
    }

    /// <summary>The base kit's three hv runs (any plausible triple; only the margins matter).</summary>
    private static readonly RunMeasures[] Base =
    {
        new(2520, 0, 95, "timed"),
        new(2476, 0, 95, "timed"),
        new(3198, 1, 118, "timed"),
    };

    private static Fork Double(string ability, params (string Option, IReadOnlyList<RunMeasures> Runs)[] options) =>
        ("double", ability, options.Select(o => (o.Option, o.Runs)).ToList());

    /// <summary>
    /// The sd the tolerance is K times (the larger key's median paired-margin sd per axis), and a sibling that equals
    /// Base except that it is worse on one axis in one run by <paramref name="sigmas"/> of that sd (rounded up to the
    /// axis grain): it is strictly dominated by Base.
    /// </summary>
    private static IReadOnlyList<RunMeasures> WorseBy(string axis, double sigmas, int run)
    {
        var keys = ForkParityNoiseStudy.Summaries(StudyText());
        var copy = Base.ToArray();
        var r = copy[run];
        copy[run] = axis switch
        {
            // The clear sd is in bp of the slower clear (the dull one): d ticks slower is s sd when d = s * bp * (c + d) / 10000.
            "clear" => r with { ClearTick = r.ClearTick + (int)Math.Ceiling(sigmas * keys.Max(k => k.ClearBp) * r.ClearTick / (10000 - sigmas * keys.Max(k => k.ClearBp))) },
            "deaths" => r with { Deaths = r.Deaths + (int)Math.Ceiling(sigmas * keys.Max(k => k.Deaths)) },
            _ => r with { Combos = r.Combos - (int)Math.Ceiling(sigmas * keys.Max(k => k.Combos)) },
        };
        return copy;
    }

    [Theory(DisplayName = "fork parity noise study > a sibling worse by 3 sd on one axis is red through the parity failure path; worse by 1 sd is not")]
    [InlineData("clear")]
    [InlineData("deaths")]
    [InlineData("combos")]
    public void Three_sigma_dominance_gates_one_sigma_does_not(string axis)
    {
        var known = new Dictionary<string, string>();
        for (var run = 0; run < Base.Length; run++)
        {
            var three = WorseBy(axis, 3, run);
            Assert.True(ForkParityTests.Dominates(Base, three));
            var red = ForkParityTests.DominanceFailures(new[] { Double("double.worse", ("sharp", Base), ("dull", three)) }, known, NoiseTolerance.Measured);
            Assert.Single(red);
            Assert.StartsWith("double.worse:dull is dominated by sharp beyond noise", red[0]);
            Assert.Contains(axis + " ", red[0]);

            var one = WorseBy(axis, 1, run);
            Assert.True(ForkParityTests.Dominates(Base, one));
            Assert.Empty(ForkParityTests.DominanceFailures(new[] { Double("double.noise", ("sharp", Base), ("dull", one)) }, known, NoiseTolerance.Measured));
        }
    }
}
