using System.Collections.Generic;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// The Key band's readiness line and the private last-three-runs strip (BIBLE s10). Pure: the Godot screen only draws
/// what <see cref="ScreenState.ReadinessLine"/> and <see cref="RunStripText.Lines"/> return.
/// </summary>
public sealed class ScreenBandTests
{
    private const string Dungeon = "hollow-vault";

    private static readonly IReadOnlyList<string> Four = new[] { "echo", "tide", "hush", "pact" };

    [Fact]
    public void An_open_band_reads_ready_for_its_key()
    {
        var s = ScreenState.Open(Dungeon, Four, 5, 0, ScreenState.NoClauseStartKey);
        Assert.Equal("Ready: key 5 can be queued now.", s.ReadinessLine);
    }

    [Fact]
    public void A_queued_band_reads_that_it_waits_for_the_run_to_start()
    {
        var (queued, _) = ScreenState.Open(Dungeon, Four, 5, 0, ScreenState.NoClauseStartKey).Queue();
        Assert.Equal("Queued at key 5: waiting for the run to start.", queued.ReadinessLine);
    }

    [Fact]
    public void The_strip_shows_the_last_three_runs_most_recent_first()
    {
        var history = new[]
        {
            new RunRecord(2, RunResult.Depleted, false),
            new RunRecord(3, RunResult.Timed, false),
            new RunRecord(4, RunResult.Abandoned, false),
            new RunRecord(5, RunResult.Timed, false),
        };
        var lines = RunStripText.Lines(history);
        Assert.Equal(new[] { "Key 5, timed", "Key 4, abandoned", "Key 3, timed" }, lines);
    }

    [Fact]
    public void The_strip_is_empty_before_any_run_is_banked()
    {
        Assert.Empty(RunStripText.Lines(new List<RunRecord>()));
    }
}
