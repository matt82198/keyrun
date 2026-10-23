using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Keyrun.Sim.Core;

namespace Keyrun.Tools.Replay;

/// <summary>
/// One named property an oracle trace protects (manifest.json <c>traces[].properties[]</c>, game/sim/ORACLE.md section 10).
/// </summary>
/// <param name="Trace">The trace the property belongs to.</param>
/// <param name="Name">Its name, kebab-case, unique within the trace.</param>
/// <param name="Why">The history it comes from: the PR or ruling that created or re-recorded the oracle for it.</param>
/// <param name="Assert">The assertion object (its <c>kind</c> selects the check).</param>
public sealed record OracleProperty(string Trace, string Name, string Why, JsonElement Assert);

/// <summary>
/// Reads and evaluates the named oracle properties. Each property asserts something about a run's events and caller
/// commands (event type, actor, ability, tick or tick window, ordering), never a hash of the whole trace, so a change
/// that moves unrelated ticks leaves it green. Kinds (each may carry <c>requires</c>, a match that must occur at least
/// once, so a property over something that never happened fails as vacuous):
/// <list type="bullet">
/// <item><c>count</c>: events matching <c>match</c> number between <c>min</c> and <c>max</c> (either may be absent).</item>
/// <item><c>sequence</c>: each of <c>steps</c> matches an event after the previous step's event.</item>
/// <item><c>ticks</c>: the ticks of the events matching <c>match</c> are exactly <c>ticks</c>, in order.</item>
/// <item><c>every</c>: at least <c>min</c> (default 1) events match <c>match</c> and each also matches <c>has</c>.</item>
/// <item><c>precededBy</c>: at least <c>min</c> (default 1) events match <c>match</c>, and for each an earlier event
/// matches <c>by</c> with every <c>same</c> pair equal (<c>{"fieldOfMatch": "fieldOfBy"}</c>).</item>
/// <item><c>sameRecordsAs</c>: every record equals the same run of the oracle named <c>trace</c>.</item>
/// <item><c>leaves</c>: actor <c>actorId</c> is projected somewhere other than where it was first projected.</item>
/// <item><c>turnsWhenDue</c>: on every tick whose caller commands hold a matching <c>command</c> (an actor's targeted
/// cast) while that actor, as projected on the tick before (the state the command was decided on), is alive, not
/// casting and not facing the command's target, the same batch also holds a move by that actor; at least <c>min</c>
/// (default 1) such ticks occur. Needs the full projection of every tick (<see cref="NeedsEveryLine"/>).</item>
/// <item><c>all</c>: every assertion in <c>of</c> holds.</item>
/// </list>
/// A match is an object of field filters over the canonical JSON of an event (or command): a scalar must equal the
/// field; an array means "one of" for a scalar field and "equal to" for an array field; <c>{"min": a, "max": b}</c> is
/// an inclusive integer range. A field the event lacks never matches.
/// </summary>
public static class OracleProperties
{
    /// <summary>The properties manifest.json states for <paramref name="trace"/>, in manifest order.</summary>
    /// <param name="oracleDir">The fixture directory (holds manifest.json).</param>
    /// <param name="trace">The trace name.</param>
    /// <returns>The properties; empty when the trace states none.</returns>
    /// <exception cref="FormatException">The trace is not in the manifest, or a property is malformed.</exception>
    public static IReadOnlyList<OracleProperty> Of(string oracleDir, string trace)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(oracleDir, "manifest.json")));
        var entry = doc.RootElement.GetProperty("traces").EnumerateArray()
            .FirstOrDefault(t => t.GetProperty("name").GetString() == trace);
        if (entry.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException($"manifest.json has no trace {trace}");
        }
        if (!entry.TryGetProperty("properties", out var props))
        {
            return Array.Empty<OracleProperty>();
        }
        var list = new List<OracleProperty>();
        foreach (var p in props.EnumerateArray())
        {
            var name = p.GetProperty("name").GetString() ?? throw new FormatException($"{trace}: a property has no name");
            var why = p.GetProperty("why").GetString() ?? throw new FormatException($"{trace} {name}: no why");
            list.Add(new OracleProperty(trace, name, why, p.GetProperty("assert").Clone()));
        }
        if (list.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() != list.Count)
        {
            throw new FormatException($"{trace}: duplicate property names");
        }
        return list;
    }

    /// <summary>Evaluates one property over <paramref name="run"/>.</summary>
    /// <param name="property">The property.</param>
    /// <param name="run">The run.</param>
    /// <param name="sibling">Supplies the same kind of run for another oracle (for <c>sameRecordsAs</c>); may be null.</param>
    /// <returns>Null when it holds, else a one-line failure naming the property.</returns>
    public static string? Check(OracleProperty property, RunRecording run, Func<string, RunRecording>? sibling = null)
    {
        ArgumentNullException.ThrowIfNull(property);
        ArgumentNullException.ThrowIfNull(run);
        var failure = Evaluate(property.Assert, new Facts(run), sibling);
        return failure is null ? null : $"{run.Name} property {property.Name} FAILED: {failure}";
    }

    /// <summary>
    /// True when <paramref name="property"/> reads the full projection of every tick, which only a C# run has (a committed
    /// fixture carries a full line every 20 ticks).
    /// </summary>
    /// <param name="property">The property.</param>
    /// <returns>Whether it needs every line.</returns>
    public static bool NeedsEveryLine(OracleProperty property)
    {
        ArgumentNullException.ThrowIfNull(property);
        static bool Needs(JsonElement a) => a.GetProperty("kind").GetString() switch
        {
            "turnsWhenDue" => true,
            "all" => a.GetProperty("of").EnumerateArray().Any(Needs),
            _ => false,
        };
        return Needs(property.Assert);
    }

    /// <summary>Evaluates every property of <paramref name="run"/>'s oracle; returns the failures (empty when all hold).</summary>
    /// <param name="properties">The properties.</param>
    /// <param name="run">The run.</param>
    /// <param name="sibling">Supplies another oracle's run of the same kind; may be null.</param>
    /// <returns>Failure lines.</returns>
    public static IReadOnlyList<string> CheckAll(IEnumerable<OracleProperty> properties, RunRecording run, Func<string, RunRecording>? sibling = null)
    {
        ArgumentNullException.ThrowIfNull(properties);
        return properties.Select(p => Check(p, run, sibling)).OfType<string>().ToList();
    }

    /// <summary>A run's events (with their index in emission order) and per-tick command batches, parsed once.</summary>
    private sealed class Facts
    {
        public Facts(RunRecording run)
        {
            Run = run;
            Events = run.Ticks.SelectMany(t => t.Events).Select(Parse).ToList();
            Batches = run.Ticks.Where(t => t.Commands.Count > 0).Select(t => (t.T, (IReadOnlyList<JsonElement>)t.Commands.Select(Parse).ToList())).ToList();
            Lines = run.Ticks.Where(t => t.Line is not null).ToDictionary(t => t.T, t => t.Line!);
        }

        public RunRecording Run { get; }

        /// <summary>Full projection lines by tick (every tick for a C# run; every 20th for a committed fixture).</summary>
        public IReadOnlyDictionary<int, string> Lines { get; }

        public IReadOnlyList<JsonElement> Events { get; }

        public IReadOnlyList<(int T, IReadOnlyList<JsonElement> Commands)> Batches { get; }

        private static JsonElement Parse(string json)
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
    }

    // The assertion first (its own failure is the informative one), then the vacuity guard on a pass.
    private static string? Evaluate(JsonElement a, Facts facts, Func<string, RunRecording>? sibling)
    {
        if (EvaluateKind(a, facts, sibling) is { } failure)
        {
            return failure;
        }
        return a.TryGetProperty("requires", out var requires) && !facts.Events.Any(e => Matches(e, requires))
            ? $"vacuous: holds, but nothing matched requires {Compact(requires)}"
            : null;
    }

    private static string? EvaluateKind(JsonElement a, Facts facts, Func<string, RunRecording>? sibling)
    {
        var kind = a.GetProperty("kind").GetString();
        switch (kind)
        {
            case "count":
            {
                var match = a.GetProperty("match");
                var hits = facts.Events.Where(e => Matches(e, match)).ToList();
                var min = a.TryGetProperty("min", out var mn) ? mn.GetInt32() : 0;
                var max = a.TryGetProperty("max", out var mx) ? mx.GetInt32() : int.MaxValue;
                return hits.Count >= min && hits.Count <= max ? null
                    : string.Create(CultureInfo.InvariantCulture, $"{hits.Count} events match {Compact(match)}, want {min}..{(max == int.MaxValue ? "*" : max.ToString(CultureInfo.InvariantCulture))}{First(hits)}");
            }
            case "sequence":
            {
                var at = -1;
                foreach (var step in a.GetProperty("steps").EnumerateArray())
                {
                    var next = -1;
                    for (var i = at + 1; i < facts.Events.Count; i++)
                    {
                        if (Matches(facts.Events[i], step))
                        {
                            next = i;
                            break;
                        }
                    }
                    if (next < 0)
                    {
                        var after = at < 0 ? "the run start" : Compact(facts.Events[at]);
                        return $"no {Compact(step)} after {after}";
                    }
                    at = next;
                }
                return null;
            }
            case "ticks":
            {
                var match = a.GetProperty("match");
                var got = facts.Events.Where(e => Matches(e, match)).Select(e => e.GetProperty("tick").GetInt64()).ToList();
                var want = a.GetProperty("ticks").EnumerateArray().Select(t => t.GetInt64()).ToList();
                return got.SequenceEqual(want) ? null
                    : $"{Compact(match)} at ticks [{string.Join(",", got)}], want [{string.Join(",", want)}]";
            }
            case "every":
            {
                var match = a.GetProperty("match");
                var has = a.GetProperty("has");
                var hits = facts.Events.Where(e => Matches(e, match)).ToList();
                var min = a.TryGetProperty("min", out var mn) ? mn.GetInt32() : 1;
                if (hits.Count < min)
                {
                    return string.Create(CultureInfo.InvariantCulture, $"{hits.Count} events match {Compact(match)}, want at least {min}");
                }
                var bad = hits.FirstOrDefault(e => !Matches(e, has));
                return bad.ValueKind == JsonValueKind.Undefined ? null : $"{Compact(bad)} does not have {Compact(has)}";
            }
            case "precededBy":
            {
                var match = a.GetProperty("match");
                var by = a.GetProperty("by");
                var same = a.TryGetProperty("same", out var s) ? s.EnumerateObject().Select(p => (p.Name, p.Value.GetString()!)).ToList() : new List<(string, string)>();
                var min = a.TryGetProperty("min", out var mn) ? mn.GetInt32() : 1;
                var count = 0;
                for (var i = 0; i < facts.Events.Count; i++)
                {
                    var e = facts.Events[i];
                    if (!Matches(e, match))
                    {
                        continue;
                    }
                    count++;
                    var found = false;
                    for (var j = 0; j < i && !found; j++)
                    {
                        var b = facts.Events[j];
                        found = Matches(b, by) && same.All(pair =>
                            e.TryGetProperty(pair.Item1, out var ev) && b.TryGetProperty(pair.Item2, out var bv) && ev.GetRawText() == bv.GetRawText());
                    }
                    if (!found)
                    {
                        return $"{Compact(e)} has no earlier {Compact(by)} with the same {string.Join(", ", same.Select(p => p.Item1 + "=" + p.Item2))}";
                    }
                }
                return count >= min ? null
                    : string.Create(CultureInfo.InvariantCulture, $"{count} events match {Compact(match)}, want at least {min}");
            }
            case "sameRecordsAs":
            {
                var other = a.GetProperty("trace").GetString()!;
                if (sibling is null)
                {
                    return $"no run of {other} supplied to compare against";
                }
                // Record bodies and result only: the trace name differs by definition.
                // R-ROLL-1 (b): loot is seed-dependent by design (G2.3), so completion_roll events are not compared.
                var diff = WithoutCompletionRolls(facts.Run).FirstDifference(WithoutCompletionRolls(sibling(other)));
                return diff is null ? null : $"differs from {other}: {diff}";
            }
            case "leaves":
            {
                var id = a.GetProperty("actorId").GetInt32();
                (long X, long Z)? first = null;
                foreach (var t in facts.Lines.Keys.OrderBy(k => k))
                {
                    if (Actor(facts.Lines[t], id) is not { } p)
                    {
                        continue;
                    }
                    first ??= (p.X, p.Z);
                    if ((p.X, p.Z) != first.Value)
                    {
                        return null;
                    }
                }
                return first is null
                    ? string.Create(CultureInfo.InvariantCulture, $"actor {id} is never projected")
                    : string.Create(CultureInfo.InvariantCulture, $"actor {id} never leaves ({first.Value.X},{first.Value.Z})");
            }
            case "turnsWhenDue":
            {
                var command = a.GetProperty("command");
                var min = a.TryGetProperty("min", out var mn) ? mn.GetInt32() : 1;
                var due = 0;
                foreach (var (t, commands) in facts.Batches)
                {
                    foreach (var c in commands.Where(c => Matches(c, command)))
                    {
                        var actorId = c.GetProperty("actorId").GetInt32();
                        if (!c.TryGetProperty("targetId", out var tid) || !facts.Lines.TryGetValue(t - 1, out var before))
                        {
                            return string.Create(CultureInfo.InvariantCulture, $"t={t}: needs the command's targetId and the full projection of t={t - 1}");
                        }
                        if (Actor(before, actorId) is not { } self || Actor(before, tid.GetInt32()) is not { } target
                            || !self.Alive || self.Casting
                            || Fixed.AngleDiff(self.Yaw, Fixed.YawBetween(new Vec3(self.X, 0, self.Z), new Vec3(target.X, 0, target.Z))) == 0)
                        {
                            continue; // no turn was due on the state the command was decided on
                        }
                        due++;
                        if (!commands.Any(m => m.GetProperty("kind").GetString() == "move" && m.GetProperty("actorId").GetInt32() == actorId))
                        {
                            return string.Create(CultureInfo.InvariantCulture,
                                $"t={t}: {Compact(c)} while actor {actorId} (yaw {self.Yaw}) was not facing {tid.GetInt32()}, and no move in the same batch: [{string.Join(",", commands.Select(Compact))}]");
                        }
                    }
                }
                return due >= min ? null
                    : string.Create(CultureInfo.InvariantCulture, $"{due} ticks with {Compact(command)} had a turn due, want at least {min}");
            }
            case "all":
            {
                foreach (var inner in a.GetProperty("of").EnumerateArray())
                {
                    if (Evaluate(inner, facts, sibling) is { } failure)
                    {
                        return failure;
                    }
                }
                return null;
            }
            default:
                throw new FormatException($"unknown property kind {kind}");
        }
    }

    private static string First(List<JsonElement> hits) => hits.Count == 0 ? string.Empty : "; first " + Compact(hits[0]);

    /// <summary>One actor of a projection line (ORACLE.md section 1).</summary>
    private sealed record ProjectedActor(long X, long Z, int Yaw, bool Alive, bool Casting);

    // The actor with id `id` in projection line `line`, or null when it is not in the line.
    private static ProjectedActor? Actor(string line, int id)
    {
        var body = line[(line.IndexOf(";a=", StringComparison.Ordinal) + 3)..];
        var prefix = id.ToString(CultureInfo.InvariantCulture) + ",";
        foreach (var group in body.Split('|'))
        {
            if (!group.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }
            var f = group.Split(',');
            long L(int i) => long.Parse(f[i], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
            return new ProjectedActor(L(2), L(3), (int)L(4), f[5] == "1", f[7] != "-");
        }
        return null;
    }

    // Compact JSON of an element (manifest filters are laid out one member per line; events are canonical).
    /// <summary>A copy of the run with every <c>completion_roll</c> event removed (R-ROLL-1 (b)); the ticks, commands and result stay.</summary>
    /// <param name="run">The run to copy.</param>
    /// <returns>The run without completion-roll events.</returns>
    private static RunRecording WithoutCompletionRolls(RunRecording run) =>
        new(run.Name,
            run.Ticks.Select(t => new RecordedTick(t.T, t.H, t.Line, t.Commands,
                t.Events.Where(e => !e.Contains("\"type\":\"completion_roll\"", StringComparison.Ordinal)).ToList())).ToList(),
            run.Result);

    private static string Compact(JsonElement e) => JsonSerializer.Serialize(e);

    /// <summary>True when <paramref name="e"/> satisfies every field filter of <paramref name="match"/>.</summary>
    /// <param name="e">An event or command (canonical JSON object).</param>
    /// <param name="match">The filter object.</param>
    /// <returns>Whether it matches.</returns>
    public static bool Matches(JsonElement e, JsonElement match)
    {
        foreach (var f in match.EnumerateObject())
        {
            if (!e.TryGetProperty(f.Name, out var v) || !FieldMatches(v, f.Value))
            {
                return false;
            }
        }
        return true;
    }

    private static bool FieldMatches(JsonElement value, JsonElement filter)
    {
        switch (filter.ValueKind)
        {
            case JsonValueKind.Array when value.ValueKind == JsonValueKind.Array:
                return value.GetArrayLength() == filter.GetArrayLength()
                    && value.EnumerateArray().Zip(filter.EnumerateArray()).All(p => Compact(p.First) == Compact(p.Second));
            case JsonValueKind.Array:
                return filter.EnumerateArray().Any(f => FieldMatches(value, f));
            case JsonValueKind.Object:
                if (value.ValueKind != JsonValueKind.Number)
                {
                    return false;
                }
                var n = value.GetInt64();
                return (!filter.TryGetProperty("min", out var min) || n >= min.GetInt64())
                    && (!filter.TryGetProperty("max", out var max) || n <= max.GetInt64());
            default:
                return Compact(value) == Compact(filter);
        }
    }
}
