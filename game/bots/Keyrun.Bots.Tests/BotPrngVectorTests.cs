using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// Parity of <see cref="Skill.SeedPrng"/> and <see cref="Skill.StepPrng"/> against the frozen TS oracle's
/// botprng.vectors.json (game/sim/ORACLE.md section 7). Not a tests/bots/** port: an extra oracle gate.
/// </summary>
public sealed class BotPrngVectorTests
{
    private static readonly Lazy<JsonElement> Functions = new(Load);

    private static JsonElement Load()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Keyrun.sln")))
        {
            dir = dir.Parent;
        }
        if (dir is null)
        {
            throw new DirectoryNotFoundException("could not locate game/Keyrun.sln above " + AppContext.BaseDirectory);
        }
        var file = Path.Combine(dir.FullName, "sim", "Keyrun.Sim.Tests", "Fixtures", "oracle", "botprng.vectors.json");
        var root = JsonDocument.Parse(File.ReadAllText(file)).RootElement;
        Assert.Equal("6e1548d", root.GetProperty("header").GetProperty("source").GetProperty("sha").GetString());
        return root.GetProperty("functions").Clone();
    }

    private static (int Count, List<JsonElement[]> Rows) Rows(string name)
    {
        foreach (var fn in Functions.Value.EnumerateArray())
        {
            var meta = fn.GetProperty("meta");
            if (meta.GetProperty("name").GetString() == name)
            {
                var rows = fn.GetProperty("rows").EnumerateArray().Select(r => r.EnumerateArray().ToArray()).ToList();
                return (meta.GetProperty("count").GetInt32(), rows);
            }
        }
        throw new InvalidOperationException("no vector function " + name);
    }

    private static uint U(JsonElement e) => checked((uint)e.GetUInt64());

    [Fact(DisplayName = "botprng vectors > seedPrng matches every TS row, including the 2^53 double-product rows")]
    public void SeedPrngMatchesOracle()
    {
        var (count, rows) = Rows("seedPrng");
        Assert.Equal(count, rows.Count);
        Assert.True(rows.Count > 500);
        var above = 0;
        foreach (var row in rows)
        {
            var got = Skill.SeedPrng(row[0].GetDouble());
            var want = new BotPrngState(U(row[1]), U(row[2]), U(row[3]), U(row[4]));
            Assert.True(want == got, $"seedPrng({row[0].GetRawText()}): want {want} got {got}");
            if (row[5].GetBoolean())
            {
                above++;
            }
        }
        Assert.True(above > 0, "the fixture must exercise the above-2^53 product path");
    }

    [Fact(DisplayName = "botprng vectors > stepPrng matches every TS row bit for bit (state and double value)")]
    public void StepPrngMatchesOracle()
    {
        var (count, rows) = Rows("stepPrng");
        Assert.Equal(count, rows.Count);
        Assert.True(rows.Count > 9000);
        foreach (var row in rows)
        {
            var step = Skill.StepPrng(new BotPrngState(U(row[0]), U(row[1]), U(row[2]), U(row[3])));
            var want = new BotPrngState(U(row[5]), U(row[6]), U(row[7]), U(row[8]));
            Assert.True(want == step.Next, $"stepPrng next: want {want} got {step.Next}");
            var wantValue = row[4].GetDouble();
            Assert.True(BitConverter.DoubleToInt64Bits(wantValue) == BitConverter.DoubleToInt64Bits(step.Value), $"stepPrng value: want {wantValue:R} got {step.Value:R}");
        }
    }
}
