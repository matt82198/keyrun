using System.Text.Json.Nodes;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// run.json pairMarks (game/content/data/run.json): the content half of the Pair's spread and stack consequences
/// (BIBLE section 9.6 item 2, finding f-87f2a9a5). content/data/effects/enemy.json gives spreadMarked and stackMarked
/// empty modifiers, so without this block the marks are decoration. C#-only because content/ is a frozen oracle root.
/// Missing or impossible values fail the load; there is no default.
/// </summary>
public sealed class PairMarksConfigTests
{
    private const string RunFile = "run.json";

    private static ContentLoadException LoadFails(MutableSource source)
    {
        return Assert.Throws<ContentLoadException>(() => ContentLoader.Load(source.Build()));
    }

    private static JsonObject Block(JsonNode root) => root["pairMarks"]!.AsObject();

    [Fact]
    public void Loads_the_shipped_values()
    {
        PairMarksConfig cfg = ContentLoader.Load().Run.PairMarks;

        Assert.Equal("spreadMarked", cfg.SpreadEffectId);
        Assert.Equal(3000, cfg.SpreadRadiusMm);
        Assert.Equal(80, cfg.SpreadDamage);
        Assert.Equal("stackMarked", cfg.StackEffectId);
        Assert.Equal(1500, cfg.StackRadiusMm);
        Assert.Equal(150, cfg.StackDamage);
    }

    [Fact]
    public void The_radii_match_the_pair_telegraphs_the_player_reads()
    {
        ContentRegistry reg = ContentLoader.Load();
        PairMarksConfig cfg = reg.Run.PairMarks;
        EnemyDef pair = reg.Enemies["pair.melee"];

        // The ring on the floor is the rule: what the player sees is the radius that hurts.
        Assert.Contains(pair.Script, s => s.Cast == "pair.spreadMark" && s.Telegraph!.Radius == cfg.SpreadRadiusMm);
        Assert.Contains(pair.Script, s => s.Cast == "pair.stackCall" && s.Telegraph!.Radius == cfg.StackRadiusMm);
    }

    [Fact]
    public void Missing_block_is_rejected()
    {
        var ex = LoadFails(new MutableSource().Edit(RunFile, n => n.AsObject().Remove("pairMarks")));
        Assert.Contains(RunFile, ex.Message);
    }

    [Theory]
    [InlineData("spreadEffectId")]
    [InlineData("stackEffectId")]
    public void Unknown_mark_effect_is_rejected(string field)
    {
        var ex = LoadFails(new MutableSource().Edit(RunFile, n => Block(n)[field] = "notAnEffect"));
        Assert.Contains("run pairMarks." + field, ex.Message);
        Assert.Contains("notAnEffect", ex.Message);
    }

    [Theory]
    [InlineData("spreadRadiusMm", 0)]
    [InlineData("spreadDamage", 0)]
    [InlineData("stackRadiusMm", -1)]
    [InlineData("stackDamage", 0)]
    public void Non_positive_values_are_rejected(string field, int value)
    {
        var ex = LoadFails(new MutableSource().Edit(RunFile, n => Block(n)[field] = value));
        Assert.Contains("run pairMarks." + field, ex.Message);
    }
}
