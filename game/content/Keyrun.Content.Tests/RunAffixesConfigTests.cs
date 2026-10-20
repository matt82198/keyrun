using System.Text.Json.Nodes;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// run.json affixes (game/content/data/run.json): the content half of FORCES-ROUTE-PLAN M5 FORTIFIED-TYRANNICAL
/// (BIBLE s8, Matt 2026-10-20 "follow current WoW"): one rotating affix from key 5, both from key 10, the key-set
/// Clause from key 5, Fortified +2000 bp health and +3000 bp damage on trash, Tyrannical +2500 bp health and +1500 bp
/// damage on the boss side. A missing or impossible value fails the load; there is no default.
/// </summary>
public sealed class RunAffixesConfigTests
{
    private const string RunFile = "run.json";

    private static ContentLoadException LoadFails(MutableSource source)
    {
        return Assert.Throws<ContentLoadException>(() => ContentLoader.Load(source.Build()));
    }

    private static JsonObject Block(JsonNode root) => root["affixes"]!.AsObject();

    [Fact]
    public void Loads_the_bible_section_8_affix_keys_and_values()
    {
        AffixesConfig a = ContentLoader.Load().Run.Affixes;

        Assert.Equal(5, a.OneAffixKey);
        Assert.Equal(10, a.BothAffixKey);
        Assert.Equal(5, a.ClauseStartKey);
        Assert.Equal(2000, a.Fortified.HealthBp);
        Assert.Equal(3000, a.Fortified.DamageBp);
        Assert.Equal(2500, a.Tyrannical.HealthBp);
        Assert.Equal(1500, a.Tyrannical.DamageBp);
    }

    [Fact]
    public void Missing_block_is_rejected()
    {
        var ex = LoadFails(new MutableSource().Edit(RunFile, n => n.AsObject().Remove("affixes")));
        Assert.Contains(RunFile, ex.Message);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(5, 5)]
    [InlineData(10, 5)]
    public void Affix_keys_out_of_order_are_rejected(int one, int both)
    {
        var ex = LoadFails(new MutableSource().Edit(RunFile, n =>
        {
            Block(n)["oneAffixKey"] = one;
            Block(n)["bothAffixKey"] = both;
        }));
        Assert.Contains("run affixes must have 0 < oneAffixKey < bothAffixKey", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Non_positive_clause_start_key_is_rejected(int value)
    {
        var ex = LoadFails(new MutableSource().Edit(RunFile, n => Block(n)["clauseStartKey"] = value));
        Assert.Contains("run affixes.clauseStartKey", ex.Message);
    }

    [Theory]
    [InlineData("fortified", "healthBp")]
    [InlineData("fortified", "damageBp")]
    [InlineData("tyrannical", "healthBp")]
    [InlineData("tyrannical", "damageBp")]
    public void Negative_affix_scale_is_rejected(string affix, string field)
    {
        var ex = LoadFails(new MutableSource().Edit(RunFile, n => Block(n)[affix]!.AsObject()[field] = -1));
        Assert.Contains("run affixes." + affix, ex.Message);
    }
}
