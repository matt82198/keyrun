using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// Whole-tree count rule (ruling R-LOADER-1): every registry kind holds exactly the distinct ids found across the files
/// that feed it, and no id repeats across those files. It replaces the "Assert.Equal(N, ...) // was N-1" literal pattern:
/// adding a file grows both sides, so no count literal is edited when content grows by a file. The file sets are read
/// from the embedded tree by directory, so a new file under abilities/ (or, once wired, layout/ and forces/values/)
/// is counted without a test edit.
/// </summary>
public sealed class DistinctIdCountTests
{
    private static readonly IContentSource Source = EmbeddedContentSource.Instance;

    /// <summary>The ids in every item of every file under <paramref name="prefix"/> (a directory) or equal to it (a file).</summary>
    private static List<string> IdsIn(string prefix, Func<JsonNode, IEnumerable<JsonNode?>> items, string idField = "id")
    {
        string[] paths = prefix.EndsWith('/')
            ? ContentDirectory.Under(Source, prefix).ToArray()
            : new[] { prefix };
        Assert.NotEmpty(paths);
        var ids = new List<string>();
        foreach (string path in paths)
        {
            JsonNode root = JsonNode.Parse(Source.Read(path)) ?? throw new InvalidOperationException(path);
            foreach (JsonNode? item in items(root))
                ids.Add(item![idField]!.GetValue<string>());
        }
        return ids;
    }

    /// <summary>A file holds one item (an object) or many (an array).</summary>
    private static IEnumerable<JsonNode?> Each(JsonNode root) => root is JsonArray array ? array : new[] { root };

    private static void AssertDistinct(int registryCount, List<string> ids, string kind)
    {
        Assert.True(ids.Count > 0, kind + ": no ids found");
        Assert.True(ids.Count == ids.Distinct(StringComparer.Ordinal).Count(), kind + ": an id repeats across files");
        Assert.True(registryCount == ids.Count, kind + ": registry holds " + registryCount + ", files hold " + ids.Count + " distinct ids");
    }

    [Fact]
    public void Every_registry_kind_holds_exactly_the_distinct_ids_across_its_files()
    {
        ContentRegistry reg = ContentLoader.Load();

        AssertDistinct(reg.Abilities.Count, IdsIn("abilities/", Each), "abilities");
        AssertDistinct(reg.Effects.Count, IdsIn("effects/", Each), "effects");
        AssertDistinct(reg.Classes.Count, IdsIn("classes/", Each), "classes");
        AssertDistinct(reg.Enemies.Count, IdsIn("enemies/", Each), "enemies");
        AssertDistinct(reg.Dungeons.Count, IdsIn("dungeons/", Each), "dungeons");
        AssertDistinct(reg.Facets.Count, IdsIn("facets/", Each), "facets");
        AssertDistinct(reg.Combos.Count, IdsIn("combos.json", Each), "combos");
        AssertDistinct(reg.Clauses.Count, IdsIn("clauses.json", Each), "clauses");
        AssertDistinct(reg.ComboRiders.Count, IdsIn("combo-riders.json", Each), "combo riders");
        AssertDistinct(reg.Gear.Count, IdsIn("gear.json", Each), "gear");
    }

    [Fact]
    public void Layout_rooms_packs_and_forces_values_hold_exactly_the_distinct_ids_in_their_files()
    {
        ContentRegistry reg = ContentLoader.Load();

        // Single-file shape today (layout.json, forces.json). The wiring step swaps these three sources for
        // "layout/hollow-vault/rooms/", "layout/hollow-vault/packs/" and "forces/values/" with Each.
        AssertDistinct(reg.Layout.Boxes.Count, IdsIn("layout.json", r => r["boxes"]!.AsArray()), "layout rooms");
        AssertDistinct(reg.Layout.Packs.Count, IdsIn("layout.json", r => r["packs"]!.AsArray()), "layout packs");
        AssertDistinct(reg.Forces.Values.Count, IdsIn("forces.json", r => r["values"]!.AsArray(), "enemyId"), "forces values");
    }
}
