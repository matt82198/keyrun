using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// Tick-for-tick replay of the five oracle traces (ORACLE.md section 3). None of them depends only on the
/// lane-1C scope: record t=0 is createRun's output (sim/index.ts run root, lane 2D, fed by the content
/// adapter, lane 1D), and every step runs sim/combat/rules.ts (move handling with RulesConfig speeds and
/// collision, casting, damage: lanes 2A and 2B) and sim/encounter (scripts, chase, pulls, tempo: lane 2C).
/// The bot traces also need Keyrun.Bots (lanes 3A and 3B). Each test stays skipped, naming the lane that
/// unblocks it, until that lane replaces the body with the replay and the first-divergence report. The
/// format layer these replays will use (projection, record hash, commands, events) is already proven
/// against every record of all five traces by <see cref="FormatParityTests"/>.
/// </summary>
public sealed class TraceReplayTests
{
    [Fact(Skip = "needs the run root (createRun/stepRun, lane 2D GATE) on combat 2A/2B, encounter 2C and the content adapter 1D (PR #111)")]
    public void Content_pull_replays_every_tick() => Assert.Fail("unblocked by lane 2D");

    [Fact(Skip = "needs the run root (createRun/stepRun, lane 2D GATE) on combat 2A/2B and encounter 2C; content is tests/sim/integration/fixtures.ts via the setup file")]
    public void Scripted_pull_replays_every_tick() => Assert.Fail("unblocked by lane 2D");

    [Fact(Skip = "needs phase 2 (lanes 2A-2D) plus Keyrun.Bots (lanes 3A/3B GATE) and content 1D")]
    public void Bots_hollow_vault_key2_seed7_replays_every_tick() => Assert.Fail("unblocked by lane 3B");

    [Fact(Skip = "needs phase 2 (lanes 2A-2D) plus Keyrun.Bots (lanes 3A/3B GATE) and content 1D")]
    public void Bots_hollow_vault_key2_seed11_replays_every_tick() => Assert.Fail("unblocked by lane 3B");

    [Fact(Skip = "needs phase 2 (lanes 2A-2D) plus Keyrun.Bots (lanes 3A/3B GATE) and content 1D")]
    public void Bots_hollow_vault_key10_seed7_replays_every_tick() => Assert.Fail("unblocked by lane 3B");
}
