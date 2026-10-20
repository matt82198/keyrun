using System;
using System.Linq;
using Keyrun.Content;
using Xunit;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// The how-to-play guide (`playtest guide`) teaches the controls, the plan file and the loop, never the answers: it names
/// no enemy and no enemy ability, so what to kick, dodge or focus is learned from runs. It documents every field the
/// policy reader accepts, so the guide and the reader cannot drift apart.
/// </summary>
public sealed class GuideTests
{
    private static readonly ContentRegistry Registry = ContentLoader.Load();

    [Fact(DisplayName = "Guide: names no enemy and no enemy ability (playtesters start without the answers)")]
    public void Guide_holds_no_answers()
    {
        var text = Guide.Text;
        Assert.False(string.IsNullOrWhiteSpace(text));
        var enemyAbilities = Registry.Enemies.Values.SelectMany(e => e.Abilities).Distinct(StringComparer.Ordinal);
        foreach (var id in Registry.Enemies.Keys.Concat(enemyAbilities))
        {
            Assert.False(text.Contains(id, StringComparison.Ordinal), $"the guide names \"{id}\"");
        }
    }

    [Fact(DisplayName = "Guide: documents every policy field the reader accepts")]
    public void Guide_documents_every_policy_field()
    {
        var text = Guide.Text;
        foreach (var field in PolicyReader.Fields)
        {
            Assert.True(text.Contains($"`{field}`", StringComparison.Ordinal), $"the guide does not document \"{field}\"");
        }
    }

    [Fact(DisplayName = "Guide: states the key rule (timed raises by tier, untimed stays)")]
    public void Guide_states_the_key_rule()
    {
        var text = Guide.Text;
        Assert.Contains("untimed key stays", text, StringComparison.Ordinal);
        Assert.Contains("+1", text, StringComparison.Ordinal);
        Assert.Contains("+3", text, StringComparison.Ordinal);
    }
}
