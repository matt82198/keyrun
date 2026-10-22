using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;

namespace Keyrun.Content.Tests;

/// <summary>
/// Copies every embedded content file into memory so a test can mutate one file (as JSON
/// nodes or raw text) and load the result through the real loader. The per-kind layout and forces directories
/// (R-LOADER-1) are edited whole through <see cref="EditLayout"/> and <see cref="EditForces"/>.
/// </summary>
internal sealed class MutableSource
{
    /// <summary>The per-kind layout directory of the shipped dungeon (R-LOADER-1).</summary>
    public const string LayoutDir = "layout/hollow-vault/";

    /// <summary>The layout head file: dungeon, start, route, formation.</summary>
    public const string LayoutHead = LayoutDir + "dungeon.json";

    /// <summary>The forces bar and route file.</summary>
    public const string ForcesHead = "forces/hollow-vault.json";

    /// <summary>The forces values directory, one value per file.</summary>
    public const string ForcesValuesDir = "forces/values/";

    private static readonly string[] LayoutHeadKeys = { "dungeon", "start", "route", "formation" };

    private static readonly (string Key, string Dir)[] LayoutItemDirs =
    {
        ("boxes", LayoutDir + "rooms/"), ("packs", LayoutDir + "packs/"), ("patrols", LayoutDir + "patrols/"),
    };

    private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);

    public MutableSource()
    {
        foreach (string path in EmbeddedContentSource.Instance.Paths)
            _files[path] = EmbeddedContentSource.Instance.Read(path);
    }

    public MutableSource Edit(string path, Action<JsonNode> mutate)
    {
        JsonNode node = JsonNode.Parse(_files[path]) ?? throw new InvalidOperationException(path);
        mutate(node);
        _files[path] = Encoding.UTF8.GetBytes(node.ToJsonString());
        return this;
    }

    public MutableSource EditText(string path, Func<string, string> mutate)
    {
        string before = Encoding.UTF8.GetString(_files[path]);
        string after = mutate(before);
        if (after == before) throw new InvalidOperationException("text mutation of " + path + " changed nothing");
        _files[path] = Encoding.UTF8.GetBytes(after);
        return this;
    }

    /// <summary>
    /// Edits the layout as the one JSON object the per-kind files assemble into (dungeon, start, route, formation, boxes,
    /// packs, patrols), then writes it back as per-kind files: every non-item key (including any key a test adds) to
    /// dungeon.json, and one file per box, pack and patrol, named by position so the loader's ordinal order is the array
    /// order. A removed or non-array boxes/packs/patrols key goes to dungeon.json as-is, where the strict loader sees it.
    /// </summary>
    public MutableSource EditLayout(Action<JsonNode> mutate)
    {
        JsonObject head = Parse(LayoutHead).AsObject();
        var layout = new JsonObject();
        foreach (string key in LayoutHeadKeys)
            if (head.ContainsKey(key)) layout[key] = head[key]?.DeepClone();
        foreach (var (key, dir) in LayoutItemDirs)
            layout[key] = new JsonArray(PathsUnder(dir).Select(p => (JsonNode?)Parse(p)).ToArray());
        foreach (var pair in head)
            if (!layout.ContainsKey(pair.Key)) layout[pair.Key] = pair.Value?.DeepClone();
        mutate(layout);

        foreach (string path in PathsUnder(LayoutDir)) _files.Remove(path);
        var newHead = new JsonObject();
        foreach (var pair in layout.ToList())
            if (!(LayoutItemDirs.Any(d => d.Key == pair.Key) && pair.Value is JsonArray)) newHead[pair.Key] = pair.Value?.DeepClone();
        Put(LayoutHead, newHead);
        foreach (var (key, dir) in LayoutItemDirs)
            if (layout[key] is JsonArray items) PutItems(dir, items, "id");
        return this;
    }

    /// <summary>
    /// Edits the forces table as one JSON object (required, values, route), then writes it back as forces/hollow-vault.json
    /// plus one file per value, named by position so the loader's ordinal order is the array order.
    /// </summary>
    public MutableSource EditForces(Action<JsonNode> mutate)
    {
        JsonObject head = Parse(ForcesHead).AsObject();
        var forces = new JsonObject();
        if (head.ContainsKey("required")) forces["required"] = head["required"]?.DeepClone();
        forces["values"] = new JsonArray(PathsUnder(ForcesValuesDir).Select(p => (JsonNode?)Parse(p)).ToArray());
        foreach (var pair in head)
            if (!forces.ContainsKey(pair.Key)) forces[pair.Key] = pair.Value?.DeepClone();
        mutate(forces);

        foreach (string path in PathsUnder(ForcesValuesDir)) _files.Remove(path);
        var newHead = new JsonObject();
        foreach (var pair in forces.ToList())
            if (!(pair.Key == "values" && pair.Value is JsonArray)) newHead[pair.Key] = pair.Value?.DeepClone();
        Put(ForcesHead, newHead);
        if (forces["values"] is JsonArray values) PutItems(ForcesValuesDir, values, "enemyId");
        return this;
    }

    public MutableSource Add(string path, string text)
    {
        _files[path] = Encoding.UTF8.GetBytes(text);
        return this;
    }

    public MutableSource Remove(string path)
    {
        if (!_files.Remove(path)) throw new InvalidOperationException(path);
        return this;
    }

    public DictionaryContentSource Build() => new(_files);

    private string[] PathsUnder(string dir) =>
        _files.Keys.Where(p => p.StartsWith(dir, StringComparison.Ordinal)).OrderBy(p => p, StringComparer.Ordinal).ToArray();

    private JsonNode Parse(string path) => JsonNode.Parse(_files[path]) ?? throw new InvalidOperationException(path);

    private void Put(string path, JsonNode node) => _files[path] = Encoding.UTF8.GetBytes(node.ToJsonString());

    private void PutItems(string dir, JsonArray items, string idField)
    {
        for (int i = 0; i < items.Count; i++)
        {
            JsonNode? item = items[i];
            string id = item is JsonObject o && o[idField] is JsonValue v && v.TryGetValue(out string? s) ? s : "item";
            string path = dir + ((i + 1) * 10).ToString("D3", CultureInfo.InvariantCulture) + "-" + id + ".json";
            _files[path] = Encoding.UTF8.GetBytes(item is null ? "null" : item.ToJsonString());
        }
    }
}
