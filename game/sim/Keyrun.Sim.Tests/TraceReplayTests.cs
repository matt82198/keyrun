using Keyrun.Tools.Replay;
using Xunit;
using Xunit.Abstractions;

namespace Keyrun.Sim.Tests;

/// <summary>
/// Tick-for-tick replay of the five oracle traces (ORACLE.md section 3) through the C# run root (lane 2D):
/// <see cref="Runs.RunRoot.CreateRun"/> on the setup file's createRun inputs, then one
/// <see cref="Runs.RunRoot.StepRun"/> per record with that record's caller commands (<c>c</c>). Every record's
/// projection hash, every full projection line, every event (canonical JSON, in emission order), the trace hash and
/// the run result are compared; the first mismatch fails with the tick, the expected and actual line (or the nearest
/// earlier full line) and both event lists.
/// </summary>
/// <remarks>
/// The bot traces replay the bots' RECORDED commands: they prove the run root over the full Hollow Vault path
/// (four pulls, Slack spend, wipe, run end), not the bots. Generating those commands is Keyrun.Bots (lanes 3A/3B).
/// </remarks>
public sealed class TraceReplayTests
{
    private readonly ITestOutputHelper _output;

    public TraceReplayTests(ITestOutputHelper output) => _output = output;

    private void AssertReplays(string name)
    {
        var report = TraceReplayer.ReplayFile(OracleFixtures.PathOf(name + ".trace.jsonl"));
        _output.WriteLine(report.Summary());
        Assert.True(report.Matches, report.Describe());
        Assert.Equal(report.TotalRecords, report.TicksChecked);
        Assert.True(report.TicksChecked > 1, "a replay that checks no step is vacuous");
    }

    [Fact]
    public void Content_pull_replays_every_tick() => AssertReplays("content-pull");

    [Fact]
    public void Scripted_pull_replays_every_tick() => AssertReplays("scripted-pull");

    [Fact]
    public void Bots_hollow_vault_key2_seed7_replays_every_tick() => AssertReplays("bots-hv-k2-s7");

    [Fact]
    public void Bots_hollow_vault_key2_seed11_replays_every_tick() => AssertReplays("bots-hv-k2-s11");

    [Fact]
    public void Bots_hollow_vault_key10_seed7_replays_every_tick() => AssertReplays("bots-hv-k10-s7");
}
