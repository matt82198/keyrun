using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// Loader for the frozen TypeScript oracle fixtures (game/sim/ORACLE.md). The fixtures are read in place
/// from the test project's source directory (found by walking up from the test binary), so no copy of the
/// 5 MB of data is made and the bytes compared are exactly the committed, hash-pinned ones.
/// </summary>
public static class OracleFixtures
{
    private static readonly Lazy<string> Root = new(FindRoot);

    /// <summary>Absolute path of Fixtures/oracle.</summary>
    public static string Directory => Root.Value;

    /// <summary>Absolute path of one fixture file.</summary>
    public static string PathOf(string fileName) => System.IO.Path.Combine(Directory, fileName);

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(System.IO.Path.Combine(dir.FullName, "Keyrun.Sim.Tests.csproj")))
            {
                var oracle = System.IO.Path.Combine(dir.FullName, "Fixtures", "oracle");
                if (!System.IO.Directory.Exists(oracle))
                {
                    throw new DirectoryNotFoundException($"oracle fixtures missing at {oracle}");
                }
                return oracle;
            }
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("could not locate Keyrun.Sim.Tests.csproj above " + AppContext.BaseDirectory);
    }

    /// <summary>One vector function block: its meta and its rows (each row a JSON array of cells).</summary>
    public sealed record VectorFunction(string Name, string From, IReadOnlyList<string> Columns, int Count, IReadOnlyList<JsonElement[]> Rows);

    /// <summary>Loads every function block of a vector file and checks the header and row counts.</summary>
    public static IReadOnlyDictionary<string, VectorFunction> LoadVectors(string fileName)
    {
        var text = File.ReadAllText(PathOf(fileName));
        var doc = JsonDocument.Parse(text);
        var root = doc.RootElement;
        var header = root.GetProperty("header");
        Assert.Equal(fileName, header.GetProperty("file").GetString());
        Assert.Equal("keyrun-oracle-vectors", header.GetProperty("kind").GetString());
        Assert.Equal("6e1548d", header.GetProperty("source").GetProperty("sha").GetString());
        var result = new Dictionary<string, VectorFunction>(StringComparer.Ordinal);
        foreach (var fn in root.GetProperty("functions").EnumerateArray())
        {
            var meta = fn.GetProperty("meta");
            var name = meta.GetProperty("name").GetString()!;
            var columns = meta.GetProperty("columns").EnumerateArray().Select(c => c.GetString()!).ToList();
            var count = meta.GetProperty("count").GetInt32();
            var rows = fn.GetProperty("rows").EnumerateArray().Select(r => r.EnumerateArray().ToArray()).ToList();
            Assert.Equal(count, rows.Count);
            foreach (var row in rows)
            {
                // A row lists every column, except a "throw" row, which lists the inputs then the single cell "throw".
                if (row.Length != columns.Count)
                {
                    Assert.True(row.Length >= 2 && IsThrow(row[^1]), $"{name}: row of {row.Length} cells for {columns.Count} columns is not a throw row");
                }
            }
            result.Add(name, new VectorFunction(name, meta.GetProperty("from").GetString()!, columns, count, rows));
        }
        return result;
    }

    /// <summary>True when a cell is the literal string "throw".</summary>
    public static bool IsThrow(JsonElement cell) =>
        cell.ValueKind == JsonValueKind.String && cell.GetString() == "throw";

    /// <summary>An integer cell as a long; fails the test on anything else.</summary>
    public static long Int(JsonElement cell)
    {
        Assert.Equal(JsonValueKind.Number, cell.ValueKind);
        return long.Parse(cell.GetRawText(), System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>A number cell's exact source text (used where the TS input may be a non-integer).</summary>
    public static string Raw(JsonElement cell)
    {
        Assert.Equal(JsonValueKind.Number, cell.ValueKind);
        return cell.GetRawText();
    }

    /// <summary>A trace file: its header element (with source text) and its record lines.</summary>
    public sealed record Trace(string Name, string HeaderLine, JsonElement Header, IReadOnlyList<string> RecordLines);

    /// <summary>The five oracle trace names in manifest order.</summary>
    public static readonly string[] TraceNames =
    {
        "content-pull", "scripted-pull", "bots-hv-k2-s7", "bots-hv-k2-s11", "bots-hv-k10-s7",
    };

    /// <summary>The four setup file names.</summary>
    public static readonly string[] SetupNames =
    {
        "content-pull.setup.json", "scripted-pull.setup.json", "hollow-vault-k2.setup.json", "hollow-vault-k10.setup.json",
    };

    /// <summary>Loads a trace, enforcing the ORACLE.md byte rules (ASCII, LF, trailing newline).</summary>
    public static Trace LoadTrace(string name)
    {
        var bytes = File.ReadAllBytes(PathOf(name + ".trace.jsonl"));
        Assert.DoesNotContain((byte)'\r', bytes);
        Assert.True(bytes.Length > 0 && bytes[^1] == (byte)'\n', "trace must end with a newline");
        Assert.True(bytes.All(b => b < 0x80), "trace must be ASCII");
        var text = System.Text.Encoding.ASCII.GetString(bytes);
        var lines = text[..^1].Split('\n');
        var header = JsonDocument.Parse(lines[0]).RootElement;
        Assert.Equal("keyrun-oracle-trace", header.GetProperty("kind").GetString());
        Assert.Equal(name, header.GetProperty("name").GetString());
        return new Trace(name, lines[0], header, lines.Skip(1).ToList());
    }
}
