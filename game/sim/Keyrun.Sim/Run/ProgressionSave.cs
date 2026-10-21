using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;

namespace Keyrun.Sim.Runs;

/// <summary>A loaded progression and, when the save could not be used, why.</summary>
/// <param name="Progression">The progression: the saved one, or a fresh character when there was none or it was bad.</param>
/// <param name="Error">Why the save was not used, or null when it loaded (or there simply was no save).</param>
public sealed record ProgressionLoad(Progression Progression, string? Error);

/// <summary>
/// Persists the character's banked progression across runs (BIBLE s7 "Every run banks Ash, win or lose"; the gear the
/// end-of-run choice equips): Ash total, runs, the five gear slots, the crafting PRNG, the "since last run" delta,
/// the talent build picked on the screen (BIBLE s6, an optional "talents" member: a save without it has no picks), and
/// the banked run history (f-275d47c9, an optional "history" member of key, result and ascended per run, from which
/// <see cref="Ascension.Marks"/> counts: a save without it has none).
/// The codec is pure and deterministic (canonical JSON, slots in <see cref="Rewards.GearSlots"/> order), so the same
/// progression always writes the same text. Loading never throws: a missing save is a fresh character, and a corrupt
/// one is a fresh character plus the reason, so a bad file can never block or crash a run.
/// </summary>
public static class ProgressionSave
{
    /// <summary>The save format version this build reads and writes.</summary>
    public const int Version = 1;

    /// <summary>The save file's name inside the host's user data directory.</summary>
    public const string FileName = "progression.json";

    private static readonly UTF8Encoding Utf8NoBom = new(false);

    /// <summary>The canonical JSON text of <paramref name="progression"/>.</summary>
    /// <param name="progression">The progression.</param>
    /// <returns>The text.</returns>
    public static string Serialize(Progression progression)
    {
        ArgumentNullException.ThrowIfNull(progression);
        var p = progression.Prng;
        var equipped = Rewards.GearSlots
            .Where(progression.Equipped.ContainsKey)
            .Select(slot => (JsValue)ItemJs(progression.Equipped[slot]));
        JsValue last = progression.LastRun is { } d
            ? JsValue.Obj(
                ("result", JsValue.Str(d.Result.ToJs())),
                ("ash", JsValue.Int(d.AshGained)),
                ("taken", d.Taken is { } t ? ItemJs(t) : JsValue.Null),
                ("was", d.PreviousTier is { } w ? JsValue.Int(w) : JsValue.Null))
            : JsValue.Null;
        return JsValue.Obj(
            ("v", JsValue.Int(Version)),
            ("ash", JsValue.Int(progression.AshTotal)),
            ("runs", JsValue.Int(progression.Runs)),
            ("prng", JsValue.Arr(JsValue.Int(p.S0), JsValue.Int(p.S1), JsValue.Int(p.S2), JsValue.Int(p.S3))),
            ("equipped", JsValue.Arr(equipped)),
            ("last", last),
            ("talents", TalentsJs(progression.Talents)),
            ("history", JsValue.Arr(progression.History.Select(r => (JsValue)RunRecordJs(r)))),
            ("ascensionCards", JsValue.Int(progression.AscensionCardMarks))).ToCanonicalJson();
    }

    // One banked run (f-275d47c9): the key, the result and whether the character was ascended, so marks are countable.
    private static JsObject RunRecordJs(RunRecord r) => JsValue.Obj(
        ("key", JsValue.Int(r.KeyLevel)),
        ("result", JsValue.Str(r.Result.ToJs())),
        ("ascended", JsValue.Bool(r.Ascended)));

    /// <summary>
    /// Loads a progression from save text. Null text (no save yet) is a fresh character with no error; text that is
    /// not a valid version-<see cref="Version"/> save is a fresh character and the reason.
    /// </summary>
    /// <param name="text">The save text, or null when there is none.</param>
    /// <param name="seed">The fresh character's crafting seed (<see cref="Progression.Start"/>).</param>
    /// <returns>The load.</returns>
    public static ProgressionLoad Load(string? text, long seed)
    {
        if (text is null)
        {
            return new ProgressionLoad(Progression.Start(seed), null);
        }
        try
        {
            return new ProgressionLoad(Parse(text), null);
        }
        catch (Exception e)
        {
            return new ProgressionLoad(Progression.Start(seed), $"progression save ignored: {e.Message}");
        }
    }

    /// <summary>Loads the save at <paramref name="path"/> (<see cref="Load"/>); a missing file is no save. Never throws.</summary>
    /// <param name="path">The save file.</param>
    /// <param name="seed">The fresh character's crafting seed.</param>
    /// <returns>The load.</returns>
    public static ProgressionLoad LoadFile(string path, long seed)
    {
        string? text;
        try
        {
            text = File.Exists(path) ? File.ReadAllText(path, Utf8NoBom) : null;
        }
        catch (Exception e)
        {
            return new ProgressionLoad(Progression.Start(seed), $"progression save unreadable: {e.Message}");
        }
        return Load(text, seed);
    }

    /// <summary>
    /// Writes <paramref name="progression"/> to <paramref name="path"/>, creating its directory. The text goes to a
    /// sibling temp file first and then replaces the save, so a crash mid-write leaves the previous save intact.
    /// A save that is there but this build cannot read (a newer version, corrupt text, a read error) is never written
    /// over: it is first moved aside to <see cref="UnreadableBackupPath"/>, so the fresh character a failed load fell
    /// back to cannot destroy the player's real one. A readable save is replaced in place, as always. Never throws.
    /// </summary>
    /// <param name="path">The save file.</param>
    /// <param name="progression">The progression.</param>
    /// <returns>Null on success, else why it was not saved.</returns>
    public static string? SaveFile(string path, Progression progression)
    {
        var temp = path + ".tmp";
        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(temp, Serialize(progression), Utf8NoBom);
            PreserveIfUnreadable(path);
            File.Move(temp, path, overwrite: true);
            return null;
        }
        catch (Exception e)
        {
            try
            {
                File.Delete(temp);
            }
            catch (Exception)
            {
                // The temp file is best-effort cleanup; the save error below is the one that matters.
            }
            return $"progression not saved: {e.Message}";
        }
    }

    /// <summary>
    /// The first free sibling backup name for an unreadable save at <paramref name="path"/>:
    /// <c>progression.json.unreadable-1</c>, then <c>-2</c> and so on, so an earlier backup is never overwritten.
    /// </summary>
    /// <param name="path">The save file.</param>
    /// <returns>A path that does not exist yet.</returns>
    public static string UnreadableBackupPath(string path)
    {
        for (var n = 1; ; n++)
        {
            var candidate = FormattableString.Invariant($"{path}.unreadable-{n}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    // Moves an existing save this build cannot load out of the way before it is replaced. A missing or readable save
    // is left alone. A failed read counts as unreadable; if the move itself fails the exception aborts the save, which
    // leaves the original file where it was.
    private static void PreserveIfUnreadable(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }
        string text;
        try
        {
            text = File.ReadAllText(path, Utf8NoBom);
        }
        catch (Exception)
        {
            File.Move(path, UnreadableBackupPath(path), overwrite: false);
            return;
        }
        if (Load(text, seed: 1).Error is not null)
        {
            File.Move(path, UnreadableBackupPath(path), overwrite: false);
        }
    }

    private static JsObject ItemJs(Item item) => JsValue.Obj(
        ("id", JsValue.Str(item.Id)),
        ("slot", JsValue.Str(item.Slot)),
        ("tier", JsValue.Int(item.Tier)),
        ("charges", JsValue.Int(item.Charges)),
        ("facet", JsValue.Obj(
            ("id", JsValue.Str(item.Facet.Id)),
            ("text", JsValue.Str(item.Facet.Text)),
            ("ability", JsValue.Str(item.Facet.AbilityId)))));

    private static Progression Parse(string text)
    {
        if (JsValue.Parse(text) is not JsObject root)
        {
            throw new FormatException("not a JSON object");
        }
        var version = Int(root, "v");
        if (version != Version)
        {
            throw new FormatException($"version {version}, this build reads {Version}");
        }
        var ash = Int(root, "ash");
        var runs = Int(root, "runs");
        if (ash < 0 || runs < 0 || runs > int.MaxValue)
        {
            throw new FormatException("ash and runs must be non-negative");
        }
        var words = Arr(root, "prng");
        if (words.Count != 4)
        {
            throw new FormatException("prng must have four words");
        }
        var w = words.Select(x => x is JsInt i && i.Value >= 0 && i.Value <= uint.MaxValue
            ? (uint)i.Value
            : throw new FormatException("prng words must be uint32")).ToArray();
        if (w.All(x => x == 0))
        {
            throw new FormatException("prng state is all zero");
        }
        var equipped = new Dictionary<string, Item>(StringComparer.Ordinal);
        foreach (var e in Arr(root, "equipped"))
        {
            var item = ParseItem(e);
            if (!equipped.TryAdd(item.Slot, item))
            {
                throw new FormatException($"slot \"{item.Slot}\" equipped twice");
            }
        }
        var history = ParseHistory(root.Get("history"));
        if (history.Count > runs)
        {
            throw new FormatException(FormattableString.Invariant($"history has {history.Count} runs but only {runs} were banked"));
        }
        return new Progression
        {
            AshTotal = ash,
            Runs = (int)runs,
            Prng = new PrngState(w[0], w[1], w[2], w[3]),
            Equipped = equipped,
            LastRun = ParseDelta(root.Get("last") ?? throw new FormatException("missing \"last\"")),
            History = history,
            AscensionCardMarks = ParseAscensionCards(root.Get("ascensionCards")),
        }.WithTalents(ParseTalents(root.Get("talents")));
    }

    // A save written before Ascension cards were recorded has no member: that character holds no card marks yet.
    private static int ParseAscensionCards(JsValue? v)
    {
        if (v is null)
        {
            return 0;
        }
        if (v is not JsInt i || i.Value < 0 || i.Value > int.MaxValue)
        {
            throw new FormatException("\"ascensionCards\" must be a non-negative integer");
        }
        return (int)i.Value;
    }

    // A save written before runs were recorded has no member: its runs simply have no history (and earned no marks).
    private static IReadOnlyList<RunRecord> ParseHistory(JsValue? v)
    {
        if (v is null)
        {
            return Array.Empty<RunRecord>();
        }
        if (v is not JsArray a)
        {
            throw new FormatException("\"history\" must be an array");
        }
        return a.Items.Select(e =>
        {
            if (e is not JsObject o)
            {
                throw new FormatException("a history entry must be an object");
            }
            var key = Int(o, "key");
            if (key < 1 || key > int.MaxValue)
            {
                throw new FormatException(FormattableString.Invariant($"history key {key} must be 1 or more"));
            }
            var ascended = o.Get("ascended") is JsBool b ? b.Value : throw new FormatException("\"ascended\" must be a boolean");
            return new RunRecord((int)key, ParseResult(Str(o, "result")), ascended);
        }).ToList();
    }

    private static RunResult ParseResult(string result) => result switch
    {
        "timed" => RunResult.Timed,
        "depleted" => RunResult.Depleted,
        "abandoned" => RunResult.Abandoned,
        var other => throw new FormatException($"unknown run result \"{other}\""),
    };

    // The screen's talent build (BIBLE s6), ability id to option id in ordinal key order.
    private static JsObject TalentsJs(IReadOnlyDictionary<string, string> talents) => JsValue.Obj(
        talents.OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new KeyValuePair<string, JsValue>(kv.Key, JsValue.Str(kv.Value))));

    // A save written before talents were saved has no member: that character simply has no picks yet.
    private static IEnumerable<KeyValuePair<string, string>> ParseTalents(JsValue? v)
    {
        if (v is null)
        {
            return Array.Empty<KeyValuePair<string, string>>();
        }
        if (v is not JsObject o)
        {
            throw new FormatException("\"talents\" must be an object");
        }
        return o.Members.Select(m => new KeyValuePair<string, string>(
            m.Key, m.Value is JsString { Value.Length: > 0 } s ? s.Value : throw new FormatException($"talent \"{m.Key}\" must be a non-empty option id"))).ToList();
    }

    private static RunDelta? ParseDelta(JsValue v)
    {
        if (v is JsNull)
        {
            return null;
        }
        if (v is not JsObject o)
        {
            throw new FormatException("\"last\" must be an object or null");
        }
        var result = ParseResult(Str(o, "result"));
        var ash = Int(o, "ash");
        // A save written before D45 recorded 10 Ash for an abandoned run; it still loads as written.
        if (ash != Ash.ForResult(result) && !(result == RunResult.Abandoned && ash == Ash.LegacyAbandoned))
        {
            throw new FormatException("last run Ash does not match its result");
        }
        var taken = o.Get("taken") is { } t and not JsNull ? ParseItem(t) : null;
        int? was = o.Get("was") is JsInt i ? Tier(i.Value) : null;
        return new RunDelta(result, (int)ash, taken, was);
    }

    private static Item ParseItem(JsValue v)
    {
        if (v is not JsObject o)
        {
            throw new FormatException("an item must be an object");
        }
        var slot = Str(o, "slot");
        if (!Rewards.GearSlots.Contains(slot, StringComparer.Ordinal))
        {
            throw new FormatException($"unknown gear slot \"{slot}\"");
        }
        var charges = Int(o, "charges");
        if (charges < 0 || charges > Rewards.StartingCharges)
        {
            throw new FormatException($"charges {charges} outside 0..{Rewards.StartingCharges}");
        }
        if (o.Get("facet") is not JsObject f)
        {
            throw new FormatException("an item needs a facet");
        }
        var facet = new Facet(Str(f, "id"), Str(f, "text"), Str(f, "ability"));
        return new Item(Str(o, "id"), slot, Tier(Int(o, "tier")), facet, (int)charges);
    }

    private static int Tier(long tier) => tier >= 1 && tier <= Progression.MaxTier
        ? (int)tier
        : throw new FormatException($"tier {tier} outside 1..{Progression.MaxTier}");

    private static long Int(JsObject o, string key) =>
        o.Get(key) is JsInt i ? i.Value : throw new FormatException($"\"{key}\" must be an integer");

    private static string Str(JsObject o, string key) =>
        o.Get(key) is JsString s ? s.Value : throw new FormatException($"\"{key}\" must be a string");

    private static IReadOnlyList<JsValue> Arr(JsObject o, string key) =>
        o.Get(key) is JsArray a ? a.Items : throw new FormatException($"\"{key}\" must be an array");
}
