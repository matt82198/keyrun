// RED spec for the C# oracle trace recorder (R-K4-10 a and b; game/sim/ORACLE.md sections 3, 6 and 10).
// Specified API (production code is NOT written in this lane):
//
//   namespace Keyrun.Bots.Oracle;
//   public static class OracleTraceRecorder
//   {
//       /// Drives the committed C# bots (the RolesOracleTraceTests.Drive loop) over fixture <name>'s setup and seed,
//       /// writes <outputDir>/<name>.trace.jsonl in the ORACLE.md section 3 format, and reports the file it wrote.
//       /// The manifest entry (traces[].properties) is not touched. Deterministic: integers, seeded PRNG, no wall clock.
//       public static OracleTraceRecord Record(string name, RoleSet roles, string outputDir);
//   }
//   public sealed record OracleTraceRecord(string Path, long Bytes, string Fnv, RunRecording Run);
//
// Fnv is the lowercase hex of the same Fnv1a the manifest uses, over the exact bytes of the written file.
using System;
using System.IO;
using Keyrun.Bots.Oracle;
using Keyrun.Content;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Keyrun.Tools.Replay;
using Xunit;
using Xunit.Abstractions;

namespace Keyrun.Bots.Tests;

public sealed class OracleTraceRecorderTests
{
    private const string Name = "bots-hv-k2-s7";

    private readonly ITestOutputHelper _output;

    public OracleTraceRecorderTests(ITestOutputHelper output) => _output = output;

    private static string OutDir()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "oracle-recorder", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact(DisplayName = "oracle recorder > bots-hv-k2-s7: the recorded trace parses, shows the manifest properties, and reports the bytes and fnv it wrote")]
    public void RecordsK2Seed7()
    {
        var rec = OracleTraceRecorder.Record(Name, RoleSet.Default, OutDir());
        _output.WriteLine($"{rec.Path} bytes {rec.Bytes} fnv {rec.Fnv}");

        // (1) the written trace parses with the loader RolesOracleTraceTests uses, and has at least one record.
        var trace = OracleTrace.Load(rec.Path);
        Assert.NotEmpty(RunRecording.FromTrace(trace).Ticks);

        // (2) replayed through the RolesOracleTraceTests harness, every manifest property for the k2-s7 row holds.
        // Gap: RolesOracleTraceTests.Drive takes a fixture name, not a path, so this checks the recorder's in-memory run
        // (built by the same loop) rather than a re-read of rec.Path.
        var properties = OracleProperties.Of(RolesOracleTraceTests.OracleDir(), Name);
        Assert.True(properties.Count > 0, $"{Name}: manifest.json states no property");
        var failures = OracleProperties.CheckAll(properties, rec.Run,
            other => RolesOracleTraceTests.Drive(other, RoleSet.Default).Run);
        Assert.True(failures.Count == 0, string.Join("\n", failures));

        // (3) the bytes and fnv the recorder reported match the file it wrote.
        var written = File.ReadAllBytes(rec.Path);
        Assert.Equal(written.LongLength, rec.Bytes);
        Assert.Equal(Fnv1a.Hex(written), rec.Fnv);
    }

    /// <summary>
    /// Re-record driver, env-gated: KEYRUN_RERECORD names comma-separated traces. Each is written straight into the
    /// oracle fixtures dir (the manifest row is not touched). Unset means the test returns without asserting.
    /// </summary>
    [Fact(DisplayName = "oracle recorder > KEYRUN_RERECORD: re-records each named trace into the oracle fixtures dir")]
    public void Rerecord()
    {
        var names = Environment.GetEnvironmentVariable("KEYRUN_RERECORD");
        if (string.IsNullOrWhiteSpace(names))
        {
            return;
        }
        var dir = RolesOracleTraceTests.OracleDir();
        foreach (var name in names.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var rec = OracleTraceRecorder.Record(name, RoleSet.Default, dir);
            Console.WriteLine($"RERECORD {name} path {rec.Path} bytes {rec.Bytes} fnv {rec.Fnv}");
        }
    }
}
