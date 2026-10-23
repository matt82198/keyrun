using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Keyrun.Content;

/// <summary>
/// The improvement-tree validator (G2.2 design/ABILITY-TREES.md s1.2, s5.1; ruling R-TREES-1). A tree file is data;
/// <see cref="Validate"/> answers with the load errors it produces. No tree ships in this slice, so
/// <see cref="Check"/> has nothing in the registry to judge yet. The producer of a condition state is inferred from the
/// ability id prefix (<c>strike.*</c> is Strike's own tree); the cross-class producer check is deferred.
/// </summary>
public sealed class TreesRule : IContentRule
{
    private static readonly string[] Kinds = { "rank", "enhanced", "choice" };
    private static readonly string[] Floors = { "castTicks", "gcdTicks", "cooldownTicks", "cost" };

    /// <summary>
    /// Fails closed on a loaded tree node that names no loaded class or ability, names an id outside its own ability's
    /// prefix, has an unknown kind, or requires a node that is not in the registry. The per-kind edit rules are
    /// <see cref="Validate"/>'s, not checked here.
    /// </summary>
    /// <param name="reg">A loaded registry.</param>
    /// <exception cref="ContentLoadException">The first violated invariant.</exception>
    public void Check(ContentRegistry reg)
    {
        foreach (TreeNodeDef n in reg.Trees.Values)
        {
            if (!reg.Classes.ContainsKey(n.ClassId))
            {
                throw new ContentLoadException("content: tree node \"" + n.Id + "\" names unknown class \"" + n.ClassId + "\"");
            }
            if (!reg.Abilities.ContainsKey(n.AbilityId))
            {
                throw new ContentLoadException("content: tree node \"" + n.Id + "\" names unknown ability \"" + n.AbilityId + "\"");
            }
            if (!n.Id.StartsWith(n.AbilityId + ".", System.StringComparison.Ordinal))
            {
                throw new ContentLoadException("content: tree node \"" + n.Id + "\" is not prefixed by its ability \"" + n.AbilityId + "\"");
            }
            if (!Kinds.Contains(n.Kind))
            {
                throw new ContentLoadException("content: tree node \"" + n.Id + "\" has unknown kind \"" + n.Kind + "\"");
            }
            foreach (string req in n.Requires)
            {
                if (!reg.Trees.ContainsKey(req))
                {
                    throw new ContentLoadException("content: tree node \"" + n.Id + "\" requires \"" + req + "\", which is not a tree node");
                }
            }
        }
    }

    /// <summary>The loaded tree nodes of one class, in loaded order; empty for a class with no tree.</summary>
    /// <param name="reg">A loaded registry.</param>
    /// <param name="classId">The class id, e.g. <c>warden</c>.</param>
    /// <returns>The class's nodes in the order they were loaded.</returns>
    public static IReadOnlyList<TreeNodeDef> NodesFor(ContentRegistry reg, string classId) =>
        reg.Trees.Values.Where(n => n.ClassId == classId).ToArray();

    /// <summary>Validates one tree file's JSON against the s1.2 decision rules.</summary>
    /// <param name="json">The tree file text.</param>
    /// <returns>The load errors; empty when the tree loads.</returns>
    public static IReadOnlyList<string> Validate(string json)
    {
        var errs = new List<string>();
        if (!TryNodes(json, out List<JsonElement> nodes, out string? parseError))
        {
            errs.Add(parseError!);
            return errs;
        }
        var byId = new Dictionary<string, JsonElement>(System.StringComparer.Ordinal);
        foreach (JsonElement n in nodes)
        {
            string id = Str(n, "id");
            if (id.Length == 0 || !byId.TryAdd(id, n))
            {
                errs.Add("tree node id \"" + id + "\" is missing or duplicated");
            }
        }
        foreach (JsonElement n in nodes)
        {
            string id = Str(n, "id");
            string kind = Str(n, "kind");
            if (!Kinds.Contains(kind))
            {
                errs.Add("node \"" + id + "\" has unknown kind \"" + kind + "\"");
            }
            long minKey = Int(n, "minKey");
            foreach (string req in Requires(n))
            {
                if (!byId.TryGetValue(req, out JsonElement parent))
                {
                    errs.Add("node \"" + id + "\" requires \"" + req + "\", which is not a node");
                    continue;
                }
                if (minKey < Int(parent, "minKey"))
                {
                    errs.Add("node \"" + id + "\" minKey " + minKey + " is below its requirement \"" + req + "\" minKey");
                }
                string want = kind switch { "enhanced" => "rank", "choice" => "enhanced", "rank" => "choice", _ => "" };
                if (want.Length > 0 && Str(parent, "kind") != want)
                {
                    errs.Add("node \"" + id + "\" (" + kind + ") must require a " + want + " node, not \"" + req + "\"");
                }
            }
            if (kind is "rank" or "enhanced")
            {
                CheckEdit(n, id, kind, errs);
            }
        }
        return errs;
    }

    /// <summary>
    /// The class-level rule (s1.2 rule 4): the enhanced nodes of a class's trees read states from at least two other
    /// rotational abilities. A condition id with another ability's prefix, or a bare state id, counts as another
    /// ability's state; an id with the node's own ability prefix does not.
    /// </summary>
    /// <param name="json">The class tree file text.</param>
    /// <returns>The errors; empty when at least two distinct other states are read.</returns>
    public static IReadOnlyList<string> ClassReadErrors(string json)
    {
        var errs = new List<string>();
        if (!TryNodes(json, out List<JsonElement> nodes, out string? parseError))
        {
            errs.Add(parseError!);
            return errs;
        }
        var read = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (JsonElement n in nodes.Where(x => Str(x, "kind") == "enhanced"))
        {
            string own = Owner(Str(n, "id"));
            foreach (string cid in ConditionIds(n))
            {
                if (own.Length == 0 || !cid.StartsWith(own + ".", System.StringComparison.Ordinal))
                {
                    read.Add(cid);
                }
            }
        }
        if (read.Count < 2)
        {
            errs.Add("the class's enhanced nodes read " + read.Count + " other rotational abilities' states; they must read at least two other");
        }
        return errs;
    }

    private static void CheckEdit(JsonElement n, string id, string kind, List<string> errs)
    {
        if (!n.TryGetProperty("edit", out JsonElement edit) || edit.ValueKind != JsonValueKind.Object)
        {
            errs.Add("node \"" + id + "\" (" + kind + ") has no edit");
            return;
        }
        foreach (JsonElement holder in Holders(edit))
        {
            foreach (string f in Floors)
            {
                if (holder.TryGetProperty(f, out JsonElement v) && (v.ValueKind != JsonValueKind.Number || !v.TryGetInt64(out long amt) || amt <= 0))
                {
                    errs.Add("node \"" + id + "\" edits " + f + " to a free, instant or lowered value; those are fork work");
                }
            }
        }
        var ops = new List<JsonElement>();
        if (edit.TryGetProperty("effectsAppend", out JsonElement app) && app.ValueKind == JsonValueKind.Array)
        {
            ops.AddRange(app.EnumerateArray());
        }
        if (edit.TryGetProperty("conditional", out JsonElement cond) && cond.ValueKind == JsonValueKind.Object
            && cond.TryGetProperty("then", out JsonElement then) && then.ValueKind == JsonValueKind.Array)
        {
            ops.AddRange(then.EnumerateArray());
        }
        var names = ops.Select(o => Str(o, "op")).ToList();
        if (names.Contains("window") && names.Contains("payoff"))
        {
            errs.Add("node \"" + id + "\" gives one ability both a window and a payoff");
        }
        if (kind == "enhanced" && names.Any(o => o is "apply" or "applyEffect" or "partyEffect"))
        {
            errs.Add("node \"" + id + "\" applies an effect another button exists to apply");
        }
        string own = Owner(id);
        if (kind == "enhanced")
        {
            foreach (string cid in ConditionIds(n))
            {
                if (own.Length > 0 && cid.StartsWith(own + ".", System.StringComparison.Ordinal))
                {
                    errs.Add("node \"" + id + "\" reads its own output \"" + cid + "\"");
                }
            }
        }
    }

    private static IEnumerable<JsonElement> Holders(JsonElement edit)
    {
        yield return edit;
        if (edit.TryGetProperty("effectDelta", out JsonElement d) && d.ValueKind == JsonValueKind.Object)
        {
            yield return d;
        }
    }

    private static IEnumerable<string> ConditionIds(JsonElement n)
    {
        if (n.TryGetProperty("edit", out JsonElement edit) && edit.ValueKind == JsonValueKind.Object
            && edit.TryGetProperty("conditional", out JsonElement cond) && cond.ValueKind == JsonValueKind.Object
            && cond.TryGetProperty("if", out JsonElement c) && c.ValueKind == JsonValueKind.Object)
        {
            string id = Str(c, "id");
            if (id.Length > 0)
            {
                yield return id;
            }
        }
    }

    private static string Owner(string nodeId)
    {
        int dot = nodeId.IndexOf('.');
        return dot > 0 ? nodeId[..dot] : "";
    }

    private static IEnumerable<string> Requires(JsonElement n) =>
        n.TryGetProperty("requires", out JsonElement r) && r.ValueKind == JsonValueKind.Array
            ? r.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToList()
            : Enumerable.Empty<string>();

    private static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? ""
            : "";

    private static long Int(JsonElement e, string name) =>
        e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out long l) ? l : 0;

    private static bool TryNodes(string json, out List<JsonElement> nodes, out string? error)
    {
        nodes = new List<JsonElement>();
        error = null;
        try
        {
            JsonElement root = JsonDocument.Parse(json).RootElement.Clone();
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("nodes", out JsonElement arr) || arr.ValueKind != JsonValueKind.Array)
            {
                error = "tree file needs a \"nodes\" array";
                return false;
            }
            nodes.AddRange(arr.EnumerateArray());
            return true;
        }
        catch (JsonException ex)
        {
            error = "tree file is not valid JSON: " + ex.Message;
            return false;
        }
    }
}
