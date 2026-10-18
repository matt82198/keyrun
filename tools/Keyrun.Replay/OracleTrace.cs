using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Keyrun.Sim.Core;

namespace Keyrun.Tools.Replay;

/// <summary>One trace record (game/sim/ORACLE.md section 3).</summary>
/// <param name="T">Tick.</param>
/// <param name="H">Record hash, 8 lowercase hex digits.</param>
/// <param name="P">The full projection line, when present (t=0, the last record, every t % 20 == 0).</param>
/// <param name="Commands">Caller commands of the step INTO tick T (addressed to T-1).</param>
/// <param name="Events">That step's events as canonical JSON, in emission order.</param>
public sealed record TraceRecord(int T, string H, string? P, IReadOnlyList<Command> Commands, IReadOnlyList<string> Events);

/// <summary>
/// A parsed oracle trace: the header fields the replay needs and every record. Enforces the ORACLE.md byte rules
/// (ASCII, LF only, trailing newline) and the header's kind/version.
/// </summary>
/// <param name="Name">Trace name.</param>
/// <param name="SetupFile">The setup file name (same directory).</param>
/// <param name="Seed">The run seed (golden <c>scenario.seed</c>, bot <c>scenario.runSeed</c>).</param>
/// <param name="TraceHash">Header traceHash.</param>
/// <param name="Result">Header result (null for golden runs).</param>
/// <param name="ResultTick">Header resultTick.</param>
/// <param name="Records">Records in tick order.</param>
public sealed record OracleTrace(string Name, string SetupFile, long Seed, string TraceHash, string? Result, int? ResultTick, IReadOnlyList<TraceRecord> Records)
{
    /// <summary>Reads and validates a <c>&lt;name&gt;.trace.jsonl</c> file.</summary>
    /// <param name="path">The trace path.</param>
    /// <returns>The trace.</returns>
    /// <exception cref="FormatException">A byte-rule, header or record violation.</exception>
    public static OracleTrace Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length == 0 || bytes[^1] != (byte)'\n')
        {
            throw new FormatException($"{path}: a trace must end with a newline");
        }
        if (bytes.Any(b => b == (byte)'\r' || b >= 0x80))
        {
            throw new FormatException($"{path}: a trace must be ASCII with LF line endings");
        }
        var lines = Encoding.ASCII.GetString(bytes)[..^1].Split('\n');
        using var headerDoc = JsonDocument.Parse(lines[0]);
        var header = headerDoc.RootElement;
        if (header.GetProperty("kind").GetString() != "keyrun-oracle-trace" || header.GetProperty("version").GetInt32() != 1)
        {
            throw new FormatException($"{path}: not a version-1 keyrun-oracle-trace");
        }
        var scenario = header.GetProperty("scenario");
        var seed = scenario.TryGetProperty("seed", out var s) ? s.GetInt64()
            : scenario.TryGetProperty("runSeed", out var rs) ? rs.GetInt64()
            : throw new FormatException($"{path}: scenario has neither seed nor runSeed");
        var records = lines.Skip(1).Select(ParseRecord).ToList();
        var expectedCount = header.GetProperty("records").GetInt32();
        if (records.Count != expectedCount)
        {
            throw new FormatException(string.Create(CultureInfo.InvariantCulture, $"{path}: header says {expectedCount} records, file has {records.Count}"));
        }
        return new OracleTrace(
            header.GetProperty("name").GetString()!,
            header.GetProperty("setup").GetString()!,
            seed,
            header.GetProperty("traceHash").GetString()!,
            header.GetProperty("result").ValueKind == JsonValueKind.Null ? null : header.GetProperty("result").GetString(),
            header.GetProperty("resultTick").ValueKind == JsonValueKind.Null ? null : header.GetProperty("resultTick").GetInt32(),
            records);
    }

    private static TraceRecord ParseRecord(string line)
    {
        using var doc = JsonDocument.Parse(line);
        var r = doc.RootElement;
        var commands = r.TryGetProperty("c", out var c)
            ? c.EnumerateArray().Select(x => CommandJson.Parse(x) ?? throw new FormatException("unparseable command " + x.GetRawText())).ToList()
            : new List<Command>();
        var events = r.TryGetProperty("e", out var e)
            ? e.EnumerateArray().Select(x => JsValue.FromJson(x).ToCanonicalJson()).ToList()
            : new List<string>();
        return new TraceRecord(
            r.GetProperty("t").GetInt32(),
            r.GetProperty("h").GetString()!,
            r.TryGetProperty("p", out var p) ? p.GetString() : null,
            commands,
            events);
    }
}
