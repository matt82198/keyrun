using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Keyrun.Sim.Runs;

namespace Keyrun.Playtest;

/// <summary>Who played and where the files went, for the summary header.</summary>
/// <param name="PlayerId">The playtester id, or null for an anonymous run.</param>
/// <param name="PolicyPath">The policy file as given, or null.</param>
/// <param name="LogPath">The full log file as written.</param>
/// <param name="KeyBefore">The key played.</param>
/// <param name="KeyAfter">The key the ledger holds after the run.</param>
/// <param name="RunNumber">The playtester's run number, or null.</param>
public sealed record SummaryHeader(string? PlayerId, string? PolicyPath, string LogPath, int KeyBefore, int KeyAfter, int? RunNumber);

/// <summary>Writes summary.json (tools/Keyrun.Playtest/GUIDE.md "Reading what happened" explains the fields).</summary>
public static class Summary
{
    /// <summary>Ticks per second (20 Hz).</summary>
    public const int TicksPerSecond = 20;

    /// <summary>The outcome of a played run.</summary>
    /// <param name="result">The run.</param>
    /// <returns>The outcome.</returns>
    public static KeyOutcome OutcomeOf(PlaytestResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var run = result.Run;
        var ended = run.RunEnd.Result is not null;
        var cleared = run.Encounter.PullIndex >= run.Dungeon.Pulls.Count;
        return KeyOutcome.Of(ended, cleared, run.Encounter.Depleted, run.State.RunTimerTicks, run.Encounter.ParTicks);
    }

    /// <summary>The summary JSON text.</summary>
    /// <param name="result">The run.</param>
    /// <param name="options">What was played.</param>
    /// <param name="header">Who and where.</param>
    /// <returns>Indented JSON, LF line ends.</returns>
    public static string ToJson(PlaytestResult result, PlaytestOptions options, SummaryHeader header)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(header);
        var run = result.Run;
        var stats = result.Stats;
        var outcome = OutcomeOf(result);
        var classOf = result.Setup.Party.ToDictionary(p => p.Id, p => p.ClassId);
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteString("player", header.PlayerId);
            if (header.RunNumber is { } n)
            {
                w.WriteNumber("run", n);
            }
            else
            {
                w.WriteNull("run");
            }
            w.WriteNumber("key", options.Key);
            w.WriteNumber("seed", options.Seed);
            w.WriteString("policy", header.PolicyPath);
            w.WriteString("policyReason", options.Policy?.Reason);
            w.WriteString("result", outcome.Label);
            w.WriteBoolean("timed", outcome.Timed);
            w.WriteNumber("tier", outcome.Tier);
            w.WriteNumber("keyBefore", header.KeyBefore);
            w.WriteNumber("keyAfter", header.KeyAfter);
            w.WriteNumber("clearTicks", run.State.RunTimerTicks);
            w.WriteNumber("clearSeconds", run.State.RunTimerTicks / TicksPerSecond);
            w.WriteNumber("parTicks", run.Encounter.ParTicks);
            w.WriteNumber("parSeconds", run.Encounter.ParTicks / TicksPerSecond);
            w.WriteNumber("parPercent", (int)((long)run.State.RunTimerTicks * 100 / Math.Max(1, run.Encounter.ParTicks)));
            w.WriteNumber("slackLeftTicks", run.Encounter.SlackTicks);
            w.WriteNumber("slackLeftSeconds", run.Encounter.SlackTicks / TicksPerSecond);
            w.WriteBoolean("depleted", run.Encounter.Depleted);
            var lost = run.RunEnd.Result is not null && run.Encounter.PullIndex < run.Dungeon.Pulls.Count ? PlaytestRunner.PullIdOf(run) : null;
            w.WriteString("lostOnPull", lost);

            w.WriteStartArray("pulls");
            foreach (var p in stats.Pulls)
            {
                w.WriteStartObject();
                w.WriteString("id", p.Id);
                WriteOptional(w, "startTick", p.StartTick);
                WriteOptional(w, "clearTick", p.ClearTick);
                WriteOptional(w, "seconds", p.StartTick is { } s && p.ClearTick is { } c ? (c - s) / TicksPerSecond : null);
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WriteStartArray("deaths");
            foreach (var d in stats.Deaths)
            {
                w.WriteStartObject();
                w.WriteNumber("tick", d.Tick);
                w.WriteNumber("second", d.Tick / TicksPerSecond);
                w.WriteString("player", d.ClassId);
                w.WriteString("pull", d.Pull);
                w.WriteString("cause", d.Cause);
                w.WriteString("source", d.Source);
                w.WriteEndObject();
            }
            w.WriteEndArray();

            WriteCounts(w, "interruptibleCastsResolved", stats.InterruptibleResolved);
            WriteCounts(w, "castsInterrupted", stats.Interrupted);

            w.WriteStartArray("kicks");
            foreach (var (seat, cls) in classOf.OrderBy(kv => kv.Key))
            {
                w.WriteStartObject();
                w.WriteString("player", cls);
                w.WriteNumber("presses", stats.KickPresses[seat]);
                w.WriteNumber("landed", stats.KicksLanded[seat]);
                w.WriteNumber("late", stats.LateKicks[seat]);
                w.WriteNumber("dodgeTicks", stats.Dodges[seat]);
                w.WriteEndObject();
            }
            w.WriteEndArray();

            w.WriteStartObject("enemiesFought");
            foreach (var (id, name) in stats.EnemiesFought)
            {
                w.WriteString(id, name);
            }
            w.WriteEndObject();

            w.WriteStartObject("damageTakenByEnemyAbility");
            foreach (var (ability, amount) in stats.DamageTakenByAbility.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal))
            {
                w.WriteNumber(ability, amount);
            }
            w.WriteEndObject();

            w.WriteStartObject("buttonsPressed");
            foreach (var (seat, cls) in classOf.OrderBy(kv => kv.Key))
            {
                w.WriteStartObject(cls);
                foreach (var (ability, count) in stats.CastsBySeat[seat])
                {
                    w.WriteNumber(ability, count);
                }
                w.WriteEndObject();
            }
            w.WriteEndObject();

            w.WriteStartObject("unusedButtons");
            foreach (var (seat, cls) in classOf.OrderBy(kv => kv.Key))
            {
                w.WriteStartArray(cls);
                foreach (var id in PolicyReader.KitIds(result.Registry.Classes[cls]).Where(id => !stats.CastsBySeat[seat].ContainsKey(id)))
                {
                    w.WriteStringValue(id);
                }
                w.WriteEndArray();
            }
            w.WriteEndObject();

            WriteCounts(w, "notable", stats.Notable);
            w.WriteString("log", header.LogPath);
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray()).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
    }

    private static void WriteOptional(Utf8JsonWriter w, string name, int? value)
    {
        if (value is { } v)
        {
            w.WriteNumber(name, v);
        }
        else
        {
            w.WriteNull(name);
        }
    }

    private static void WriteCounts(Utf8JsonWriter w, string name, IEnumerable<KeyValuePair<string, int>> counts)
    {
        w.WriteStartObject(name);
        foreach (var (k, v) in counts)
        {
            w.WriteNumber(k, v);
        }
        w.WriteEndObject();
    }
}
