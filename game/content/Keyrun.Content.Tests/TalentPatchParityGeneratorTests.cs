using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Keyrun.Content;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// Pins the C# generator for the talent patch parity snapshot (<c>Fixtures/talent-patch-parity.json</c>). The snapshot
/// is a frozen file read by <see cref="TalentPatchParityTests"/>; the TS generator that wrote it was removed in PR 553,
/// so a content balance change had no way to regenerate it. <see cref="TalentPatchParitySnapshot.Generate"/> is the
/// replacement: it must reproduce the committed bytes on main content, and must reflect an in-memory content edit
/// without touching any other entry.
/// </summary>
public sealed class TalentPatchParityGeneratorTests
{
    private static readonly ContentRegistry Reg = ContentLoader.Load();

    [Fact]
    public void Generate_on_main_content_is_byte_equal_to_the_committed_fixture()
    {
        var expected = File.ReadAllBytes(SnapshotPath());
        var actual = Encoding.UTF8.GetBytes(TalentPatchParitySnapshot.Generate(Reg));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Changing_one_ability_cooldown_changes_only_that_ability_entry()
    {
        const string abilityId = "warden.strike";
        var source = new MutableSource().Edit("abilities/warden.json", node =>
        {
            var entry = node.AsArray().Single(a => (string)a!["id"]! == abilityId)!.AsObject();
            entry["cooldownTicks"] = 20;
        });
        var changed = ContentLoader.Load(source.Build());

        var before = JsonNode.Parse(TalentPatchParitySnapshot.Generate(Reg))!.AsObject();
        var after = JsonNode.Parse(TalentPatchParitySnapshot.Generate(changed))!.AsObject();

        var differingRows = 0;
        foreach (var section in new[] { "talents", "facets" })
        {
            var beforeRows = before[section]!.AsArray();
            var afterRows = after[section]!.AsArray();
            Assert.Equal(beforeRows.Count, afterRows.Count);
            for (var i = 0; i < beforeRows.Count; i++)
            {
                var b = beforeRows[i]!.AsObject();
                var a = afterRows[i]!.AsObject();
                if (b.ToJsonString() == a.ToJsonString()) continue;

                differingRows++;
                Assert.Equal(abilityId, (string)a["abilityId"]!);
                Assert.Equal(WithoutAbility(b), WithoutAbility(a));

                var bAbility = b["ability"]!.AsObject();
                var aAbility = a["ability"]!.AsObject();
                var keys = bAbility.Select(kv => kv.Key).Union(aAbility.Select(kv => kv.Key));
                var differingKeys = keys.Where(k => bAbility[k]?.ToJsonString() != aAbility[k]?.ToJsonString()).ToList();
                Assert.Equal(new[] { "cooldownTicks" }, differingKeys);
            }
        }
        Assert.True(differingRows > 0, "the edited ability appears in no snapshot row, so the test would be vacuous");
    }

    private static string WithoutAbility(JsonObject row)
    {
        var copy = JsonNode.Parse(row.ToJsonString())!.AsObject();
        copy.Remove("ability");
        return copy.ToJsonString();
    }

    private static string SnapshotPath()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var path = Path.Combine(d.FullName, "game", "content", "Keyrun.Content.Tests", "Fixtures", "talent-patch-parity.json");
            if (File.Exists(path)) return path;
        }
        throw new FileNotFoundException("Fixtures/talent-patch-parity.json not found above " + AppContext.BaseDirectory);
    }
}
