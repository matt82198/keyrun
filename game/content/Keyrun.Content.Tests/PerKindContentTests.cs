using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// The per-kind content directories (ruling R-LOADER-1): two room files and two pack files assemble into the same
/// <see cref="LayoutDef"/> as the equivalent single layout.json, forces values merge by enemy id with a duplicate failing
/// by name, ordinal path order is deterministic, and the file-set rule accepts any file under a known directory while
/// the listed files stay listed.
/// </summary>
public sealed class PerKindContentTests : IDisposable
{
    private const string SingleLayout = """
        {
          "dungeon": "hv",
          "start": { "x": 100, "z": 0 },
          "boxes": [
            { "id": "entrance", "kind": "room", "minX": -1000, "maxX": 1000, "minZ": -1000, "maxZ": 1000 },
            {
              "id": "hall", "kind": "room", "minX": 900, "maxX": 5000, "minZ": -2000, "maxZ": 2000,
              "pull": "choir",
              "trigger": { "minX": 1200, "maxX": 5000, "minZ": -2000, "maxZ": 2000 },
              "origin": { "x": 3000, "z": 0 },
              "start": { "x": 1500, "z": 0 },
              "aggroMm": 7100
            }
          ],
          "route": [ { "x": 100, "z": 0 }, { "x": 3000, "z": 0 } ],
          "formation": [ { "classId": "chanter", "behindMm": 2500, "sideMm": -1000 } ],
          "packs": [
            { "id": "alpha", "name": "Alpha", "room": "hall", "optional": true, "center": { "x": 2000, "z": 500 }, "aggroMm": 3000,
              "enemies": [ { "enemyId": "hall.sentry", "count": 2, "spawn": { "x": 0, "z": 0 } } ] },
            { "id": "beta", "name": "Beta", "room": "hall", "optional": false, "center": { "x": 4000, "z": -500 }, "aggroMm": 3500,
              "enemies": [ { "enemyId": "choir.melee", "count": 1, "spawn": { "x": 100, "z": -100 } } ], "branch": "hall" }
          ]
        }
        """;

    private const string DungeonFile = """
        {
          "dungeon": "hv",
          "start": { "x": 100, "z": 0 },
          "route": [ { "x": 100, "z": 0 }, { "x": 3000, "z": 0 } ],
          "formation": [ { "classId": "chanter", "behindMm": 2500, "sideMm": -1000 } ]
        }
        """;

    private const string Room1 = """{ "id": "entrance", "kind": "room", "minX": -1000, "maxX": 1000, "minZ": -1000, "maxZ": 1000 }""";

    private const string Room2 = """
        {
          "id": "hall", "kind": "room", "minX": 900, "maxX": 5000, "minZ": -2000, "maxZ": 2000,
          "pull": "choir",
          "trigger": { "minX": 1200, "maxX": 5000, "minZ": -2000, "maxZ": 2000 },
          "origin": { "x": 3000, "z": 0 },
          "start": { "x": 1500, "z": 0 },
          "aggroMm": 7100
        }
        """;

    private const string PackAlpha = """
        { "id": "alpha", "name": "Alpha", "room": "hall", "optional": true, "center": { "x": 2000, "z": 500 }, "aggroMm": 3000,
          "enemies": [ { "enemyId": "hall.sentry", "count": 2, "spawn": { "x": 0, "z": 0 } } ] }
        """;

    private const string PackBeta = """
        { "id": "beta", "name": "Beta", "room": "hall", "optional": false, "center": { "x": 4000, "z": -500 }, "aggroMm": 3500,
          "enemies": [ { "enemyId": "choir.melee", "count": 1, "spawn": { "x": 100, "z": -100 } } ], "branch": "hall" }
        """;

    private const string SingleForces = """
        {
          "required": 1000,
          "values": [ { "enemyId": "a.one", "points": 40 }, { "enemyId": "b.two", "points": 60 } ],
          "route": { "dungeonId": "hv", "forkAfterPull": "pair", "rejoinPull": "seed" }
        }
        """;

    private const string ForcesRoute = """
        { "required": 1000, "route": { "dungeonId": "hv", "forkAfterPull": "pair", "rejoinPull": "seed" } }
        """;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "keyrun-per-kind-" + Guid.NewGuid().ToString("N"));

    public PerKindContentTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private void Put(string relative, string text)
    {
        string full = Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }

    private void PutSplitLayout()
    {
        // Written out of path order on purpose: the source, not creation order, decides the order.
        Put("layout/hv/packs/beta.json", PackBeta);
        Put("layout/hv/rooms/002-hall.json", Room2);
        Put("layout/hv/dungeon.json", DungeonFile);
        Put("layout/hv/packs/alpha.json", PackAlpha);
        Put("layout/hv/rooms/001-entrance.json", Room1);
    }

    [Fact]
    public void Two_room_files_and_two_pack_files_assemble_into_the_layout_the_single_layout_json_loads()
    {
        Put("layout.json", SingleLayout);
        PutSplitLayout();
        var source = new DirectoryContentSource(_root);

        LayoutDef single = PerKindContent.ReadLayoutFile(source, "layout.json");
        LayoutDef assembled = PerKindContent.AssembleLayout(source, "hv");

        Assert.Equal(new[] { "entrance", "hall" }, assembled.Boxes.Select(b => b.Id).ToArray());
        Assert.Equal(new[] { "alpha", "beta" }, assembled.Packs.Select(p => p.Id).ToArray());
        Assert.Equal(single, assembled);
    }

    [Fact]
    public void Forces_values_one_per_file_merge_into_the_table_the_single_forces_json_loads()
    {
        Put("forces.json", SingleForces);
        Put("forces/values/b.two.json", """{ "enemyId": "b.two", "points": 60 }""");
        Put("forces/hv.json", ForcesRoute);
        Put("forces/values/a.one.json", """{ "enemyId": "a.one", "points": 40 }""");
        var source = new DirectoryContentSource(_root);

        ForcesTable single = PerKindContent.ReadForcesFile(source, "forces.json");
        ForcesTable assembled = PerKindContent.AssembleForces(source, "hv");

        Assert.Equal(2, assembled.Values.Count);
        Assert.Equal(single, assembled);
    }

    [Fact]
    public void A_duplicate_enemy_id_across_two_forces_value_files_fails_the_load_naming_the_enemy()
    {
        Put("forces/hv.json", ForcesRoute);
        Put("forces/values/a.json", """{ "enemyId": "hall.sentry", "points": 40 }""");
        Put("forces/values/b.json", """{ "enemyId": "hall.sentry", "points": 60 }""");
        var source = new DirectoryContentSource(_root);

        var ex = Assert.Throws<ContentLoadException>(() => PerKindContent.AssembleForces(source, "hv"));
        Assert.Equal("content: duplicate forces value id \"hall.sentry\"", ex.Message);
    }

    [Fact]
    public void Ordinal_path_order_is_deterministic_same_files_same_records_twice()
    {
        PutSplitLayout();
        // Ordinal, not case-insensitive: "B-..." sorts before "a-..." (OrdinalIgnoreCase would flip them).
        Put("layout/hv/packs/a-late.json", PackAlpha.Replace("\"alpha\"", "\"gamma\"", StringComparison.Ordinal));
        Put("layout/hv/packs/B-early.json", PackBeta.Replace("\"beta\"", "\"delta\"", StringComparison.Ordinal));

        var first = new DirectoryContentSource(_root);
        var second = new DirectoryContentSource(_root);

        Assert.Equal(
            new[] { "layout/hv/packs/B-early.json", "layout/hv/packs/a-late.json", "layout/hv/packs/alpha.json", "layout/hv/packs/beta.json" },
            ContentDirectory.Under(first, "layout/hv/packs/").ToArray());
        LayoutDef a = PerKindContent.AssembleLayout(first, "hv");
        LayoutDef b = PerKindContent.AssembleLayout(second, "hv");
        Assert.Equal(new[] { "delta", "gamma", "alpha", "beta" }, a.Packs.Select(p => p.Id).ToArray());
        Assert.Equal(a, b);
        Assert.Equal(first.Paths.ToArray(), second.Paths.ToArray());
    }

    [Fact]
    public void A_missing_dungeon_file_fails_closed()
    {
        Put("layout/hv/rooms/001-entrance.json", Room1);
        var source = new DirectoryContentSource(_root);

        var ex = Assert.Throws<ContentLoadException>(() => PerKindContent.AssembleLayout(source, "hv"));
        Assert.Contains("layout/hv/dungeon.json", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_room_file_with_an_unknown_property_fails_with_the_strict_loader_rules()
    {
        PutSplitLayout();
        Put("layout/hv/rooms/003-bad.json", Room1.Replace("\"entrance\"", "\"bad\", \"colour\": 1", StringComparison.Ordinal));
        var source = new DirectoryContentSource(_root);

        var ex = Assert.Throws<ContentLoadException>(() => PerKindContent.AssembleLayout(source, "hv"));
        Assert.Contains("layout/hv/rooms/003-bad.json", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void File_set_accepts_any_file_under_a_known_directory_and_keeps_the_listed_files_listed()
    {
        Put("listed.json", "[]");
        PutSplitLayout();
        Put("forces/values/new-enemy.json", """{ "enemyId": "x", "points": 1 }""");
        string[] listed = { "listed.json" };

        ContentFileSet.Check(new DirectoryContentSource(_root), listed, PerKindContent.KnownDirectories);

        Put("stray.json", "[]");
        var unlisted = Assert.Throws<ContentLoadException>(() => ContentFileSet.Check(new DirectoryContentSource(_root), listed, PerKindContent.KnownDirectories));
        Assert.Contains("unlisted file \"stray.json\"", unlisted.Message, StringComparison.Ordinal);
        File.Delete(Path.Combine(_root, "stray.json"));

        // A prefix look-alike is not a known directory.
        Put("layoutx/a.json", "[]");
        var lookalike = Assert.Throws<ContentLoadException>(() => ContentFileSet.Check(new DirectoryContentSource(_root), listed, PerKindContent.KnownDirectories));
        Assert.Contains("unlisted file \"layoutx/a.json\"", lookalike.Message, StringComparison.Ordinal);
        Directory.Delete(Path.Combine(_root, "layoutx"), recursive: true);

        File.Delete(Path.Combine(_root, "listed.json"));
        var missing = Assert.Throws<ContentLoadException>(() => ContentFileSet.Check(new DirectoryContentSource(_root), listed, PerKindContent.KnownDirectories));
        Assert.Equal("content: missing file \"listed.json\"", missing.Message);
    }

    [Fact]
    public void Directory_source_paths_are_slash_separated_relative_and_ordinal()
    {
        PutSplitLayout();
        var source = new DirectoryContentSource(_root);

        Assert.Equal(
            new[]
            {
                "layout/hv/dungeon.json", "layout/hv/packs/alpha.json", "layout/hv/packs/beta.json",
                "layout/hv/rooms/001-entrance.json", "layout/hv/rooms/002-hall.json",
            },
            source.Paths.ToArray());
        Assert.Equal(new[] { "layout/hv/rooms/001-entrance.json", "layout/hv/rooms/002-hall.json" }, ContentDirectory.Under(source, "layout/hv/rooms/").ToArray());
    }
}
