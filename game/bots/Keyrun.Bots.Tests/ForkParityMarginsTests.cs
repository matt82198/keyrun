using System.Collections.Generic;
using System.Linq;
using Xunit;
using Fork = System.ValueTuple<string, string, System.Collections.Generic.IReadOnlyList<System.ValueTuple<string, System.Collections.Generic.IReadOnlyList<Keyrun.Bots.Tests.ForkParityTests.RunMeasures>>>>;
using RunMeasures = Keyrun.Bots.Tests.ForkParityTests.RunMeasures;

namespace Keyrun.Bots.Tests;

/// <summary>
/// The fork parity noise tolerance (finding f-e3daed76, R-PILE-7): a dominance inside the measured seed noise is
/// recorded in ForkParity.margins.txt but does not gate; a dominance beyond it still blocks, through the same
/// failure path <see cref="ForkParityTests.No_fork_option_is_dominated"/> asserts on. These cases pin the margin
/// mechanics against a fixed fixture tolerance (<see cref="Fixed"/>), so they do not move when the noise study is
/// re-run; the measured tolerance itself is covered by <see cref="ForkParityNoiseStudyTests"/>.
/// </summary>
public sealed class ForkParityMarginsTests
{
    private static readonly string[] RunNames = { "hv-k2-s7", "hv-k2-s11-d90", "hv-k10-s7" };

    /// <summary>Fixture tolerance for the margin mechanics (the pre-study 1-sd values): clear 715 bp, deaths 0, combos 3.</summary>
    private static readonly NoiseTolerance Fixed = new(715, 0, 3);

    /// <summary>The base kit's three hv runs on main a7069768 (1407/0/43, 1478/0/42, 1855/1/59).</summary>
    private static readonly RunMeasures[] Base =
    {
        new(1407, 0, 43, "timed"),
        new(1478, 0, 42, "timed"),
        new(1855, 1, 59, "timed"),
    };

    private static IReadOnlyList<RunMeasures> With(int run, RunMeasures changed)
    {
        var copy = Base.ToArray();
        copy[run] = changed;
        return copy;
    }

    private static Fork Double(string ability, params (string Option, IReadOnlyList<RunMeasures> Runs)[] options) =>
        ("double", ability, options.Select(o => (o.Option, o.Runs)).ToList());

    [Fact(DisplayName = "fork parity margins > a dominance inside the measured noise does not gate (the #307 staccato shape: 2 combos on equal clears)")]
    public void Noise_level_dominance_does_not_gate()
    {
        var twoCombos = With(0, Base[0] with { Combos = 45 });
        Assert.True(ForkParityTests.Dominates(twoCombos, Base));
        Assert.False(ForkParityMargins.DominatesBeyondNoise(twoCombos, Base, Fixed));

        var fiftyTicks = With(0, Base[0] with { ClearTick = 1357 });
        Assert.True(ForkParityTests.Dominates(fiftyTicks, Base));
        Assert.False(ForkParityMargins.DominatesBeyondNoise(fiftyTicks, Base, Fixed));
        Assert.Empty(ForkParityMargins.Excess(fiftyTicks, Base, Fixed, RunNames));
    }

    [Fact(DisplayName = "fork parity margins > a dominance beyond the measured noise gates; a trade-off never does")]
    public void Beyond_noise_dominance_gates()
    {
        // Beyond the noise in two scenarios (f-bee3bf93: one scenario alone does not gate, see the next case).
        var fourCombos = new[] { Base[0], Base[1] with { Combos = 46 }, Base[2] with { Combos = 63 } };
        Assert.True(ForkParityMargins.DominatesBeyondNoise(fourCombos, Base, Fixed));

        // 715 bp of 1407 is 100 ticks: 101 ticks faster is beyond the noise, 100 is not.
        Assert.True(ForkParityMargins.DominatesBeyondNoise(Faster(101), Base, Fixed));
        Assert.False(ForkParityMargins.DominatesBeyondNoise(Faster(100), Base, Fixed));

        var oneDeathFewer = new[] { Base[0], Base[1] with { Combos = 46 }, Base[2] with { Deaths = 0 } };
        Assert.True(ForkParityMargins.DominatesBeyondNoise(oneDeathFewer, Base, Fixed));
        Assert.Equal(new[] { "hv-k2-s11-d90 combos 4 > 3", "hv-k10-s7 deaths 1 > 0" }, ForkParityMargins.Excess(oneDeathFewer, Base, Fixed, RunNames));

        var tradeOff = new[] { Base[0] with { ClearTick = 1200 }, Base[1] with { Combos = 41 }, Base[2] };
        Assert.False(ForkParityMargins.DominatesBeyondNoise(tradeOff, Base, Fixed));
        Assert.Empty(ForkParityMargins.Excess(tradeOff, Base, Fixed, RunNames));

        // Zero tolerance is the bible's literal rule, per scenario: two scenarios with any strict margin.
        Assert.True(ForkParityMargins.DominatesBeyondNoise(new[] { Base[0] with { Combos = 44 }, Base[1] with { Combos = 43 }, Base[2] }, Base, NoiseTolerance.None));
    }

    private static IReadOnlyList<RunMeasures> Faster(int ticks) =>
        new[] { Base[0] with { ClearTick = 1407 - ticks }, Base[1] with { Combos = 46 }, Base[2] };

    [Fact(DisplayName = "fork parity margins > a margin beyond the noise in a single scenario does not gate, in two it does (f-bee3bf93)")]
    public void Single_scenario_beyond_noise_does_not_gate()
    {
        var oneDeathFewer = With(2, Base[2] with { Deaths = 0 });
        Assert.True(ForkParityTests.Dominates(oneDeathFewer, Base));
        Assert.False(ForkParityMargins.DominatesBeyondNoise(oneDeathFewer, Base, Fixed));
        Assert.Empty(ForkParityMargins.Excess(oneDeathFewer, Base, Fixed, RunNames));
        Assert.False(ForkParityMargins.DominatesBeyondNoise(With(1, Base[1] with { Combos = 46 }), Base, Fixed));

        // Two measures beyond the noise inside ONE scenario are still one scenario.
        Assert.False(ForkParityMargins.DominatesBeyondNoise(With(0, Base[0] with { ClearTick = 1000, Combos = 50 }), Base, Fixed));
        Assert.Equal(2, ForkParityMargins.MinScenariosBeyondNoise);
    }

    [Fact(DisplayName = "fork parity margins > a deliberately dominated fork (test double) is red through the parity failure path; a noise-level one is not")]
    public void A_deliberately_dominated_fork_is_red()
    {
        var dull = Base.Select(r => r with { ClearTick = r.ClearTick + 200, Combos = r.Combos - 6 }).ToList();
        var dominatedFork = Double("double.dominated", ("sharp", Base), ("dull", dull));
        var noiseFork = Double("double.noise", ("plain", Base), ("lucky", new[] { Base[0] with { Combos = 45 }, Base[1] with { Combos = 44 }, Base[2] }));
        var known = new Dictionary<string, string>();

        var red = ForkParityTests.DominanceFailures(new[] { dominatedFork, noiseFork }, known, Fixed);
        Assert.Single(red);
        Assert.StartsWith("double.dominated:dull is dominated by sharp beyond noise", red[0]);

        Assert.Empty(ForkParityTests.DominanceFailures(new[] { noiseFork }, known, Fixed));
        // The same noise fork is red under the literal zero-tolerance rule: the tolerance is what lets it through.
        Assert.Single(ForkParityTests.DominanceFailures(new[] { noiseFork }, known, NoiseTolerance.None));

        // A listed option that is only dominated inside the noise is stale (remove the entry).
        var staleKnown = new Dictionary<string, string> { ["double.noise:plain"] = "f-test" };
        var stale = ForkParityTests.DominanceFailures(new[] { noiseFork }, staleKnown, Fixed);
        Assert.Single(stale);
        Assert.Contains("double.noise:plain", stale[0]);
    }

    [Fact(DisplayName = "fork parity margins > every sibling pair gets one margins line that round-trips, and a changed pair set is reported")]
    public void Margins_lines_round_trip()
    {
        var dull = Base.Select(r => r with { ClearTick = r.ClearTick + 200, Combos = r.Combos - 6 }).ToList();
        var lucky = With(0, Base[0] with { Combos = 45 });
        var forks = new[] { Double("double.a", ("x", Base), ("y", dull), ("z", lucky)) };

        var lines = ForkParityMargins.Lines(forks, RunNames, Fixed);
        Assert.Equal(3, lines.Count);
        Assert.Equal(
            "double.a x y | hv-k2-s7 1407/0/43 1607/0/37 +200/+0/+6 | hv-k2-s11-d90 1478/0/42 1678/0/36 +200/+0/+6 | hv-k10-s7 1855/1/59 2055/1/53 +200/+0/+6 | x DOMINATES y beyond noise",
            lines[0]);
        Assert.EndsWith("| z dominates x within noise", lines[1]);
        Assert.EndsWith("| z DOMINATES y beyond noise", lines[2]);

        var text = ForkParityMargins.Render(lines, Fixed, null);
        var parsed = ForkParityMargins.Parse(text);
        Assert.Equal(new[] { "double.a x y", "double.a x z", "double.a y z" }, parsed.Keys.ToArray());
        Assert.Equal(lines[0], parsed["double.a x y"]);
        Assert.Empty(ForkParityMargins.PairSetChanges(parsed, lines));

        var grown = new[] { Double("double.a", ("x", Base), ("y", dull), ("z", lucky), ("w", Base)) };
        var changes = ForkParityMargins.PairSetChanges(parsed, ForkParityMargins.Lines(grown, RunNames, Fixed));
        Assert.Equal(new[] { "unrecorded pair: double.a x w", "unrecorded pair: double.a y w", "unrecorded pair: double.a z w" }, changes);
    }

    [Fact(DisplayName = "fork parity margins > drift beyond the noise between the recorded and the measured margins is reported, drift inside it is not")]
    public void Drift_is_reported_beyond_noise()
    {
        var recorded = ForkParityMargins.Lines(new[] { Double("double.a", ("x", Base), ("y", With(0, Base[0] with { Combos = 45 }))) }, RunNames, Fixed);
        var small = ForkParityMargins.Lines(new[] { Double("double.a", ("x", Base), ("y", With(0, Base[0] with { Combos = 46 }))) }, RunNames, Fixed);
        var large = ForkParityMargins.Lines(new[] { Double("double.a", ("x", Base), ("y", With(0, Base[0] with { Combos = 52 }))) }, RunNames, Fixed);
        var parsed = ForkParityMargins.Parse(ForkParityMargins.Render(recorded, Fixed, null));

        Assert.Empty(ForkParityMargins.Drift(parsed, small, Fixed));
        var drift = ForkParityMargins.Drift(parsed, large, Fixed);
        Assert.Single(drift);
        Assert.StartsWith("double.a x y: hv-k2-s7 combos margin -2 -> -9", drift[0]);
    }
}
