using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Keyrun.Content;

/// <summary>One timed clear the private rating counts: the dungeon, the key level, the clear time and the par, in ticks.</summary>
/// <param name="DungeonId">The dungeon id (one best counts per dungeon).</param>
/// <param name="KeyLevel">The key level the run was played at.</param>
/// <param name="ClearTicks">The tick the run ended on (the run timer starts at tick 0).</param>
/// <param name="ParTicks">The dungeon's par for that run, in ticks.</param>
public sealed record TimedRun(string DungeonId, int KeyLevel, int ClearTicks, int ParTicks);

/// <summary>
/// The private WoW-style Mythic+ score of one timed run (Matt 2026-10-20, f-a3230c59: "private WoW-style M+ rating shown
/// only on the player's own screen, never to others and never used to filter groups"). Integer floor division only:
/// a key cleared at par scores its level base, 125 + 15 x key (WoW's +2 = 155, +3 = 170); clearing up to 40% under par
/// adds up to one more level's 15 points, so a faster clear never beats the next key at par. A timed run past par (on
/// Slack) scores the base. Depleted and abandoned runs score nothing: only timed runs count.
/// </summary>
public static class PrivateRating
{
    /// <summary>The base at key 0 (WoW: +2 = 155 = 125 + 2 x 15).</summary>
    public const int BaseAtZero = 125;

    /// <summary>Points per key level, and the most a fast clear adds.</summary>
    public const int PerLevel = 15;

    /// <summary>The speed window, in percent of par: a clear this far under par earns the full bonus.</summary>
    public const int WindowPercent = 40;

    /// <summary>The score of a key cleared exactly at par: 125 + 15 x key from key 2, else 0.</summary>
    /// <param name="keyLevel">The key level.</param>
    /// <returns>The base score.</returns>
    public static int Base(int keyLevel) => keyLevel < 2 ? 0 : BaseAtZero + (PerLevel * keyLevel);

    /// <summary>The run's score (see the type summary).</summary>
    /// <param name="run">The timed run.</param>
    /// <returns>The score, never negative.</returns>
    public static int Score(TimedRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.KeyLevel < 2 || run.ParTicks <= 0)
        {
            return 0;
        }
        var window = Math.Max(1L, (long)run.ParTicks * WindowPercent / 100);
        var under = Math.Clamp((long)run.ParTicks - run.ClearTicks, 0L, window);
        return Base(run.KeyLevel) + (int)(under * PerLevel / window);
    }

    /// <summary>
    /// The timed run a finished run counts as, or null when it was not timed. Read-only: the host passes the run's own
    /// result fields (Screen: <c>RunEnd.Result == RunResult.Timed</c>, <c>RunEnd.ResultTick</c>, <c>Dungeon.Id</c>,
    /// <c>Encounter.KeyLevel</c>, <c>Encounter.ParTicks</c>); the key level is never changed here.
    /// </summary>
    /// <param name="timed">True when the run ended timed (completed with Slack left).</param>
    /// <param name="resultTick">The tick it ended, or null while it runs.</param>
    /// <param name="dungeonId">The dungeon id.</param>
    /// <param name="keyLevel">The key level the run was played at.</param>
    /// <param name="parTicks">The run's par.</param>
    /// <returns>The timed run, or null.</returns>
    public static TimedRun? FromResult(bool timed, int? resultTick, string dungeonId, int keyLevel, int parTicks) =>
        timed && resultTick is { } tick ? new TimedRun(dungeonId, keyLevel, tick, parTicks) : null;
}

/// <summary>
/// The player's private rating record: the best timed run per dungeon, whose scores sum to the rating. Immutable;
/// <see cref="Add"/> returns a new record. It is kept only in <see cref="FileName"/> in the local profile directory and
/// drawn only on the player's own between-runs Screen (<see cref="ScreenText"/>); the one log line about it
/// (<see cref="LogLine"/>) carries no number. No queue, backfill, run or trace type takes it
/// (PrivateRatingTests, Keyrun.Sim.Tests PrivateRatingIsolationTests). The file is canonical version-1 JSON;
/// <see cref="Parse"/> and <see cref="LoadFile"/> never throw.
/// </summary>
public sealed class PrivateRecord
{
    /// <summary>The save format version.</summary>
    public const int Version = 1;

    /// <summary>The file name, beside the progression save in the local profile directory.</summary>
    public const string FileName = "private-rating.json";

    private static readonly UTF8Encoding Utf8NoBom = new(false);

    private PrivateRecord(SortedDictionary<string, TimedRun> best)
    {
        Best = best;
    }

    /// <summary>A player with no timed runs.</summary>
    public static PrivateRecord Empty { get; } = new(new SortedDictionary<string, TimedRun>(StringComparer.Ordinal));

    /// <summary>The best-scoring timed run per dungeon id.</summary>
    public IReadOnlyDictionary<string, TimedRun> Best { get; }

    /// <summary>The private rating: the sum of each dungeon's best score.</summary>
    public int Rating => Best.Values.Sum(PrivateRating.Score);

    /// <summary>Records a timed run: it replaces its dungeon's best when it scores strictly higher.</summary>
    /// <param name="run">The timed run.</param>
    /// <returns>The new record (this one when nothing improved).</returns>
    public PrivateRecord Add(TimedRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var score = PrivateRating.Score(run);
        if (score <= 0 || (Best.TryGetValue(run.DungeonId, out var old) && PrivateRating.Score(old) >= score))
        {
            return this;
        }
        var best = new SortedDictionary<string, TimedRun>((IDictionary<string, TimedRun>)Best, StringComparer.Ordinal)
        {
            [run.DungeonId] = run,
        };
        return new PrivateRecord(best);
    }

    /// <summary>The text the player's own Screen draws.</summary>
    /// <returns>"M+ rating N (only you see this)".</returns>
    public string ScreenText() => FormattableString.Invariant($"M+ rating {Rating} (only you see this)");

    /// <summary>The proof line the Screen prints about the rating: whether one is shown, never its value.</summary>
    /// <returns>"SCREEN rating private=1 shown=0|1".</returns>
    public string LogLine() => "SCREEN rating private=1 shown=" + (Best.Count > 0 ? "1" : "0");

    /// <summary>The canonical version-1 JSON (bests in dungeon id order).</summary>
    /// <returns>The JSON text.</returns>
    public string ToJson()
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream))
        {
            w.WriteStartObject();
            w.WriteNumber("version", Version);
            w.WriteStartArray("best");
            foreach (var run in Best.Values)
            {
                w.WriteStartObject();
                w.WriteString("dungeon", run.DungeonId);
                w.WriteNumber("key", run.KeyLevel);
                w.WriteNumber("clearTicks", run.ClearTicks);
                w.WriteNumber("parTicks", run.ParTicks);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Reads the JSON. Never throws: no text is <see cref="Empty"/>; a bad file is <see cref="Empty"/> plus the reason.</summary>
    /// <param name="text">The JSON text, or null when there is no file.</param>
    /// <returns>The record and the error, if any.</returns>
    public static (PrivateRecord Record, string? Error) Parse(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return (Empty, null);
        }
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("version", out var v) ||
                v.ValueKind != JsonValueKind.Number || v.GetInt32() != Version)
            {
                return (Empty, "unsupported private rating version");
            }
            if (!root.TryGetProperty("best", out var arr) || arr.ValueKind != JsonValueKind.Array)
            {
                throw new FormatException("missing best");
            }
            var best = new SortedDictionary<string, TimedRun>(StringComparer.Ordinal);
            foreach (var e in arr.EnumerateArray())
            {
                var dungeon = e.GetProperty("dungeon").GetString();
                if (string.IsNullOrEmpty(dungeon))
                {
                    throw new FormatException("a run with no dungeon");
                }
                var run = new TimedRun(dungeon, Int(e, "key", 2, 99), Int(e, "clearTicks", 0, int.MaxValue), Int(e, "parTicks", 1, int.MaxValue));
                if (!best.TryAdd(dungeon, run))
                {
                    throw new FormatException("a dungeon twice");
                }
            }
            return (new PrivateRecord(best), null);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException or KeyNotFoundException)
        {
            return (Empty, "corrupt private rating: " + ex.Message);
        }
    }

    /// <summary>Reads <paramref name="path"/>. Never throws: a missing file is <see cref="Empty"/> with no error.</summary>
    /// <param name="path">The file.</param>
    /// <returns>The record and the error, if any.</returns>
    public static (PrivateRecord Record, string? Error) LoadFile(string path)
    {
        try
        {
            return Parse(File.Exists(path) ? File.ReadAllText(path, Utf8NoBom) : null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return (Empty, "private rating unreadable: " + e.Message);
        }
    }

    /// <summary>Writes <paramref name="record"/> to <paramref name="path"/> via a sibling temp file. Never throws.</summary>
    /// <param name="path">The file.</param>
    /// <param name="record">The record.</param>
    /// <returns>Null on success, else why it was not saved.</returns>
    public static string? SaveFile(string path, PrivateRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var temp = path + ".tmp";
        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(temp, record.ToJson(), Utf8NoBom);
            File.Move(temp, path, overwrite: true);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            try
            {
                File.Delete(temp);
            }
            catch (Exception)
            {
                // Best-effort cleanup; the save error below is the one that matters.
            }
            return "private rating not saved: " + e.Message;
        }
    }

    private static int Int(JsonElement e, string name, int min, int max)
    {
        var value = e.GetProperty(name).GetInt32();
        return value < min || value > max ? throw new FormatException(name + " out of range") : value;
    }
}
