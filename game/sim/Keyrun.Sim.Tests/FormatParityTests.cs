using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Keyrun.Sim.Core;
using Xunit;
using static Keyrun.Sim.Tests.OracleFixtures;

namespace Keyrun.Sim.Tests;

/// <summary>
/// Parity of the oracle wire format (ORACLE.md sections 1, 2, 3 and 5) against every committed fixture:
/// FNV-1a 32 over every fixture file (manifest.json), the projection writer and record hash over every
/// full projection in all five traces, canonical JSON over every header, setup, event and command, and the
/// command validator over every recorded caller command. These need no rules, so they are fully in 1C scope.
/// </summary>
public sealed class FormatParityTests
{
    [Theory]
    [InlineData("", "811c9dc5")]
    [InlineData("a", "e40c292c")]
    [InlineData("foobar", "bf9cf968")]
    public void Fnv1a32_matches_the_oracle_test_vectors(string text, string expected)
    {
        Assert.Equal(expected, Fnv1a.Hex(Fnv1a.Hash(text)));
    }

    [Fact]
    public void Fnv1a32_rejects_non_ascii()
    {
        Assert.Throws<ArgumentException>(() => Fnv1a.Hash("caf" + (char)0xe9));
    }

    [Fact]
    public void Trace_hash_chains_lines_with_newlines()
    {
        Assert.Equal(Fnv1a.Hash("a\nbc\n"), Projection.TraceHash(new[] { "a", "bc" }));
        Assert.Equal(Fnv1a.Hash(string.Empty), Projection.TraceHash(Array.Empty<string>()));
    }

    [Fact]
    public void Manifest_bytes_and_fnv_match_every_fixture_file()
    {
        var manifest = JsonDocument.Parse(File.ReadAllText(PathOf("manifest.json"))).RootElement;
        var files = manifest.GetProperty("files").EnumerateObject().ToList();
        Assert.Equal(14, files.Count);
        foreach (var f in files)
        {
            var bytes = File.ReadAllBytes(PathOf(f.Name));
            Assert.Equal(f.Value.GetProperty("bytes").GetInt64(), bytes.LongLength);
            Assert.Equal(f.Value.GetProperty("fnv").GetString(), Fnv1a.Hex(Fnv1a.Hash(bytes)));
        }
    }

    /// <summary>Test-side reader of a projection line into actors (the inverse of the writer under test).</summary>
    private static (int Tick, int Pull, long Penalty, List<ActorState> Actors) ParseProjection(string p)
    {
        var parts = p.Split(';');
        Assert.Equal(4, parts.Length);
        Assert.StartsWith("t=", parts[0]);
        Assert.StartsWith("pull=", parts[1]);
        Assert.StartsWith("penalty=", parts[2]);
        Assert.StartsWith("a=", parts[3]);
        var actors = new List<ActorState>();
        var body = parts[3][2..];
        if (body.Length > 0)
        {
            foreach (var group in body.Split('|'))
            {
                var f = group.Split(',');
                Assert.Equal(9, f.Length);
                long L(string s) => long.Parse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
                // Deliberately reversed insertion order: the writer must sort by id itself.
                actors.Insert(0, new ActorState
                {
                    Id = (int)L(f[0]),
                    Health = L(f[1]),
                    Pos = new Vec3(L(f[2]), 0, L(f[3])),
                    Yaw = (int)L(f[4]),
                    Alive = f[5] == "1",
                    Resource = L(f[6]),
                    CastId = f[7] == "-" ? null : f[7],
                    CastEndsTick = f[8] == "-" ? null : (int)L(f[8]),
                });
            }
        }
        return (int.Parse(parts[0][2..], CultureInfo.InvariantCulture), int.Parse(parts[1][5..], CultureInfo.InvariantCulture),
            long.Parse(parts[2]["penalty=".Length..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture), actors);
    }

    public static IEnumerable<object[]> Traces() => TraceNames.Select(n => new object[] { n });

    [Theory]
    [MemberData(nameof(Traces))]
    public void Trace_records_projections_events_and_commands_round_trip(string name)
    {
        var trace = LoadTrace(name);

        // Header: canonical JSON, and it re-renders byte-identically.
        Assert.Equal(trace.HeaderLine, JsValue.Parse(trace.HeaderLine).ToCanonicalJson());
        var records = trace.Header.GetProperty("records").GetInt32();
        Assert.Equal(records, trace.RecordLines.Count);
        Assert.Equal(trace.Header.GetProperty("finalTick").GetInt32(), records - 1);

        int projections = 0, events = 0, commands = 0;
        for (var i = 0; i < trace.RecordLines.Count; i++)
        {
            var line = trace.RecordLines[i];
            var rec = JsonDocument.Parse(line).RootElement;
            var keys = rec.EnumerateObject().Select(p => p.Name).ToList();
            Assert.Equal(new[] { "t", "h" }, keys.Take(2));
            var t = rec.GetProperty("t").GetInt32();
            Assert.Equal(i, t);
            var h = rec.GetProperty("h").GetString()!;
            Assert.Matches("^[0-9a-f]{8}$", h);

            var mustHaveP = t == 0 || i == trace.RecordLines.Count - 1 || t % 20 == 0;
            Assert.Equal(mustHaveP, rec.TryGetProperty("p", out _));
            if (rec.TryGetProperty("p", out var pEl))
            {
                var p = pEl.GetString()!;
                Assert.Equal(h, Fnv1a.Hex(Fnv1a.Hash(p)));
                var (tick, pull, penalty, actors) = ParseProjection(p);
                Assert.Equal(t, tick);
                Assert.Equal(p, Projection.Line(tick, pull, penalty, actors));
                projections++;
            }
            if (rec.TryGetProperty("c", out var cEl))
            {
                Assert.True(cEl.GetArrayLength() > 0, $"t={t}: empty c must be omitted");
                foreach (var c in cEl.EnumerateArray())
                {
                    var cmd = CommandJson.Parse(c);
                    Assert.True(cmd is not null, $"t={t}: recorded command rejected: {c.GetRawText()}");
                    Assert.Equal(t - 1, cmd!.Tick);
                    Assert.Same(cmd, Commands.Validate(cmd));
                    Assert.Equal(c.GetRawText(), CommandJson.ToCanonicalJson(cmd));
                    commands++;
                }
            }
            if (rec.TryGetProperty("e", out var eEl))
            {
                Assert.True(eEl.GetArrayLength() > 0, $"t={t}: empty e must be omitted");
                foreach (var e in eEl.EnumerateArray())
                {
                    var raw = e.GetRawText();
                    Assert.Equal(raw, JsValue.FromJson(e).ToCanonicalJson());
                    var ev = SimEvent.FromJson(e);
                    Assert.Equal(raw, ev.ToCanonicalJson());
                    events++;
                }
            }
        }
        Assert.Equal((records - 1) / 20 + 1 + ((records - 1) % 20 == 0 ? 0 : 1), projections);
        Assert.True(events > 0, "trace must carry events");
        Assert.True(commands > 0, "trace must carry caller commands");
    }

    [Theory]
    [InlineData("content-pull.setup.json")]
    [InlineData("scripted-pull.setup.json")]
    [InlineData("hollow-vault-k2.setup.json")]
    [InlineData("hollow-vault-k10.setup.json")]
    public void Setup_files_are_canonical_json_that_round_trips(string file)
    {
        var text = File.ReadAllText(PathOf(file));
        Assert.EndsWith("\n", text);
        var line = text[..^1];
        Assert.DoesNotContain("\n", line);
        Assert.Equal(line, JsValue.Parse(line).ToCanonicalJson());
    }

    [Fact]
    public void Projection_renders_sorted_actors_dashes_and_negative_coordinates()
    {
        var a = new ActorState { Id = 7, Health = 10, Pos = new Vec3(-5, 99, 3), Yaw = 1023, Alive = false, Resource = 0 };
        var b = new ActorState { Id = 2, Health = 1, Pos = new Vec3(0, 0, -1), Yaw = 0, Alive = true, Resource = 4, CastId = "warden.brace", CastEndsTick = 40 };
        Assert.Equal("t=3;pull=1;penalty=-2;a=2,1,0,-1,0,1,4,warden.brace,40|7,10,-5,3,1023,0,0,-,-", Projection.Line(3, 1, -2, new[] { a, b }));
        Assert.Equal("t=0;pull=0;penalty=0;a=", Projection.Line(0, 0, 0, Array.Empty<ActorState>()));
    }

    [Theory]
    [InlineData("-")]
    [InlineData("bad id")]
    [InlineData("a|b")]
    [InlineData("")]
    public void Projection_refuses_an_unsafe_cast_token(string castId)
    {
        var a = new ActorState { Id = 1, CastId = castId };
        Assert.Throws<InvalidOperationException>(() => Projection.Line(0, 0, 0, new[] { a }));
    }

    [Fact]
    public void Projection_refuses_a_value_beyond_the_safe_integer_range()
    {
        var a = new ActorState { Id = 1, Health = IntMath.MaxSafeInteger + 1 };
        Assert.Throws<InvalidOperationException>(() => Projection.Line(0, 0, 0, new[] { a }));
    }

    [Fact]
    public void Canonical_json_sorts_keys_ordinally_and_escapes_like_json_stringify()
    {
        var v = JsValue.Obj(
            ("b", JsValue.Int(-1)),
            ("B", JsValue.Bool(true)),
            ("a", JsValue.Str("q\"\\\n\t\b\f\r\u0001/")),
            ("_", JsValue.Arr(JsValue.Null, JsValue.Int(0), JsValue.Str(""))));
        Assert.Equal("{\"B\":true,\"_\":[null,0,\"\"],\"a\":\"q\\\"\\\\\\n\\t\\b\\f\\r\\u0001/\",\"b\":-1}", v.ToCanonicalJson());
    }

    [Fact]
    public void Canonical_json_parse_rejects_non_integer_numbers()
    {
        Assert.Throws<ArgumentException>(() => JsValue.Parse("{\"x\":1.5}"));
        Assert.Equal("{\"x\":100}", JsValue.Parse("{\"x\":1e2}").ToCanonicalJson());
        Assert.Equal("{\"x\":0}", JsValue.Parse("{\"x\":-0}").ToCanonicalJson());
    }

    [Fact]
    public void Canonical_json_parse_keeps_the_last_duplicate_key_like_json_parse()
    {
        Assert.Equal("{\"a\":2}", JsValue.Parse("{\"a\":1,\"a\":2}").ToCanonicalJson());
    }
}
