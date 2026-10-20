using System.Text.Json.Nodes;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// run.json classArmorBp (game/content/data/run.json): the passive armor layer of design/BIBLE.md section 3.1, class id
/// to bp removed from every incoming hit. C#-only because content/ is a frozen oracle root. A missing block, an unknown
/// class or a value outside [0, 9000] fails the load; there is no default.
/// </summary>
public sealed class ClassArmorConfigTests
{
    private const string RunFile = "run.json";

    private static ContentLoadException LoadFails(MutableSource source)
    {
        return Assert.Throws<ContentLoadException>(() => ContentLoader.Load(source.Build()));
    }

    private static JsonObject Block(JsonNode root) => root["classArmorBp"]!.AsObject();

    [Fact]
    public void The_warden_has_forty_percent_armor_and_the_others_none()
    {
        var armor = ContentLoader.Load().Run.ClassArmorBp;

        Assert.Equal(3500, armor["warden"]);
        Assert.False(armor.ContainsKey("cinder"));
        Assert.False(armor.ContainsKey("chanter"));
    }

    [Fact]
    public void Missing_block_is_rejected()
    {
        var ex = LoadFails(new MutableSource().Edit(RunFile, n => n.AsObject().Remove("classArmorBp")));
        Assert.Contains(RunFile, ex.Message);
    }

    [Fact]
    public void Unknown_class_is_rejected()
    {
        var ex = LoadFails(new MutableSource().Edit(RunFile, n => Block(n)["paladin"] = 3000));
        Assert.Contains("run classArmorBp has unknown class \"paladin\"", ex.Message);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(9001)]
    public void Out_of_range_armor_is_rejected(int value)
    {
        var ex = LoadFails(new MutableSource().Edit(RunFile, n => Block(n)["warden"] = value));
        Assert.Contains("run classArmorBp.warden must be in [0, 9000]", ex.Message);
    }
}
