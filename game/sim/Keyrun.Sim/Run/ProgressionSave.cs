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
public sealed record ProgressionLoad(Progression Progression, string? Error)
{
    /// <summary>The saved talent picks (<see cref="SavedTalents.None"/> for no save, an old save or a bad one).</summary>
    public SavedTalents Talents { get; init; } = SavedTalents.None;
}

/// <summary>
/// The talent picks kept in the progression save (BIBLE s6: the talents are the player's build, so a restart must not
/// reset them): per class id, forked ability id to picked option id. Plain ids only, because the sim never sees a
/// ClassDef; the host turns them back into a TalentPicks (see <see cref="TalentRestore"/>). Immutable; a class with
/// no picks is simply absent.
/// </summary>
public sealed record SavedTalents
{
    private static readonly IReadOnlyDictionary<string, string> NoChoices = new Dictionary<string, string>(StringComparer.Ordinal);

    private SavedTalents(IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> classes)
    {
        Classes = classes;
    }

    /// <summary>No picks for any class.</summary>
    public static SavedTalents None { get; } = new(new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal));

    /// <summary>Class id to that class's picks (forked ability id to option id); only classes with a pick.</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Classes { get; }

    /// <summary>Number of classes with at least one pick.</summary>
    public int Count => Classes.Count;

    /// <summary>The picks of <paramref name="classId"/>, empty when it has none.</summary>
    /// <param name="classId">The class id.</param>
    /// <returns>Forked ability id to option id.</returns>
    public IReadOnlyDictionary<string, string> For(string classId) =>
        Classes.TryGetValue(classId, out var c) ? c : NoChoices;

    /// <summary>These picks with <paramref name="classId"/>'s replaced by <paramref name="choices"/>; empty removes it.</summary>
    /// <param name="classId">The class id.</param>
    /// <param name="choices">Forked ability id to option id.</param>
    /// <returns>The new picks.</returns>
    public SavedTalents With(string classId, IReadOnlyDictionary<string, string> choices)
    {
        ArgumentException.ThrowIfNullOrEmpty(classId);
        ArgumentNullException.ThrowIfNull(choices);
        var next = new Dictionary<string, IReadOnlyDictionary<string, string>>(Classes, StringComparer.Ordinal);
        if (choices.Count == 0)
        {
            next.Remove(classId);
        }
        else
        {
            next[classId] = new Dictionary<string, string>(choices, StringComparer.Ordinal);
        }
        return new SavedTalents(next);
    }
}

/// <summary>
/// Persists the character's banked progression across runs (BIBLE s7 "Every run banks Ash, win or lose"; the gear the
/// end-of-run choice equips): Ash total, runs, the five gear slots, the crafting PRNG and the "since last run" delta,
/// plus the talent picks per class (BIBLE s6, <see cref="SavedTalents"/>, in the versioned optional <c>talents</c> field).
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

    /// <summary>
    /// The version of the save's optional <c>talents</c> field. It is versioned on its own so that adding it did not
    /// change <see cref="Version"/>: a save written before the field existed is still a valid version-1 save (it loads
    /// with no picks), and a save with no picks is byte-identical to one from before the field.
    /// </summary>
    public const int TalentsVersion = 1;

    /// <summary>The canonical JSON text of <paramref name="progression"/> and the talent picks.</summary>
    /// <param name="progression">The progression.</param>
    /// <param name="talents">The talent picks per class, or null for none; with none no <c>talents</c> field is written.</param>
    /// <returns>The text.</returns>
    public static string Serialize(Progression progression, SavedTalents? talents = null)
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
        var members = new List<(string, JsValue)>
        {
            ("v", JsValue.Int(Version)),
            ("ash", JsValue.Int(progression.AshTotal)),
            ("runs", JsValue.Int(progression.Runs)),
            ("prng", JsValue.Arr(JsValue.Int(p.S0), JsValue.Int(p.S1), JsValue.Int(p.S2), JsValue.Int(p.S3))),
            ("equipped", JsValue.Arr(equipped)),
            ("last", last),
        };
        if (talents is { Count: > 0 })
        {
            members.Add(("talents", TalentsJs(talents)));
        }
        return JsValue.Obj(members.ToArray()).ToCanonicalJson();
    }

    private static JsObject TalentsJs(SavedTalents talents) => JsValue.Obj(
        ("v", JsValue.Int(TalentsVersion)),
        ("classes", JsValue.Obj(talents.Classes.Select(c => new KeyValuePair<string, JsValue>(
            c.Key,
            JsValue.Obj(c.Value.Select(kv => new KeyValuePair<string, JsValue>(kv.Key, JsValue.Str(kv.Value)))))))));

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
            var (progression, talents) = Parse(text);
            return new ProgressionLoad(progression, null) { Talents = talents };
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
    /// <param name="talents">The talent picks per class to keep with it, or null for none.</param>
    /// <returns>Null on success, else why it was not saved.</returns>
    public static string? SaveFile(string path, Progression progression, SavedTalents? talents = null)
    {
        var temp = path + ".tmp";
        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            File.WriteAllText(temp, Serialize(progression, talents), Utf8NoBom);
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

    private static (Progression Progression, SavedTalents Talents) Parse(string text)
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
        var progression = new Progression
        {
            AshTotal = ash,
            Runs = (int)runs,
            Prng = new PrngState(w[0], w[1], w[2], w[3]),
            Equipped = equipped,
            LastRun = ParseDelta(root.Get("last") ?? throw new FormatException("missing \"last\"")),
        };
        return (progression, root.Get("talents") is { } t ? ParseTalents(t) : SavedTalents.None);
    }

    // The optional talents field: {"v":TalentsVersion,"classes":{classId:{abilityId:optionId}}}. Absent (a save from
    // before the field) is no picks. Anything else this build cannot read throws, so the whole save is unreadable and
    // SaveFile moves it aside instead of writing a build without the player's picks over it. Whether each id still
    // exists in the content is not checked here (the sim has no ClassDef); TalentRestore drops stale ones on load.
    private static SavedTalents ParseTalents(JsValue v)
    {
        if (v is not JsObject o)
        {
            throw new FormatException("\"talents\" must be an object");
        }
        var version = Int(o, "v");
        if (version != TalentsVersion)
        {
            throw new FormatException($"talents version {version}, this build reads {TalentsVersion}");
        }
        if (o.Get("classes") is not JsObject classes)
        {
            throw new FormatException("\"talents.classes\" must be an object");
        }
        var talents = SavedTalents.None;
        foreach (var (classId, picks) in classes.Members)
        {
            if (classId.Length == 0 || picks is not JsObject p)
            {
                throw new FormatException("talents: each class must be a non-empty id mapped to an object");
            }
            var choices = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (abilityId, option) in p.Members)
            {
                if (abilityId.Length == 0 || option is not JsString { Value.Length: > 0 } s)
                {
                    throw new FormatException($"talents: class \"{classId}\" picks must map ability ids to option ids");
                }
                choices[abilityId] = s.Value;
            }
            talents = talents.With(classId, choices);
        }
        return talents;
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
        var result = Str(o, "result") switch
        {
            "timed" => RunResult.Timed,
            "depleted" => RunResult.Depleted,
            "abandoned" => RunResult.Abandoned,
            var other => throw new FormatException($"unknown run result \"{other}\""),
        };
        var ash = Int(o, "ash");
        if (ash != Ash.ForResult(result))
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
