// G2.2 slice 3b (f-decb64d2, ruling R-TREES-4 (b)): every shipped tree node carries the edit its kind calls for
// (design/ABILITY-TREES.md s1.2, s1.5), the six s4 worked-example edits are the named ones, and the loader runs
// TreesRule.Validate on each tree file. RED on this tree: the shipped nodes carry no edit and no optionsFrom, and
// ContentLoader does not call TreesRule.Validate. Node-to-GearPatch and TreeComposer facts wait for the samepatch PR.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// The tree edits of every shipped rotational-ability node (ABILITY-TREES s1.2, s1.5, s4) and their load-time
/// validation through <see cref="ContentLoader"/> (R-TREES-4 (b)). Each shipped file is one class's tree.
/// </summary>
public sealed class TreeEditContentTests
{
    private const string TreesDir = "trees/";

    /// <summary>
    /// One s4 worked example: the named enhanced node, its display name, its conditional's <c>if</c> (kind and argument),
    /// whether it consumes the state it read, and the ops it appends in <c>then</c> (op name and integer amount).
    /// </summary>
    private sealed record S4Edit(string NodeId, string Name, string IfKind, string IfArg, bool Consume, (string Op, int Amount)[] Then);

    private static readonly S4Edit[] S4Edits =
    {
        new("warden.strike.enhanced", "Braced Strike", "selfHas", "braced", false, new[] { ("resource", 15) }),
        new("warden.reckoning.enhanced", "Vigilant Reckoning", "selfHas", "bracedSelf", true, new[] { ("damage", 200) }),
        new("cinder.detonate.enhanced", "Fieldburst", "targetHas", "burning", true, new[] { ("damage", 150) }),
        new("cinder.coldSnap.enhanced", "Vented Heat", "selfResourceAtLeast", "80", false, new[] { ("damage", 200) }),
        new("chanter.chorus.enhanced", "Hymnal Chorus", "selfHas", "hymn", false, new[] { ("heal", 60) }),
        new("chanter.dissonance.enhanced", "Discordant Verse", "selfResourceAtLeast", "1", false, new[] { ("resource", -1), ("damage", 130) }),
    };

    /// <summary>The s1.5 edit keys an enhanced node may carry (s1.5 list, plus the s1.2 <c>conditional</c> op).</summary>
    private static readonly string[] EnhancedEditKeys = { "conditional", "effectsAppend", "tagAdd", "tagRemove", "shape" };

    /// <summary>Every shipped tree file, as embedded content paths (<c>trees/&lt;classId&gt;.json</c>), in ordinal order.</summary>
    private static IEnumerable<string> ShippedTreePaths() =>
        EmbeddedContentSource.Instance.Paths
            .Where(p => p.StartsWith(TreesDir, StringComparison.Ordinal) && p.EndsWith(".json", StringComparison.Ordinal))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();

    private static string ShippedText(string path) => Encoding.UTF8.GetString(EmbeddedContentSource.Instance.Read(path));

    /// <summary>Walks up from the test binary to the project's own Fixtures folder, so a fixture never lives under game/content/data.</summary>
    private static string FixturePath(string relative)
    {
        for (DirectoryInfo? d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            string candidate = Path.Combine(d.FullName, "game", "content", "Keyrun.Content.Tests", "Fixtures", relative);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        throw new FileNotFoundException("Fixtures/" + relative + " not found above " + AppContext.BaseDirectory);
    }

    [Fact]
    public void Every_shipped_tree_node_carries_the_edit_its_kind_calls_for_and_the_s4_named_edits_match()
    {
        string? problem = FirstShippedProblem();
        Assert.True(problem is null, problem);
    }

    [Fact]
    public void The_shipped_tree_files_pass_TreesRule_Validate_with_zero_errors()
    {
        foreach (string path in ShippedTreePaths())
        {
            IReadOnlyList<string> errors = TreesRule.Validate(ShippedText(path));
            if (errors.Count > 0)
            {
                Assert.Fail(path + " has " + errors.Count + " Validate error(s); first: " + errors[0]);
            }
        }
    }

    [Fact]
    public void A_tree_file_with_an_edit_less_node_fails_to_load_with_the_Validate_message()
    {
        string text = File.ReadAllText(FixturePath("trees/edit-less-node.json"));
        IReadOnlyList<string> expected = TreesRule.Validate(text);
        Assert.NotEmpty(expected);
        var source = new MutableSource().Add("trees/edit-less-node.json", text).Build();
        ContentLoadException ex = Assert.Throws<ContentLoadException>(() => ContentLoader.Load(source));
        Assert.Contains(expected[0], ex.Message, StringComparison.Ordinal);
    }

    /// <summary>The first shipped node that breaks its kind's edit rule or its s4 named edit, or null when all pass.</summary>
    private static string? FirstShippedProblem()
    {
        foreach (string path in ShippedTreePaths())
        {
            using JsonDocument doc = JsonDocument.Parse(ShippedText(path));
            foreach (JsonElement node in doc.RootElement.GetProperty("nodes").EnumerateArray())
            {
                string? problem = NodeProblem(path, node);
                if (problem is not null)
                {
                    return problem;
                }
            }
        }
        return null;
    }

    private static string? NodeProblem(string path, JsonElement node)
    {
        string id = Str(node, "id");
        string kind = Str(node, "kind");
        string where = path + " node \"" + id + "\" (" + kind + ")";
        bool hasEdit = node.TryGetProperty("edit", out JsonElement edit) && edit.ValueKind == JsonValueKind.Object;
        switch (kind)
        {
            case "rank":
                // s1.5: a rank node's edit is effectDelta only.
                if (!hasEdit || !edit.TryGetProperty("effectDelta", out JsonElement delta) || !HasItems(delta))
                {
                    return where + " has no edit with a non-empty effectDelta";
                }
                return null;

            case "enhanced":
                // s1.5: an enhanced node carries at least one of the GearPatch keys (the s1.2 conditional op included).
                if (!hasEdit)
                {
                    return where + " has no edit";
                }
                if (!EnhancedEditKeys.Any(k => edit.TryGetProperty(k, out _)))
                {
                    return where + " edit has none of conditional, effectsAppend, tagAdd, tagRemove, shape";
                }
                S4Edit? named = S4Edits.FirstOrDefault(s => s.NodeId == id);
                return named is null ? null : S4Problem(where, node, edit, named);

            case "choice":
                // s1.5: a choice node carries optionsFrom "talents" and no edit.
                if (hasEdit)
                {
                    return where + " must carry no edit";
                }
                return Str(node, "optionsFrom") == "talents" ? null : where + " has no optionsFrom \"talents\"";

            default:
                return where + " has an unknown kind";
        }
    }

    private static string? S4Problem(string where, JsonElement node, JsonElement edit, S4Edit s4)
    {
        if (Str(node, "name") != s4.Name)
        {
            return where + " name is not the s4 name \"" + s4.Name + "\"";
        }
        if (!edit.TryGetProperty("conditional", out JsonElement cond) || cond.ValueKind != JsonValueKind.Object)
        {
            return where + " (s4 \"" + s4.Name + "\") needs edit.conditional";
        }
        if (!cond.TryGetProperty("if", out JsonElement cif) || cif.ValueKind != JsonValueKind.Object)
        {
            return where + " conditional has no if";
        }
        if (Str(cif, "kind") != s4.IfKind)
        {
            return where + " if.kind is not s4 \"" + s4.IfKind + "\"";
        }
        bool argOk = s4.IfKind == "selfResourceAtLeast"
            ? cif.TryGetProperty("n", out JsonElement n) && n.TryGetInt32(out int nv) && nv.ToString() == s4.IfArg
            : Str(cif, "id") == s4.IfArg;
        if (!argOk)
        {
            return where + " if argument is not s4 \"" + s4.IfArg + "\"";
        }
        bool consume = cond.TryGetProperty("consume", out JsonElement cons) && cons.ValueKind == JsonValueKind.True;
        if (consume != s4.Consume)
        {
            return where + " consume is " + consume + ", s4 says " + s4.Consume;
        }
        if (!cond.TryGetProperty("then", out JsonElement then) || then.ValueKind != JsonValueKind.Array)
        {
            return where + " conditional has no then array";
        }
        foreach ((string op, int amount) in s4.Then)
        {
            bool found = then.EnumerateArray().Any(o =>
                Str(o, "op") == op && o.TryGetProperty("amount", out JsonElement a) && a.TryGetInt32(out int v) && v == amount);
            if (!found)
            {
                return where + " then has no {op " + op + ", amount " + amount + "}";
            }
        }
        return null;
    }

    private static bool HasItems(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Array => e.GetArrayLength() > 0,
        JsonValueKind.Object => e.EnumerateObject().Any(),
        _ => false,
    };

    private static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out JsonElement v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? ""
            : "";
}
