using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// The HUD pull header (game/src/Hud/Objective.cs, via <see cref="PullHeader"/>) is player-facing copy: it names the
/// pull and numbers it, and never shows the designer's intent note (content <c>teaches</c>, BIBLE s9 "Each pull
/// teaches one thing") or a raw hyphenated boss mechanic id ("sundered-twins").
/// </summary>
public sealed class PullHeaderTests
{
    private static readonly ContentRegistry Registry = ContentLoader.Load();
    private static readonly DungeonDef Vault = Registry.Dungeons["hollow-vault"];

    /// <summary>Every pull id of the shipped dungeon, boss last.</summary>
    public static TheoryData<string> PullIds()
    {
        var data = new TheoryData<string>();
        foreach (var id in Vault.Pulls.Select(p => p.Id).Append(PullHeader.BossPullId))
        {
            data.Add(id);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(PullIds))]
    public void Header_HasNoDesignerIntentText(string pullId)
    {
        var header = PullHeader.Of(Registry, Vault, pullId);
        Assert.NotNull(header);
        var title = header!.Title;
        foreach (var pull in Vault.Pulls)
        {
            Assert.DoesNotContain(pull.Teaches, title, System.StringComparison.Ordinal);
        }
        foreach (var mechanic in Vault.Boss.Mechanics)
        {
            Assert.DoesNotContain(mechanic, title, System.StringComparison.Ordinal);
        }
        Assert.False(Regex.IsMatch(title, @"[a-z]+-[a-z]+"), $"header \"{title}\" shows a hyphenated mechanic id");
        Assert.DoesNotContain(" - ", title, System.StringComparison.Ordinal);
    }

    [Fact]
    public void Headers_NumberAndNameEveryPull()
    {
        var titles = Vault.Pulls.Select(p => p.Id).Append(PullHeader.BossPullId)
            .Select(id => PullHeader.Of(Registry, Vault, id)!.Title).ToArray();
        Assert.Equal(
            new[] { "Pull 1/4  The Choir", "Pull 2/4  The Pair", "Pull 3/4  The Seed", "Pull 4/4  " + Registry.Enemies[Vault.Boss.EnemyId].Name },
            titles);
    }

    [Fact]
    public void Header_UnknownPull_IsNull() => Assert.Null(PullHeader.Of(Registry, Vault, "nope"));
}
