using System;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// R-ROLL-1 (design/PROGRESSION-PIVOT.md s2.1-2.3): the completion roll reads its drop rates and slot list from
/// game/content/data/loot/completion.json. RED on main 07e2be4b: the file does not exist. API S2 must add it with
/// <c>dropBpTimed</c> 6000, <c>dropBpDepleted</c> 3500 and <c>slots</c> = weapon, armor, charm, ring, keystone.
/// </summary>
public sealed class CompletionLootTests
{
    private const string CompletionFile = "loot/completion.json";

    private static JsonNode ReadTable()
    {
        Assert.Contains(CompletionFile, EmbeddedContentSource.Instance.Paths);
        return JsonNode.Parse(Encoding.UTF8.GetString(EmbeddedContentSource.Instance.Read(CompletionFile)))!;
    }

    [Fact]
    public void The_completion_table_is_listed_and_embedded()
    {
        Assert.Contains(CompletionFile, EmbeddedContentSource.Instance.Paths);
    }

    [Fact]
    public void The_completion_table_drops_at_6000_bp_on_a_timed_clear_and_3500_bp_on_a_depleted_run()
    {
        var table = ReadTable();
        Assert.Equal(6000, table["dropBpTimed"]!.GetValue<int>());
        Assert.Equal(3500, table["dropBpDepleted"]!.GetValue<int>());
    }

    [Fact]
    public void The_completion_table_names_the_five_gear_slots_in_bible_order()
    {
        var table = ReadTable();
        var slots = table["slots"]!.AsArray().Select(n => n!.GetValue<string>()).ToArray();
        Assert.Equal(new[] { "weapon", "armor", "charm", "ring", "keystone" }, slots);
    }
}
