using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;

namespace Keyrun.Sim.Tests.Encounter;

/// <summary>One oracle trace record (ORACLE.md section 3) with its parsed commands and events.</summary>
/// <param name="T">Tick.</param>
/// <param name="P">Full projection line, when present.</param>
/// <param name="Commands">Caller commands addressed to tick T-1.</param>
/// <param name="Events">SimEvents of the step into T, in emission order.</param>
internal sealed record OracleRecord(int T, string? P, IReadOnlyList<Command> Commands, IReadOnlyList<SimEvent> Events);

/// <summary>One projected actor (ORACLE.md section 1).</summary>
internal sealed record ProjectedActor(int Id, long Hp, long X, long Z, int Yaw, bool Alive, long Res, string? Cast, int? CastEnd);

/// <summary>A parsed projection line.</summary>
internal sealed record ProjectedLine(int T, int Pull, long Slack, IReadOnlyList<ProjectedActor> Actors);

/// <summary>Trace and setup loading for the encounter oracle tests.</summary>
internal static class OracleRun
{
    public static IReadOnlyList<OracleRecord> Records(string trace) =>
        OracleFixtures.LoadTrace(trace).RecordLines.Select(line =>
        {
            var r = JsonDocument.Parse(line).RootElement;
            var cmds = r.TryGetProperty("c", out var c)
                ? c.EnumerateArray().Select(x => CommandJson.Parse(x) ?? throw new InvalidOperationException("bad command " + x)).ToList()
                : new List<Command>();
            var evs = r.TryGetProperty("e", out var e) ? e.EnumerateArray().Select(SimEvent.FromJson).ToList() : new List<SimEvent>();
            return new OracleRecord(r.GetProperty("t").GetInt32(), r.TryGetProperty("p", out var p) ? p.GetString() : null, cmds, evs);
        }).ToList();

    /// <summary>
    /// A C# run (<see cref="Keyrun.Tools.Replay.TraceRecorder"/>) as records, with the full projection on EVERY record, so
    /// every tick of a replay over it is exact (state synced from the run itself, never from a TS recording).
    /// </summary>
    public static IReadOnlyList<OracleRecord> RecordsOf(Keyrun.Tools.Replay.RunRecording run) =>
        run.Ticks.Select(t =>
        {
            static JsonElement Parse(string json)
            {
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.Clone();
            }
            var cmds = t.Commands.Select(c => CommandJson.Parse(Parse(c)) ?? throw new InvalidOperationException("bad command " + c)).ToList();
            var evs = t.Events.Select(e => SimEvent.FromJson(Parse(e))).ToList();
            return new OracleRecord(t.T, t.Line, cmds, evs);
        }).ToList();

    public static string SetupNameOf(string trace) =>
        OracleFixtures.LoadTrace(trace).Header.GetProperty("setup").GetString()!;

    public static ProjectedLine Parse(string p)
    {
        var parts = p.Split(';');
        int I(string s) => int.Parse(s, CultureInfo.InvariantCulture);
        long L(string s) => long.Parse(s, CultureInfo.InvariantCulture);
        var t = I(parts[0]["t=".Length..]);
        var pull = I(parts[1]["pull=".Length..]);
        var slack = L(parts[2]["slack=".Length..]);
        var body = parts[3]["a=".Length..];
        var actors = body.Length == 0
            ? new List<ProjectedActor>()
            : body.Split('|').Select(a =>
            {
                var f = a.Split(',');
                return new ProjectedActor(I(f[0]), L(f[1]), L(f[2]), L(f[3]), I(f[4]), f[5] == "1", L(f[6]),
                    f[7] == "-" ? null : f[7], f[8] == "-" ? null : I(f[8]));
            }).ToList();
        return new ProjectedLine(t, pull, slack, actors);
    }

    /// <summary>The party as createRun builds it (sim/combat/actors.ts:19 createActor over setup.party).</summary>
    public static List<ActorState> Party(JsObject setup) =>
        ((JsArray)setup.Get("party")!).Items.Cast<JsObject>().Select(m =>
        {
            var pos = (JsObject)m.Get("pos")!;
            long N(JsObject o, string k) => ((JsInt)o.Get(k)!).Value;
            return new ActorState
            {
                Id = (int)N(m, "id"),
                Kind = ActorKind.Player,
                ClassId = ((JsString)m.Get("classId")!).Value,
                Role = ActorTags.ParseRole(((JsString)m.Get("role")!).Value),
                Pos = new Vec3(N(pos, "x"), N(pos, "y"), N(pos, "z")),
                Yaw = m.Get("yaw") is JsInt y ? (int)y.Value : 0,
                Health = N(m, "maxHealth"),
                MaxHealth = N(m, "maxHealth"),
                Resource = N(m, "maxResource"),
                MaxResource = N(m, "maxResource"),
            };
        }).ToList();

    public static int KeyLevel(JsObject setup) =>
        ((JsObject)setup.Get("content")!).Get("keyLevel") is JsInt k ? (int)k.Value : 1;

    public static int ParTicks(JsObject setup) =>
        ((JsObject)setup.Get("content")!).Get("parTicks") is JsInt k ? (int)k.Value : 24000;

    /// <summary>The abilities' window ids (the first "window" effect op's windowId), for the Seed loot rule.</summary>
    public static Func<string, string?> WindowIdOf(JsObject setup)
    {
        var abilities = (JsObject)((JsObject)setup.Get("content")!).Get("abilities")!;
        return id =>
        {
            if (abilities.Get(id) is not JsObject a || a.Get("effects") is not JsArray effects)
            {
                return null;
            }
            return effects.Items.OfType<JsObject>()
                .Where(op => op.Get("op") is JsString { Value: "window" })
                .Select(op => ((JsString)op.Get("windowId")!).Value)
                .FirstOrDefault();
        };
    }
}
