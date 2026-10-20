using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Keyrun.Playtest;

/// <summary>The files a remembered run can be checked against: the summary and the plan, each with its SHA-256.</summary>
/// <param name="Summary">The summary.json path as written (as given on the command line, so repo-root relative there).</param>
/// <param name="SummarySha256">Lowercase hex SHA-256 of the summary file's bytes.</param>
/// <param name="Policy">The policy file path, or null.</param>
/// <param name="PolicySha256">Lowercase hex SHA-256 of the policy file's bytes, or null.</param>
public sealed record MemoryReceipt(string Summary, string SummarySha256, string? Policy, string? PolicySha256);

/// <summary>One remembered run: what was played, what came of it, what the run showed and what the playtester wrote.</summary>
/// <param name="Run">1-based run number (the same number as the ledger row).</param>
/// <param name="Key">The key played.</param>
/// <param name="Seed">The seed.</param>
/// <param name="Result">The outcome label ("timed +1", "untimed (wiped)").</param>
/// <param name="KeyAfter">The key the ledger holds after the run.</param>
/// <param name="Receipt">The files this entry can be checked against.</param>
/// <param name="Observed">What the run showed, in plain lines (the end-of-run recap a player would see).</param>
/// <param name="Lessons">What the playtester wrote down after reading the run, oldest first.</param>
public sealed record MemoryEntry(int Run, int Key, long Seed, string Result, int KeyAfter, MemoryReceipt Receipt, IReadOnlyList<string> Observed, IReadOnlyList<string> Lessons);

/// <summary>
/// A playtester's memory (game/playtest/players/ID.json): every run it played with a receipt, what each run showed, and
/// the lessons it wrote. This is how a playtester learns run by run: it reads this file before writing its next plan.
/// A lesson must cite a run, so every lesson can be traced to the receipt of the run it came from; <see cref="Verify"/>
/// re-hashes the receipts so an edited or missing summary is caught.
/// </summary>
public sealed partial class PlaytesterMemory
{
    private readonly List<MemoryEntry> _entries = new();

    /// <summary>Creates an empty memory for <paramref name="playerId"/>.</summary>
    /// <param name="playerId">The playtester id (a lowercase slug).</param>
    public PlaytesterMemory(string playerId)
    {
        RequireSlug(playerId);
        PlayerId = playerId;
    }

    /// <summary>The playtester id.</summary>
    public string PlayerId { get; }

    /// <summary>The runs, oldest first.</summary>
    public IReadOnlyList<MemoryEntry> Entries => _entries;

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex Slug();

    /// <summary>Throws unless <paramref name="playerId"/> is a lowercase slug (letters, digits, dashes).</summary>
    /// <param name="playerId">The id.</param>
    /// <exception cref="ArgumentException">The id is not a slug.</exception>
    public static void RequireSlug(string playerId)
    {
        if (playerId is null || !Slug().IsMatch(playerId))
        {
            throw new ArgumentException($"playtester id \"{playerId}\" must be lowercase letters, digits and dashes (e.g. fresh-01)", nameof(playerId));
        }
    }

    /// <summary>The memory file of <paramref name="playerId"/> under <paramref name="memoryDir"/>.</summary>
    /// <param name="memoryDir">The memory directory.</param>
    /// <param name="playerId">The playtester id.</param>
    /// <returns>The path.</returns>
    public static string PathFor(string memoryDir, string playerId)
    {
        ArgumentNullException.ThrowIfNull(memoryDir);
        RequireSlug(playerId);
        return Path.Combine(memoryDir, playerId + ".json").Replace('\\', '/');
    }

    /// <summary>Lowercase hex SHA-256 of the file's bytes.</summary>
    /// <param name="path">The file.</param>
    /// <returns>The hash.</returns>
    public static string Sha256Of(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    /// <summary>Appends a run; its number must be the next one.</summary>
    /// <param name="entry">The run.</param>
    /// <returns>The entry.</returns>
    /// <exception cref="ArgumentException">The run number is not the next one.</exception>
    public MemoryEntry Append(MemoryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Run != _entries.Count + 1)
        {
            throw new ArgumentException($"memory {PlayerId}: run {entry.Run} is not the next run ({_entries.Count + 1})", nameof(entry));
        }
        _entries.Add(entry);
        return entry;
    }

    /// <summary>Writes a lesson against a run (the last run when <paramref name="run"/> is null).</summary>
    /// <param name="run">The run the lesson came from, or null for the last run.</param>
    /// <param name="lesson">The lesson, one line.</param>
    /// <returns>The entry with the lesson added.</returns>
    /// <exception cref="InvalidOperationException">No run has been played yet.</exception>
    /// <exception cref="ArgumentException">The run does not exist or the lesson is blank.</exception>
    public MemoryEntry AddLesson(int? run, string lesson)
    {
        if (_entries.Count == 0)
        {
            throw new InvalidOperationException($"memory {PlayerId}: a lesson must cite a run, and no run has been played yet");
        }
        if (string.IsNullOrWhiteSpace(lesson))
        {
            throw new ArgumentException("a lesson cannot be blank", nameof(lesson));
        }
        var n = run ?? _entries.Count;
        if (n < 1 || n > _entries.Count)
        {
            throw new ArgumentException($"memory {PlayerId}: no run {n} (runs 1-{_entries.Count})", nameof(run));
        }
        var text = lesson.Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal).Trim();
        var old = _entries[n - 1];
        var updated = old with { Lessons = old.Lessons.Append(text).ToArray() };
        _entries[n - 1] = updated;
        return updated;
    }

    /// <summary>Re-hashes every receipt. Each problem names the run and says "missing" or "hash".</summary>
    /// <returns>The problems; empty when every receipt holds.</returns>
    public IReadOnlyList<string> Verify()
    {
        var problems = new List<string>();
        foreach (var e in _entries)
        {
            Check(problems, e.Run, "summary", e.Receipt.Summary, e.Receipt.SummarySha256);
            if (e.Receipt.Policy is { } policy)
            {
                Check(problems, e.Run, "policy", policy, e.Receipt.PolicySha256);
            }
        }
        return problems;
    }

    private static void Check(List<string> problems, int run, string what, string path, string? sha)
    {
        if (!File.Exists(path))
        {
            problems.Add(FormattableString.Invariant($"run {run}: {what} {path} is missing"));
        }
        else if (!string.Equals(Sha256Of(path), sha, StringComparison.Ordinal))
        {
            problems.Add(FormattableString.Invariant($"run {run}: {what} {path} hash does not match the receipt"));
        }
    }

    /// <summary>The memory as plain text, for the playtester to read before its next plan.</summary>
    /// <returns>The text.</returns>
    public string ToText()
    {
        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture, $"playtester {PlayerId}: {_entries.Count} run(s)\n");
        foreach (var e in _entries)
        {
            sb.Append(CultureInfo.InvariantCulture, $"\nrun {e.Run}: key {e.Key}, seed {e.Seed}: {e.Result}, key after {e.KeyAfter}\n");
            sb.Append(CultureInfo.InvariantCulture, $"  receipt: {e.Receipt.Summary} sha256 {e.Receipt.SummarySha256}");
            if (e.Receipt.Policy is { } p)
            {
                sb.Append(CultureInfo.InvariantCulture, $"; plan {p} sha256 {e.Receipt.PolicySha256}");
            }
            sb.Append('\n');
            foreach (var o in e.Observed)
            {
                sb.Append("  saw: ").Append(o).Append('\n');
            }
            foreach (var l in e.Lessons)
            {
                sb.Append("  lesson: ").Append(l).Append('\n');
            }
        }
        return sb.ToString();
    }

    /// <summary>Loads <paramref name="path"/>; a missing file is a playtester with no runs.</summary>
    /// <param name="path">The memory file.</param>
    /// <param name="playerId">The playtester id the file must belong to.</param>
    /// <returns>The memory.</returns>
    /// <exception cref="InvalidDataException">The file is malformed or belongs to another playtester.</exception>
    public static PlaytesterMemory Load(string path, string playerId)
    {
        var memory = new PlaytesterMemory(playerId);
        if (!File.Exists(path))
        {
            return memory;
        }
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        var owner = root.TryGetProperty("player", out var pl) ? pl.GetString() : null;
        if (!string.Equals(owner, playerId, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"memory {path}: belongs to \"{owner}\", not \"{playerId}\"");
        }
        if (!root.TryGetProperty("runs", out var runs) || runs.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException($"memory {path}: missing \"runs\" array");
        }
        foreach (var r in runs.EnumerateArray())
        {
            var receipt = r.GetProperty("receipt");
            memory.Append(new MemoryEntry(
                r.GetProperty("run").GetInt32(),
                r.GetProperty("key").GetInt32(),
                r.GetProperty("seed").GetInt64(),
                r.GetProperty("result").GetString() ?? string.Empty,
                r.GetProperty("keyAfter").GetInt32(),
                new MemoryReceipt(
                    receipt.GetProperty("summary").GetString() ?? string.Empty,
                    receipt.GetProperty("summarySha256").GetString() ?? string.Empty,
                    receipt.TryGetProperty("policy", out var pp) ? pp.GetString() : null,
                    receipt.TryGetProperty("policySha256", out var ps) ? ps.GetString() : null),
                Lines(r, "observed"),
                Lines(r, "lessons")));
        }
        return memory;
    }

    private static string[] Lines(JsonElement e, string name) =>
        e.TryGetProperty(name, out var a) ? a.EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToArray() : Array.Empty<string>();

    /// <summary>Writes the memory as indented JSON with LF line ends.</summary>
    /// <param name="path">The memory file.</param>
    public void Save(string path)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            w.WriteStartObject();
            w.WriteString("player", PlayerId);
            w.WriteStartArray("runs");
            foreach (var e in _entries)
            {
                w.WriteStartObject();
                w.WriteNumber("run", e.Run);
                w.WriteNumber("key", e.Key);
                w.WriteNumber("seed", e.Seed);
                w.WriteString("result", e.Result);
                w.WriteNumber("keyAfter", e.KeyAfter);
                w.WriteStartObject("receipt");
                w.WriteString("summary", e.Receipt.Summary);
                w.WriteString("summarySha256", e.Receipt.SummarySha256);
                w.WriteString("policy", e.Receipt.Policy);
                w.WriteString("policySha256", e.Receipt.PolicySha256);
                w.WriteEndObject();
                WriteLines(w, "observed", e.Observed);
                WriteLines(w, "lessons", e.Lessons);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        File.WriteAllText(path, Encoding.UTF8.GetString(stream.ToArray()).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n");
    }

    private static void WriteLines(Utf8JsonWriter w, string name, IReadOnlyList<string> lines)
    {
        w.WriteStartArray(name);
        foreach (var l in lines)
        {
            w.WriteStringValue(l);
        }
        w.WriteEndArray();
    }

    /// <summary>
    /// What a run showed, as the end-of-run recap a player sees: the result and where it ended, the pulls cleared and
    /// how long each took, deaths by cause, interruptible enemy casts that finished, the enemy abilities that hurt most,
    /// and the buttons a class never pressed. Facts from the run only, never advice.
    /// </summary>
    /// <param name="result">The played run.</param>
    /// <returns>The lines.</returns>
    public static IReadOnlyList<string> Observe(PlaytestResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var run = result.Run;
        var stats = result.Stats;
        var outcome = Summary.OutcomeOf(result);
        var lines = new List<string>();
        var tps = Summary.TicksPerSecond;
        var lost = run.RunEnd.Result is not null && run.Encounter.PullIndex < run.Dungeon.Pulls.Count ? PlaytestRunner.PullIdOf(run) : null;
        lines.Add(FormattableString.Invariant(
            $"result: {outcome.Label}; clock {Summary.ClockTicksOf(run) / tps}s of par {run.Encounter.ParTicks / tps}s (run {run.State.RunTimerTicks / tps}s + {Math.Max(0, run.Encounter.PenaltyTicks) / tps}s added); deaths {run.Encounter.Deaths}{(lost is null ? string.Empty : "; run ended on pull " + lost)}"));
        var cleared = stats.Pulls.Where(p => p.StartTick is not null && p.ClearTick is not null)
            .Select(p => FormattableString.Invariant($"{p.Id} {(p.ClearTick!.Value - p.StartTick!.Value) / tps}s")).ToList();
        lines.Add(cleared.Count == 0 ? "pulls cleared: none" : "pulls cleared: " + string.Join(", ", cleared));
        if (stats.Deaths.Count == 0)
        {
            lines.Add("deaths: none");
        }
        else
        {
            lines.Add(FormattableString.Invariant($"deaths: {stats.Deaths.Count}"));
            foreach (var g in stats.Deaths.GroupBy(d => (d.Cause, d.Source, d.Pull)).OrderByDescending(g => g.Count()).ThenBy(g => g.Key.Cause, StringComparer.Ordinal))
            {
                lines.Add(FormattableString.Invariant($"died to {g.Key.Cause} ({g.Key.Source}) in {g.Key.Pull}: {g.Count()}x ({string.Join(", ", g.Select(d => d.ClassId))})"));
            }
        }
        if (stats.EnemiesFought.Count > 0)
        {
            lines.Add("enemies fought: " + string.Join(", ", stats.EnemiesFought.Select(kv => $"{kv.Key} ({kv.Value})")));
        }
        foreach (var (ability, n) in stats.InterruptibleResolved)
        {
            var kicked = stats.Interrupted.TryGetValue(ability, out var k) ? k : 0;
            lines.Add(FormattableString.Invariant($"interruptible cast {ability} finished {n}x (interrupted {kicked}x)"));
        }
        foreach (var (ability, amount) in stats.DamageTakenByAbility.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Take(3))
        {
            lines.Add(FormattableString.Invariant($"damage taken from {ability}: {amount}"));
        }
        foreach (var p in result.Setup.Party.OrderBy(p => p.Id))
        {
            var unused = PolicyReader.KitIds(result.Registry.Classes[p.ClassId]).Where(id => !stats.CastsBySeat[p.Id].ContainsKey(id)).ToList();
            if (unused.Count > 0)
            {
                lines.Add($"{p.ClassId} never pressed: {string.Join(", ", unused)}");
            }
        }
        return lines;
    }
}
