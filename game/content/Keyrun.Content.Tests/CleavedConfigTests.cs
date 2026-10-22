using System.Text.Json.Nodes;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// run.json cleaved (R-BOSS-4, ruling on #573): an unanswered Hollow Warden Cleave stacks the Cleaved effect for 400 ticks
/// (20 s, refreshed on hit); the effect is +25% damage taken per stack, capped at 5. The load fails on an unknown ability
/// or effect, or a non-positive duration.
/// </summary>
public sealed class CleavedConfigTests
{
    private const string RunFile = "run.json";

    private static JsonObject Block(JsonNode root) => root["cleaved"]!.AsObject();

    [Fact]
    public void Shipped_cleaved_marks_the_warden_cleave_for_twenty_seconds_at_a_quarter_per_stack()
    {
        var reg = ContentLoader.Load();
        var cleaved = reg.Run.Cleaved;
        Assert.NotNull(cleaved);
        Assert.Equal("hollowWarden.cleave", cleaved!.AbilityId);
        Assert.Equal("cleaved", cleaved.EffectId);
        Assert.Equal(400, cleaved.DurationTicks);
        var effect = reg.Effects["cleaved"];
        Assert.Equal(5, effect.MaxStacks);
        Assert.Equal(2500, effect.Modifiers.DamageTakenBp);
    }

    [Theory]
    [InlineData("abilityId", "hollowWarden.hug", "run cleaved.abilityId \"hollowWarden.hug\" is not an ability")]
    [InlineData("effectId", "bruised", "run cleaved.effectId \"bruised\" is not an effect")]
    public void Unknown_ids_are_rejected(string field, string value, string message)
    {
        var ex = Assert.Throws<ContentLoadException>(() => ContentLoader.Load(new MutableSource().Edit(RunFile, n => Block(n)[field] = value).Build()));
        Assert.Contains(message, ex.Message);
    }

    [Fact]
    public void A_non_positive_duration_is_rejected()
    {
        var ex = Assert.Throws<ContentLoadException>(() => ContentLoader.Load(new MutableSource().Edit(RunFile, n => Block(n)["durationTicks"] = 0).Build()));
        Assert.Contains("run cleaved.durationTicks must be positive, got 0", ex.Message);
    }
}
