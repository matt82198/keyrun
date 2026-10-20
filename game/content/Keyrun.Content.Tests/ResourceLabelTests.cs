using System;
using System.Linq;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// Lane RESOURCE-LABEL: the HUD resource bar names the local player's own class resource (BIBLE.md: Warden Momentum,
/// Cinder Heat, Chanter Verse), never a hardcoded "Momentum" for every class.
/// </summary>
public sealed class ResourceLabelTests
{
    [Theory]
    [InlineData("warden", "Momentum")]
    [InlineData("cinder", "Heat")]
    [InlineData("chanter", "Verse")]
    public void Each_shipped_class_resource_reads_as_its_bible_word(string classId, string expected)
    {
        ContentRegistry reg = ContentLoader.Load();

        Assert.True(reg.Classes.TryGetValue(classId, out var cls), $"class {classId} is shipped");
        Assert.Equal(expected, ResourceLabel.For(cls!.Loop.Resource));
    }

    [Fact]
    public void Every_shipped_class_json_resource_has_a_label()
    {
        ContentRegistry reg = ContentLoader.Load();

        var classes = reg.Classes.Values.ToArray();
        Assert.NotEmpty(classes);
        foreach (var cls in classes)
        {
            var label = ResourceLabel.For(cls.Loop.Resource);
            Assert.False(string.IsNullOrWhiteSpace(label), $"class {cls.Id} resource {cls.Loop.Resource} has a label");
        }
    }

    [Fact]
    public void Shipped_classes_read_three_distinct_labels_so_none_falls_back_to_momentum()
    {
        ContentRegistry reg = ContentLoader.Load();

        var labels = reg.Classes.Values.Select(c => ResourceLabel.For(c.Loop.Resource)).ToArray();
        Assert.Equal(labels.Length, labels.Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData("rage")]
    [InlineData("")]
    [InlineData("Heat")]
    public void An_unknown_resource_id_throws_instead_of_defaulting(string resourceId)
    {
        var ex = Assert.Throws<ArgumentException>(() => ResourceLabel.For(resourceId));
        Assert.Contains("resource", ex.Message, StringComparison.Ordinal);
    }
}
