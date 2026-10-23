// Writes a re-recorded bot oracle trace (game/sim/ORACLE.md section 3) from the committed C# bots (R-K4-10).
// Only <outputDir>/<name>.trace.jsonl is written. manifest.json and the fixtures are never touched.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Keyrun.Bots.Tests;
using Keyrun.Sim.Core;
using Keyrun.Tools.Replay;

namespace Keyrun.Bots.Oracle;

/// <summary>A trace file the recorder wrote, with the bytes and FNV-1a 32 hex of the exact written bytes.</summary>
/// <param name="Path">The written file.</param>
/// <param name="Bytes">Byte length of the file.</param>
/// <param name="Fnv">FNV-1a 32 of the file bytes, 8 lowercase hex digits.</param>
/// <param name="Run">The recording the file was written from.</param>
public sealed record OracleTraceRecord(string Path, long Bytes, string Fnv, RunRecording Run);

/// <summary>Records a bot run over a fixture's setup and seed as an oracle trace file (ORACLE.md section 3).</summary>
public static class OracleTraceRecorder
{
    /// <summary>ORACLE.md section 3: a full projection line every 20 ticks.</summary>
    private const int FullEvery = 20;

    /// <summary>The frozen TS tree the setups came from (ORACLE.md section 3 source).</summary>
    private const string SourceSha = "6e1548d";

    /// <summary>The stop rule the bot loop runs to (RolesOracleTraceTests.MaxTicks).</summary>
    private const int MaxTicks = 24000;

    /// <summary>
    /// Drives <paramref name="name"/>'s setup and seed with the C# bots over <paramref name="roles"/> and writes the
    /// trace to <c>&lt;outputDir&gt;/&lt;name&gt;.trace.jsonl</c>. Deterministic: same inputs, same bytes.
    /// </summary>
    /// <param name="name">Fixture name (e.g. bots-hv-k2-s7).</param>
    /// <param name="roles">Role set the bots dispatch through.</param>
    /// <param name="outputDir">Directory to write into (created if missing).</param>
    /// <returns>The file, its byte length, its FNV-1a 32 hex and the recording.</returns>
    public static OracleTraceRecord Record(string name, RoleSet roles, string outputDir)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(outputDir);

        var source = OracleTrace.Load(System.IO.Path.Combine(RolesOracleTraceTests.OracleDir(), name + ".trace.jsonl"));
        var run = RolesOracleTraceTests.Drive(name, roles).Run;
        var bytes = Encode(name, source, run);

        Directory.CreateDirectory(outputDir);
        var path = System.IO.Path.Combine(outputDir, name + ".trace.jsonl");
        File.WriteAllBytes(path, bytes);
        return new OracleTraceRecord(path, bytes.LongLength, Fnv1a.Hex(bytes), run);
    }

    /// <summary>Renders the header and every record as ASCII with LF line endings (ORACLE.md section 3).</summary>
    private static byte[] Encode(string name, OracleTrace source, RunRecording run)
    {
        var ticks = run.Ticks;
        if (ticks.Count == 0)
        {
            throw new InvalidOperationException($"{name}: the bot run recorded no ticks");
        }
        var (result, resultTick) = SplitResult(run.Result);
        var last = ticks.Count - 1;
        var seed = I(source.Seed);

        var sb = new StringBuilder();
        // Header keys in sorted (canonical) order, as in the committed fixtures.
        sb.Append('{')
            .Append("\"finalTick\":").Append(I(ticks[last].T)).Append(',')
            .Append("\"firstTick\":").Append(I(ticks[0].T)).Append(',')
            .Append("\"fullEvery\":").Append(I(FullEvery)).Append(',')
            .Append("\"kind\":\"keyrun-oracle-trace\",")
            .Append("\"name\":").Append(Q(name)).Append(',')
            .Append("\"records\":").Append(I(ticks.Count)).Append(',')
            .Append("\"result\":").Append(result is null ? "null" : Q(result)).Append(',')
            .Append("\"resultTick\":").Append(resultTick is null ? "null" : I(resultTick.Value)).Append(',')
            .Append("\"scenario\":{")
            .Append("\"botPrngSeed\":").Append(seed).Append(',')
            .Append("\"botSlots\":\"all five party actors (human slot bot-driven), ascending actorId, skill dial 100\",")
            .Append("\"kind\":\"bots\",")
            .Append("\"maxTicks\":").Append(I(MaxTicks)).Append(',')
            .Append("\"runSeed\":").Append(seed)
            .Append("},")
            .Append("\"setup\":").Append(Q(source.SetupFile)).Append(',')
            .Append("\"source\":{\"sha\":\"").Append(SourceSha)
            .Append("\",\"tree\":\"sim/ bots/src/ content/ client/src/sim-host/ (frozen TypeScript)\"},")
            .Append("\"traceHash\":").Append(Q(run.TraceHash())).Append(',')
            .Append("\"version\":1}\n");

        for (var i = 0; i < ticks.Count; i++)
        {
            var tick = ticks[i];
            sb.Append("{\"t\":").Append(I(tick.T)).Append(",\"h\":").Append(Q(tick.H));
            if (tick.Line is not null && (i == 0 || i == last || tick.T % FullEvery == 0))
            {
                sb.Append(",\"p\":").Append(Q(tick.Line));
            }
            if (tick.Commands.Count > 0)
            {
                sb.Append(",\"c\":[").Append(string.Join(",", tick.Commands)).Append(']');
            }
            if (tick.Events.Count > 0)
            {
                sb.Append(",\"e\":[").Append(string.Join(",", tick.Events)).Append(']');
            }
            sb.Append("}\n");
        }

        var text = sb.ToString();
        if (text.Any(c => c > 0x7f))
        {
            throw new InvalidOperationException($"{name}: the trace is not ASCII");
        }
        return Encoding.ASCII.GetBytes(text);
    }

    /// <summary>Splits "result@tick" (RunRecording.Render); "none" has no result or tick.</summary>
    private static (string? Result, int? Tick) SplitResult(string rendered)
    {
        if (rendered == "none")
        {
            return (null, null);
        }
        var at = rendered.LastIndexOf('@');
        if (at < 0)
        {
            throw new InvalidOperationException($"unrecognised run end '{rendered}'");
        }
        return (rendered[..at], int.Parse(rendered[(at + 1)..], CultureInfo.InvariantCulture));
    }

    private static string I(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>A plain ASCII JSON string. Names, setup files and projection lines never need escaping (ORACLE.md section 1).</summary>
    private static string Q(string text)
    {
        if (text.Any(c => c == '"' || c == '\\' || c < 0x20 || c > 0x7f))
        {
            throw new ArgumentException($"'{text}' needs JSON escaping, which the oracle format does not use", nameof(text));
        }
        return "\"" + text + "\"";
    }
}
