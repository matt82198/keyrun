using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;

namespace Keyrun.Tools.Replay;

/// <summary>The first tick where the C# run diverged from the trace.</summary>
/// <param name="Tick">The record tick.</param>
/// <param name="What">What differed: "hash", "projection", "events", "result" or "traceHash".</param>
/// <param name="Expected">The trace's value (the full line when the record has one, else the hash).</param>
/// <param name="Actual">The C# value (the full projection line, or the hash/result).</param>
/// <param name="NearestFullLine">The nearest earlier full projection line of the trace (ORACLE.md: diff against it).</param>
/// <param name="ExpectedEvents">The record's events.</param>
/// <param name="ActualEvents">The C# step's events.</param>
public sealed record TickDivergence(int Tick, string What, string Expected, string Actual, string? NearestFullLine, IReadOnlyList<string> ExpectedEvents, IReadOnlyList<string> ActualEvents);

/// <summary>A replay's outcome.</summary>
/// <param name="Name">Trace name.</param>
/// <param name="TotalRecords">Records in the trace (t=0 included).</param>
/// <param name="TicksChecked">Records whose hash, line (when present) and events all matched, in order from t=0.</param>
/// <param name="FullLinesChecked">Full projection lines compared byte for byte.</param>
/// <param name="EventsChecked">Events compared (canonical JSON).</param>
/// <param name="CommandsReplayed">Caller commands fed to stepRun.</param>
/// <param name="ExpectedTraceHash">Header traceHash.</param>
/// <param name="ActualTraceHash">The C# trace hash (over the lines replayed).</param>
/// <param name="ExpectedResult">Header result and tick, as "result@tick" or "none".</param>
/// <param name="ActualResult">The C# run end, same rendering.</param>
/// <param name="Divergence">The first divergence, or null.</param>
public sealed record ReplayReport(
    string Name,
    int TotalRecords,
    int TicksChecked,
    int FullLinesChecked,
    int EventsChecked,
    int CommandsReplayed,
    string ExpectedTraceHash,
    string ActualTraceHash,
    string ExpectedResult,
    string ActualResult,
    TickDivergence? Divergence)
{
    /// <summary>True when every record, the trace hash and the result matched.</summary>
    public bool Matches => Divergence is null;

    /// <summary>One line: name, ticks checked of total, lines, events, commands, hashes, result, verdict.</summary>
    /// <returns>The summary line.</returns>
    public string Summary() => string.Create(CultureInfo.InvariantCulture,
        $"{Name}: ticks {TicksChecked}/{TotalRecords} fullLines {FullLinesChecked} events {EventsChecked} commands {CommandsReplayed} traceHash {ActualTraceHash} (expected {ExpectedTraceHash}) result {ActualResult} (expected {ExpectedResult}) {(Matches ? "MATCH" : "DIVERGED")}");

    /// <summary>The summary plus, on divergence, the tick, both values, the nearest full line and both event lists.</summary>
    /// <returns>A multi-line description.</returns>
    public string Describe()
    {
        var sb = new StringBuilder(Summary());
        if (Divergence is { } d)
        {
            sb.Append(CultureInfo.InvariantCulture, $"\nfirst divergence at t={d.Tick} ({d.What})");
            sb.Append("\n  expected: ").Append(d.Expected);
            sb.Append("\n  actual:   ").Append(d.Actual);
            if (d.NearestFullLine is not null)
            {
                sb.Append("\n  nearest earlier full line: ").Append(d.NearestFullLine);
            }
            sb.Append("\n  expected events:");
            foreach (var e in d.ExpectedEvents)
            {
                sb.Append("\n    ").Append(e);
            }
            sb.Append("\n  actual events:");
            foreach (var e in d.ActualEvents)
            {
                sb.Append("\n    ").Append(e);
            }
        }
        return sb.ToString();
    }
}

/// <summary>
/// Replays an oracle trace through <see cref="RunRoot"/> (ORACLE.md section 3, "Recommended C# check"): createRun on
/// the setup's inputs, then one stepRun per record with that record's commands; each record's hash, full line and
/// events must match, then the trace hash and the run result. Stops at the first divergence.
/// </summary>
public static class TraceReplayer
{
    /// <summary>Loads <paramref name="tracePath"/> and its setup (same directory, header <c>setup</c>) and replays it.</summary>
    /// <param name="tracePath">The trace file.</param>
    /// <returns>The report.</returns>
    public static ReplayReport ReplayFile(string tracePath)
    {
        var trace = OracleTrace.Load(tracePath);
        var dir = Path.GetDirectoryName(Path.GetFullPath(tracePath)) ?? ".";
        // R-BOTS-9: the trace is the frozen TS run, which has no verse rule (RunSetup.WithTsKit).
        var setup = RunSetup.Load(Path.Combine(dir, trace.SetupFile), trace.Seed).WithTsKit();
        return Replay(trace, setup);
    }

    /// <summary>Replays <paramref name="trace"/> on <paramref name="setup"/>.</summary>
    /// <param name="trace">The trace.</param>
    /// <param name="setup">The createRun inputs (its seed is used as given).</param>
    /// <returns>The report.</returns>
    public static ReplayReport Replay(OracleTrace trace, RunSetup setup)
    {
        ArgumentNullException.ThrowIfNull(trace);
        ArgumentNullException.ThrowIfNull(setup);
        var run = RunRoot.CreateRun(setup.Seed, setup.Party, setup.Dungeon, setup.Content, setup.Options);
        var lines = new List<string>();
        var checkedTicks = 0;
        var fullLines = 0;
        var eventsChecked = 0;
        var commands = 0;
        string? nearestFull = null;
        TickDivergence? divergence = null;

        foreach (var record in trace.Records)
        {
            IReadOnlyList<string> actualEvents = Array.Empty<string>();
            if (record.T != run.State.Tick + (record.T == 0 ? 0 : 1))
            {
                divergence = new TickDivergence(record.T, "tick", record.T.ToString(CultureInfo.InvariantCulture),
                    (run.State.Tick + 1).ToString(CultureInfo.InvariantCulture), nearestFull, record.Events, actualEvents);
                break;
            }
            if (record.T > 0)
            {
                var stepped = RunRoot.StepRun(run, record.Commands);
                run = stepped.Run;
                commands += record.Commands.Count;
                actualEvents = stepped.Events.Select(e => e.ToCanonicalJson()).ToList();
            }

            var line = Projection.Line(run.State, run.Encounter.PullIndex, run.Encounter.SlackTicks);
            var hash = Fnv1a.Hex(Projection.RecordHash(line));
            if (hash != record.H)
            {
                divergence = new TickDivergence(record.T, "hash", record.P ?? record.H, line, nearestFull, record.Events, actualEvents);
                break;
            }
            if (record.P is not null)
            {
                if (record.P != line)
                {
                    divergence = new TickDivergence(record.T, "projection", record.P, line, nearestFull, record.Events, actualEvents);
                    break;
                }
                fullLines++;
                nearestFull = record.P;
            }
            if (!record.Events.SequenceEqual(actualEvents, StringComparer.Ordinal))
            {
                divergence = new TickDivergence(record.T, "events", record.P ?? record.H, line, nearestFull, record.Events, actualEvents);
                break;
            }
            eventsChecked += actualEvents.Count;
            lines.Add(line);
            checkedTicks++;
        }

        var actualTraceHash = Fnv1a.Hex(Projection.TraceHash(lines));
        var expectedResult = Render(trace.Result, trace.ResultTick);
        var actualResult = Render(run.RunEnd.Result?.ToJs(), run.RunEnd.ResultTick);
        if (divergence is null && actualTraceHash != trace.TraceHash)
        {
            divergence = new TickDivergence(run.State.Tick, "traceHash", trace.TraceHash, actualTraceHash, nearestFull, Array.Empty<string>(), Array.Empty<string>());
        }
        if (divergence is null && actualResult != expectedResult)
        {
            divergence = new TickDivergence(run.State.Tick, "result", expectedResult, actualResult, nearestFull, Array.Empty<string>(), Array.Empty<string>());
        }
        return new ReplayReport(trace.Name, trace.Records.Count, checkedTicks, fullLines, eventsChecked, commands,
            trace.TraceHash, actualTraceHash, expectedResult, actualResult, divergence);
    }

    private static string Render(string? result, int? tick) =>
        result is null ? "none" : string.Create(CultureInfo.InvariantCulture, $"{result}@{tick}");
}
