using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;

namespace Keyrun.Tools.Replay;

/// <summary>One recorded tick of a run, in the oracle trace's shape (game/sim/ORACLE.md section 3).</summary>
/// <param name="T">Tick.</param>
/// <param name="H">Record hash, 8 lowercase hex digits.</param>
/// <param name="Line">The full projection line; null only for a committed fixture record that carries no <c>p</c>.</param>
/// <param name="Commands">Caller commands of the step INTO tick T (addressed to T-1), as canonical JSON.</param>
/// <param name="Events">That step's events as canonical JSON, in emission order.</param>
public sealed record RecordedTick(int T, string H, string? Line, IReadOnlyList<string> Commands, IReadOnlyList<string> Events);

/// <summary>
/// A run as data: every tick's projection, commands and events, and the run end. Produced by the C# run root
/// (<see cref="TraceRecorder"/>, or a bot driver) or read from a committed fixture (<see cref="FromTrace"/>), so the
/// named oracle properties (<see cref="OracleProperties"/>) read the same shape whichever side produced it.
/// </summary>
/// <param name="Name">Trace name.</param>
/// <param name="Ticks">Records in tick order from t=0.</param>
/// <param name="Result">The run end as "result@tick", or "none".</param>
public sealed record RunRecording(string Name, IReadOnlyList<RecordedTick> Ticks, string Result)
{
    /// <summary>FNV-1a 32 over every projection line (ORACLE.md section 2); only meaningful when every line is known.</summary>
    /// <returns>The trace hash, 8 lowercase hex digits.</returns>
    /// <exception cref="InvalidOperationException">A tick has no projection line (a committed fixture record without <c>p</c>).</exception>
    public string TraceHash()
    {
        if (Ticks.Any(t => t.Line is null))
        {
            throw new InvalidOperationException($"{Name}: a recording read from a fixture has no full line on every tick");
        }
        return Fnv1a.Hex(Projection.TraceHash(Ticks.Select(t => t.Line!)));
    }

    /// <summary>The first tick where this recording and <paramref name="other"/> differ (hash, commands or events), or null.</summary>
    /// <param name="other">The recording to compare.</param>
    /// <returns>A one-line description of the first difference, or null when every tick and the result match.</returns>
    public string? FirstDifference(RunRecording other)
    {
        ArgumentNullException.ThrowIfNull(other);
        var n = Math.Min(Ticks.Count, other.Ticks.Count);
        for (var i = 0; i < n; i++)
        {
            var a = Ticks[i];
            var b = other.Ticks[i];
            if (a.T != b.T || a.H != b.H)
            {
                return string.Create(CultureInfo.InvariantCulture, $"t={a.T}: projection {a.Line ?? a.H} vs {b.Line ?? b.H}");
            }
            if (!a.Commands.SequenceEqual(b.Commands, StringComparer.Ordinal))
            {
                return string.Create(CultureInfo.InvariantCulture, $"t={a.T}: commands [{string.Join(",", a.Commands)}] vs [{string.Join(",", b.Commands)}]");
            }
            if (!a.Events.SequenceEqual(b.Events, StringComparer.Ordinal))
            {
                return string.Create(CultureInfo.InvariantCulture, $"t={a.T}: events {string.Join(" ", a.Events)} vs {string.Join(" ", b.Events)}");
            }
        }
        if (Ticks.Count != other.Ticks.Count)
        {
            return string.Create(CultureInfo.InvariantCulture, $"records {Ticks.Count} vs {other.Ticks.Count}");
        }
        return Result == other.Result ? null : $"result {Result} vs {other.Result}";
    }

    /// <summary>A committed fixture as a recording (its own commands, events and hashes; lines where it has <c>p</c>).</summary>
    /// <param name="trace">The parsed fixture.</param>
    /// <returns>The recording.</returns>
    public static RunRecording FromTrace(OracleTrace trace)
    {
        ArgumentNullException.ThrowIfNull(trace);
        var ticks = trace.Records
            .Select(r => new RecordedTick(r.T, r.H, r.P, r.Commands.Select(CommandJson.ToCanonicalJson).ToList(), r.Events))
            .ToList();
        return new RunRecording(trace.Name, ticks, Render(trace.Result, trace.ResultTick));
    }

    /// <summary>A run end rendered as "result@tick", or "none".</summary>
    /// <param name="result">The result, or null.</param>
    /// <param name="tick">Its tick.</param>
    /// <returns>The rendering.</returns>
    public static string Render(string? result, int? tick) =>
        result is null ? "none" : string.Create(CultureInfo.InvariantCulture, $"{result}@{tick}");
}

/// <summary>
/// Records a C# run that takes a committed oracle trace as its INPUT: the setup file's createRun inputs and seed, then
/// each record's caller commands fed to <see cref="RunRoot.StepRun"/>, for as many steps as the trace has records.
/// Nothing is compared with the trace's recorded projections or events: what the run must show is stated as named
/// properties next to the fixture in manifest.json (<see cref="OracleProperties"/>), so a rules change that keeps
/// every property never needs a re-recorded fixture.
/// </summary>
public static class TraceRecorder
{
    /// <summary>Loads <paramref name="tracePath"/> and its setup (same directory) and records the C# run.</summary>
    /// <param name="tracePath">The trace file.</param>
    /// <returns>The C# recording.</returns>
    public static RunRecording RecordFile(string tracePath)
    {
        var trace = OracleTrace.Load(tracePath);
        var dir = Path.GetDirectoryName(Path.GetFullPath(tracePath)) ?? ".";
        // R-BOTS-9: the trace's setup is the frozen TS run's, which has no verse rule (RunSetup.WithTsKit).
        var setup = RunSetup.Load(Path.Combine(dir, trace.SetupFile), trace.Seed).WithTsKit();
        return Record(trace, setup);
    }

    /// <summary>Steps <paramref name="setup"/> with <paramref name="trace"/>'s recorded caller commands.</summary>
    /// <param name="trace">The input trace (its commands and record count are read, nothing else).</param>
    /// <param name="setup">The createRun inputs (its seed is used as given).</param>
    /// <returns>The C# recording.</returns>
    public static RunRecording Record(OracleTrace trace, RunSetup setup)
    {
        ArgumentNullException.ThrowIfNull(trace);
        ArgumentNullException.ThrowIfNull(setup);
        var run = RunRoot.CreateRun(setup.Seed, setup.Party, setup.Dungeon, setup.Content, setup.Options);
        var ticks = new List<RecordedTick>(trace.Records.Count);
        foreach (var record in trace.Records)
        {
            IReadOnlyList<string> events = Array.Empty<string>();
            IReadOnlyList<string> commands = Array.Empty<string>();
            if (record.T > 0)
            {
                var stepped = RunRoot.StepRun(run, record.Commands);
                run = stepped.Run;
                commands = record.Commands.Select(CommandJson.ToCanonicalJson).ToList();
                events = stepped.Events.Select(e => e.ToCanonicalJson()).ToList();
            }
            var line = Projection.Line(run.State, run.Encounter.PullIndex, run.Encounter.SlackTicks);
            ticks.Add(new RecordedTick(run.State.Tick, Fnv1a.Hex(Projection.RecordHash(line)), line, commands, events));
        }
        return new RunRecording(trace.Name, ticks, RunRecording.Render(run.RunEnd.Result?.ToJs(), run.RunEnd.ResultTick));
    }
}
