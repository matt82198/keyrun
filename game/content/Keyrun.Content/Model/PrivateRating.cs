using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Keyrun.Content;

/// <summary>How a run ended, for the private rating (BIBLE s8 the timer and Slack).</summary>
public enum RunClear
{
    /// <summary>Cleared within par plus the Slack left: the key upgrades +1, +2 or +3.</summary>
    Timed,

    /// <summary>Cleared, but the key was depleted (the Slack bank ran out): the key stays at its level.</summary>
    Untimed,

    /// <summary>Abandoned, or ended by a full wipe: nothing was cleared.</summary>
    NotCleared,
}

/// <summary>
/// One finished run as the private rating and the last-3-runs strip see it (BIBLE s10). Integers only: ticks, the key
/// level and a death count, so the rule is exact on every machine.
/// </summary>
/// <param name="DungeonId">The dungeon id (one best counts per dungeon).</param>
/// <param name="KeyLevel">The key level the run was played at.</param>
/// <param name="Clear">How the run ended.</param>
/// <param name="ClearTicks">The run timer at the end, in sim ticks.</param>
/// <param name="ParTicks">The dungeon's par, in sim ticks.</param>
/// <param name="Deaths">Player deaths in the run.</param>
public sealed record RatedRun(string DungeonId, int KeyLevel, RunClear Clear, int ClearTicks, int ParTicks, int Deaths)
{
    /// <summary>The run's Mythic+ score (<see cref="MythicRating.Score"/>).</summary>
    public int Score => MythicRating.Score(this);

    /// <summary>The key upgrade the run earned: 0 untimed or not cleared, else +1, +2 or +3 (<see cref="MythicRating.Upgrade"/>).</summary>
    public int Upgrade => MythicRating.Upgrade(this);
}

/// <summary>One row of the last-3-runs strip: the run and how much it moved the private rating.</summary>
/// <param name="Run">The run.</param>
/// <param name="Gain">The rating gained by this run (0 when it did not beat that dungeon's best).</param>
public sealed record StripEntry(RatedRun Run, int Gain);

/// <summary>
/// The private Mythic+ score of one run, WoW's rule (Matt 2026-10-20, BIBLE s10: "a private WoW-style Mythic+ rating";
/// s9 "almost exact to wow"). Sources: the Raider.IO FAQ base values ("+2 | 155 points", "+3 | 170 points", "If you
/// complete it faster the value will increase from here, and if you go over time the value will decrease") and
/// warcraft.wiki.gg Mythic+ Rating ("Completing the dungeon faster yields a small increase, not exceeding the jump to a
/// higher level"; slightly slower "yields points comparable to a key two levels easier"; "a lengthy delay (&gt;40%
/// beyond the timer) awards zero score"). The formula, integer floor division throughout:
/// <list type="bullet">
/// <item>Base(key) = 125 + 15 x key for key 2 and up (155, 170, ...), 0 below.</item>
/// <item>Timed: Base + 15 x min(par - clear, 40% of par) / (40% of par); a timed run past par (on Slack) scores Base.</item>
/// <item>Untimed: Base - 30 - 15 x min(clear - par, 40% of par) / (40% of par), and 0 past 40% over par.</item>
/// <item>Not cleared: 0.</item>
/// </list>
/// So a faster clear never out-scores the next key at par, and an untimed clear always scores below a timed one at the
/// same key. Per dungeon only the best run counts and the rating is the sum of the bests (<see cref="PrivateRecord"/>).
/// </summary>
public static class MythicRating
{
    /// <summary>The base at key 0 (WoW: +2 = 155 = 125 + 2 x 15).</summary>
    public const int BaseAtZero = 125;

    /// <summary>Points per key level, also the most a fast clear adds and the most overtime takes.</summary>
    public const int PerLevel = 15;

    /// <summary>What a depleted (untimed) clear loses up front: two key levels.</summary>
    public const int UntimedPenalty = 30;

    /// <summary>The timer window, in basis points of par: 40% under par is the full bonus, 40% over is zero.</summary>
    public const int WindowBp = 4000;

    /// <summary>A timed run within this share of par upgrades the key +2 (BIBLE s8 "within 80% of par").</summary>
    public const int PlusTwoBp = 8000;

    /// <summary>A timed run within this share of par upgrades the key +3 (BIBLE s8 "within 60% of par").</summary>
    public const int PlusThreeBp = 6000;

    /// <summary>The score of a key cleared exactly at par.</summary>
    /// <param name="keyLevel">The key level.</param>
    /// <returns>125 + 15 x key from key 2, else 0.</returns>
    public static int Base(int keyLevel) => keyLevel < 2 ? 0 : BaseAtZero + (PerLevel * keyLevel);

    /// <summary>The run's score (see the type's formula).</summary>
    /// <param name="run">The run.</param>
    /// <returns>The score, never negative.</returns>
    public static int Score(RatedRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.Clear == RunClear.NotCleared || run.ParTicks <= 0 || run.KeyLevel < 2)
        {
            return 0;
        }
        var window = Math.Max(1L, (long)run.ParTicks * WindowBp / 10000);
        var baseScore = Base(run.KeyLevel);
        if (run.Clear == RunClear.Timed)
        {
            var under = Math.Clamp((long)run.ParTicks - run.ClearTicks, 0L, window);
            return baseScore + (int)(under * PerLevel / window);
        }
        var over = Math.Max(0L, (long)run.ClearTicks - run.ParTicks);
        if (over > window)
        {
            return 0;
        }
        return Math.Max(0, baseScore - UntimedPenalty - (int)(over * PerLevel / window));
    }

    /// <summary>The key upgrade (BIBLE s8): +3 within 60% of par, +2 within 80%, else +1 when timed; 0 otherwise.</summary>
    /// <param name="run">The run.</param>
    /// <returns>0 to 3.</returns>
    public static int Upgrade(RatedRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (run.Clear != RunClear.Timed)
        {
            return 0;
        }
        var clear = (long)run.ClearTicks * 10000;
        return clear <= (long)run.ParTicks * PlusThreeBp ? 3 : clear <= (long)run.ParTicks * PlusTwoBp ? 2 : 1;
    }
}

/// <summary>
/// The character's private run record (BIBLE s10 "No exported score" and "Last-3-runs strip"): the best run per
/// dungeon, whose scores sum to the private Mythic+ rating, and the last three runs newest first. Immutable;
/// <see cref="Add"/> returns a new record. It is drawn only on the player's own between-runs screen, and no queue,
/// matching or other-player type takes it (PrivateRatingTests, PrivateRatingIsolationTests). The save is canonical
/// version-1 JSON; <see cref="Parse"/> never throws.
/// </summary>
public sealed class PrivateRecord
{
    /// <summary>Runs the strip keeps.</summary>
    public const int StripSize = 3;

    /// <summary>The save format version.</summary>
    public const int Version = 1;

    /// <summary>The save file name, beside the progression save.</summary>
    public const string FileName = "private-rating.json";

    private PrivateRecord(SortedDictionary<string, RatedRun> best, IReadOnlyList<StripEntry> recent)
    {
        Best = best;
        Recent = recent;
    }

    /// <summary>A character with no runs.</summary>
    public static PrivateRecord Empty { get; } = new(new SortedDictionary<string, RatedRun>(StringComparer.Ordinal), Array.Empty<StripEntry>());

    /// <summary>The best-scoring run per dungeon id.</summary>
    public IReadOnlyDictionary<string, RatedRun> Best { get; }

    /// <summary>The last runs, newest first, at most <see cref="StripSize"/>.</summary>
    public IReadOnlyList<StripEntry> Recent { get; }

    /// <summary>The private rating: the sum of each dungeon's best score.</summary>
    public int Rating => Best.Values.Sum(r => r.Score);

    /// <summary>Records a finished run: it heads the strip, and replaces its dungeon's best when it scores higher.</summary>
    /// <param name="run">The run.</param>
    /// <returns>The new record.</returns>
    public PrivateRecord Add(RatedRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var best = new SortedDictionary<string, RatedRun>((IDictionary<string, RatedRun>)Best, StringComparer.Ordinal);
        var before = Rating;
        var score = run.Score;
        if (score > 0 && (!best.TryGetValue(run.DungeonId, out var old) || score > old.Score))
        {
            best[run.DungeonId] = run;
        }
        var next = new PrivateRecord(best, Array.Empty<StripEntry>());
        var recent = new[] { new StripEntry(run, next.Rating - before) }.Concat(Recent).Take(StripSize).ToArray();
        return new PrivateRecord(best, recent);
    }

    /// <summary>One strip row: "+4  Timed +3  7:12 / 12:00  1 death  +200" (the last part is the rating gained).</summary>
    /// <param name="entry">The row.</param>
    /// <param name="ticksPerSecond">Sim ticks per second (the sim's Tick.TickHz).</param>
    /// <returns>The row text.</returns>
    public static string RowText(StripEntry entry, int ticksPerSecond)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var r = entry.Run;
        var result = r.Clear switch
        {
            RunClear.Timed => "Timed +" + r.Upgrade.ToString(CultureInfo.InvariantCulture),
            RunClear.Untimed => "Untimed",
            _ => "Not cleared",
        };
        var deaths = r.Deaths == 1 ? "1 death" : r.Deaths.ToString(CultureInfo.InvariantCulture) + " deaths";
        var text = FormattableString.Invariant(
            $"+{r.KeyLevel}  {result}  {Clock(r.ClearTicks, ticksPerSecond)} / {Clock(r.ParTicks, ticksPerSecond)}  {deaths}");
        return r.Clear == RunClear.NotCleared ? text : text + FormattableString.Invariant($"  +{entry.Gain}");
    }

    private static string Clock(int ticks, int hz)
    {
        var seconds = Math.Max(0, ticks) / Math.Max(1, hz);
        return FormattableString.Invariant($"{seconds / 60}:{seconds % 60:00}");
    }

    /// <summary>The canonical version-1 JSON (bests in dungeon id order, the strip newest first).</summary>
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
                WriteRun(w, run);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteStartArray("recent");
            foreach (var e in Recent)
            {
                w.WriteStartObject();
                WriteRun(w, e.Run);
                w.WriteNumber("gain", e.Gain);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Reads a save. Never throws: no text is <see cref="Empty"/>; a bad save is <see cref="Empty"/> plus the reason.</summary>
    /// <param name="text">The JSON text, or null when there is no save.</param>
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
                return (Empty, "unsupported private rating save version");
            }
            var best = new SortedDictionary<string, RatedRun>(StringComparer.Ordinal);
            foreach (var e in Items(root, "best"))
            {
                var run = ReadRun(e);
                if (run.Score <= 0 || !best.TryAdd(run.DungeonId, run))
                {
                    throw new FormatException("a best run that scores nothing or repeats a dungeon");
                }
            }
            var recent = new List<StripEntry>();
            foreach (var e in Items(root, "recent"))
            {
                recent.Add(new StripEntry(ReadRun(e), Int(e, "gain", 0, int.MaxValue)));
            }
            if (recent.Count > StripSize)
            {
                throw new FormatException("more than " + StripSize + " recent runs");
            }
            return (new PrivateRecord(best, recent), null);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException or KeyNotFoundException)
        {
            return (Empty, "corrupt private rating save: " + ex.Message);
        }
    }

    private static void WriteRun(Utf8JsonWriter w, RatedRun run)
    {
        w.WriteString("dungeon", run.DungeonId);
        w.WriteNumber("key", run.KeyLevel);
        w.WriteString("clear", run.Clear switch { RunClear.Timed => "timed", RunClear.Untimed => "untimed", _ => "none" });
        w.WriteNumber("clearTicks", run.ClearTicks);
        w.WriteNumber("parTicks", run.ParTicks);
        w.WriteNumber("deaths", run.Deaths);
    }

    private static JsonElement.ArrayEnumerator Items(JsonElement root, string name) =>
        root.TryGetProperty(name, out var a) && a.ValueKind == JsonValueKind.Array
            ? a.EnumerateArray()
            : throw new FormatException("missing " + name);

    private static RatedRun ReadRun(JsonElement e)
    {
        var dungeon = e.GetProperty("dungeon").GetString();
        if (string.IsNullOrEmpty(dungeon))
        {
            throw new FormatException("a run with no dungeon");
        }
        var clear = e.GetProperty("clear").GetString() switch
        {
            "timed" => RunClear.Timed,
            "untimed" => RunClear.Untimed,
            "none" => RunClear.NotCleared,
            var other => throw new FormatException("unknown clear " + other),
        };
        return new RatedRun(dungeon, Int(e, "key", 1, 99), clear, Int(e, "clearTicks", 0, int.MaxValue),
            Int(e, "parTicks", 1, int.MaxValue), Int(e, "deaths", 0, int.MaxValue));
    }

    private static int Int(JsonElement e, string name, int min, int max)
    {
        var value = e.GetProperty(name).GetInt32();
        return value < min || value > max ? throw new FormatException(name + " out of range") : value;
    }
}
