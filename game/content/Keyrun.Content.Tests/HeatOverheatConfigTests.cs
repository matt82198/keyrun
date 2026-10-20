using System.Text.Json.Nodes;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// run.json heatOverheat (game/content/data/run.json): the content half of the BIBLE section 5 Cinder Overheat, "At
/// 100 the next cast is a free Overheat that dumps Heat to 0 and applies Chill (slower casts for 3 s)". The threshold is
/// the heat-overheat class loop max (classes/cinder.json, 100); this block names the Chill effect and its duration.
/// C#-only because content/ is a frozen oracle root. Missing or impossible values fail the load; there is no default.
/// </summary>
public sealed class HeatOverheatConfigTests
{
    private const string RunFile = "run.json";

    private static ContentLoadException LoadFails(MutableSource source)
    {
        return Assert.Throws<ContentLoadException>(() => ContentLoader.Load(source.Build()));
    }

    private static JsonObject Block(JsonNode root) => root["heatOverheat"]!.AsObject();

    [Fact]
    public void Loads_chill_for_three_seconds()
    {
        HeatOverheatConfig cfg = ContentLoader.Load().Run.HeatOverheat;

        Assert.Equal("chill", cfg.ChillEffectId);
        Assert.Equal(60, cfg.ChillTicks);
    }

    [Fact]
    public void The_chill_effect_slows_casts_and_the_cinder_loop_is_heat_overheat()
    {
        ContentRegistry reg = ContentLoader.Load();

        Assert.True(reg.Effects[reg.Run.HeatOverheat.ChillEffectId].Modifiers.HasteBp < 0);
        Assert.Equal(LoopKind.HeatOverheat, reg.Classes["cinder"].Loop.Kind);
        Assert.Equal(100, reg.Classes["cinder"].Loop.Max);
    }

    [Fact]
    public void Missing_block_is_rejected()
    {
        var ex = LoadFails(new MutableSource().Edit(RunFile, n => n.AsObject().Remove("heatOverheat")));
        Assert.Contains(RunFile, ex.Message);
    }

    [Fact]
    public void Unknown_chill_effect_is_rejected()
    {
        var ex = LoadFails(new MutableSource().Edit(RunFile, n => Block(n)["chillEffectId"] = "frostbite"));
        Assert.Contains("run heatOverheat.chillEffectId", ex.Message);
        Assert.Contains("frostbite", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-60)]
    public void Non_positive_chill_ticks_are_rejected(int value)
    {
        var ex = LoadFails(new MutableSource().Edit(RunFile, n => Block(n)["chillTicks"] = value));
        Assert.Contains("run heatOverheat.chillTicks", ex.Message);
    }
}
