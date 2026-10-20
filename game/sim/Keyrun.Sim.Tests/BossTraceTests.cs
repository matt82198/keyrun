using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Keyrun.Sim.Core;
using Keyrun.Sim.Runs;
using Xunit;
using static Keyrun.Sim.Tests.BossTestKit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// Lane 3F seeded boss trace: the armed Hollow Warden at 50% with the Twins, a drained-and-staggered Break bar, a
/// broken first Lock, then unbroken Locks into the enrage, recorded per tick as the projection hash plus the tick's
/// events. Two runs from the same seed must give identical hashes, and both must equal the committed golden
/// Fixtures/boss/hollow-warden-boss.trace.jsonl (the reason it is not an oracle trace is in Fixtures/boss/README.md).
/// Set KEYRUN_WRITE_BOSS_GOLDEN=1 to rewrite the golden; the diff then needs that stated reason in the PR.
/// </summary>
public sealed class BossTraceTests
{
    private const int Ticks = 2000;
    private const string GoldenName = "hollow-warden-boss.trace.jsonl";

    private static readonly Command[] Script =
    {
        new CastCommand(5, Warden, "warden.strike", Boss),
        new CastCommand(10, Cinder, "cinder.coldSnap", Boss),
        new CastCommand(40, Warden, "warden.shieldBash", TwinA),
        new CastCommand(42, Chanter, "chanter.rebuke", TwinB),
        new CastCommand(44, Cinder, "cinder.quench", TwinA),
    };

    /// <summary>Runs the scenario and returns the trace text (header line, then one line per tick).</summary>
    public static string Record(long seed)
    {
        var run = BuildRun(bossHealth: 30000, seed: seed);
        var lines = new List<string>();
        var body = new StringBuilder();
        while (run.State.Tick < Ticks)
        {
            var tick = run.State.Tick;
            var stepped = RunRoot.StepRun(run, Script.Where(c => c.Tick == tick).ToList());
            run = stepped.Run;
            var line = Projection.Line(run.State, run.Encounter.PullIndex, run.Encounter.SlackTicks);
            lines.Add(line);
            var events = string.Join(",", stepped.Events.Select(e => e.ToCanonicalJson()));
            body.Append("{\"t\":").Append(run.State.Tick)
                .Append(",\"h\":\"").Append(Fnv1a.Hex(Projection.RecordHash(line)))
                .Append("\",\"e\":[").Append(events).Append("]}\n");
        }
        var header = "{\"kind\":\"keyrun-boss-trace\",\"lane\":\"3F\",\"seed\":" + seed + ",\"ticks\":" + Ticks +
            ",\"traceHash\":\"" + Fnv1a.Hex(Projection.TraceHash(lines)) +
            "\",\"reason\":\"new behaviour (BIBLE s9 Twins + Lock), no TS oracle exists; see README.md\"}\n";
        return header + body;
    }

    private static string GoldenPath() =>
        Path.Combine(Path.GetDirectoryName(OracleFixtures.Directory)!, "boss", GoldenName);

    [Fact(DisplayName = "Boss trace: the same seed twice gives identical per-tick hashes")]
    public void Same_seed_is_deterministic()
    {
        var first = Record(Seed);
        var second = Record(Seed);
        Assert.Equal(first, second);
        Assert.Contains("\"type\":\"lock_broken\"", first);
        Assert.Contains("\"type\":\"stagger\"", first);
        Assert.Contains("\"type\":\"boss_enrage\"", first);
    }

    [Fact(DisplayName = "Boss trace: equals the committed golden byte for byte")]
    public void Matches_the_golden()
    {
        var actual = Record(Seed);
        if (Environment.GetEnvironmentVariable("KEYRUN_WRITE_BOSS_GOLDEN") == "1")
        {
            File.WriteAllText(GoldenPath(), actual, new UTF8Encoding(false));
        }
        var golden = File.ReadAllText(GoldenPath());
        Assert.Equal(golden, actual);
    }
}
