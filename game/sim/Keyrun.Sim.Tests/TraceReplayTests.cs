using System.Collections.Generic;
using System.Linq;
using Keyrun.Tools.Replay;
using Xunit;
using Xunit.Abstractions;

namespace Keyrun.Sim.Tests;

/// <summary>
/// The five oracle traces as INPUTS to the C# run root (lane 2D): <see cref="Runs.RunRoot.CreateRun"/> on the setup
/// file's createRun inputs and seed, then one <see cref="Runs.RunRoot.StepRun"/> per record with that record's caller
/// commands (<c>c</c>). The C# run must show every named property manifest.json states for that trace (game/sim/ORACLE.md
/// section 10): event type, actor, ability, tick or tick window and ordering, never a whole-trace hash, so a casting or
/// rules change that keeps every property needs no re-recorded fixture. Determinism is proven once, without pinning
/// content: the same trace replayed twice in-process gives the same run, tick for tick.
/// </summary>
/// <remarks>
/// The bot traces replay the bots' RECORDED commands here: they prove the run root over the full Hollow Vault path
/// (four pulls, Slack spend, run end), not the bots. The bots generating those commands is Keyrun.Bots'
/// RolesOracleTraceTests, against the same properties.
/// </remarks>
public sealed class TraceReplayTests
{
    private readonly ITestOutputHelper _output;

    public TraceReplayTests(ITestOutputHelper output) => _output = output;

    private static RunRecording Record(string name) => TraceRecorder.RecordFile(OracleFixtures.PathOf(name + ".trace.jsonl"));

    private static RunRecording Fixture(string name) => RunRecording.FromTrace(OracleTrace.Load(OracleFixtures.PathOf(name + ".trace.jsonl")));

    public static IEnumerable<object[]> Traces() => OracleFixtures.TraceNames.Select(n => new object[] { n });

    [Theory]
    [MemberData(nameof(Traces))]
    public void The_csharp_run_of_each_oracle_shows_every_property_it_protects(string name)
    {
        var properties = OracleProperties.Of(OracleFixtures.Directory, name);
        Assert.True(properties.Count > 0, $"{name}: manifest.json states no property; a trace that protects nothing is vacuous");
        var run = Record(name);
        _output.WriteLine($"{name}: records {run.Ticks.Count} result {run.Result} properties {properties.Count}");
        Assert.Equal(Fixture(name).Ticks.Count, run.Ticks.Count);
        var failures = OracleProperties.CheckAll(properties, run, Record);
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Theory]
    [MemberData(nameof(Traces))]
    public void The_committed_fixture_shows_every_property_stated_for_it(string name)
    {
        // The properties are stated from the history that recorded each trace, so the recording itself must hold them.
        // A fixture carries a full projection only every 20 ticks, so a property that reads every tick's line (taunt
        // turns) is checked on the C# runs only.
        var properties = OracleProperties.Of(OracleFixtures.Directory, name).Where(p => !OracleProperties.NeedsEveryLine(p)).ToList();
        Assert.NotEmpty(properties);
        var failures = OracleProperties.CheckAll(properties, Fixture(name), Fixture);
        Assert.True(failures.Count == 0, string.Join("\n", failures));
    }

    [Fact]
    public void The_sim_is_deterministic_the_same_trace_replayed_twice_in_process_gives_the_same_run()
    {
        // The one whole-trace check: it compares the C# run with itself (same seed, same commands), never with a recording.
        var first = Record("bots-hv-k10-s7");
        var second = Record("bots-hv-k10-s7");
        _output.WriteLine($"bots-hv-k10-s7 twice: records {first.Ticks.Count} traceHash {first.TraceHash()} / {second.TraceHash()} result {first.Result}");
        Assert.True(first.Ticks.Count > 1, "a replay that steps nothing is vacuous");
        Assert.True(first.Ticks.Sum(t => t.Events.Count) > 0, "a replay that emits nothing is vacuous");
        var difference = first.FirstDifference(second);
        Assert.True(difference is null, $"the second in-process replay differs: {difference}");
        Assert.Equal(first.TraceHash(), second.TraceHash());
    }
}
