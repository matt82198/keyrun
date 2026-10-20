using System.Linq;
using System.Text.Json.Nodes;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// combo-riders.json (game/content/data/combo-riders.json, C#-only) replaces the effects of a combo whose BIBLE
/// section 4 effect the frozen content/data/combos.json cannot say: Anthem "Party gains 15% haste for 6 s." and Steam
/// "Burst; a cloud that blinds enemies inside (they miss) for 4 s." content/data stays verbatim (it is an oracle root).
/// </summary>
public sealed class ComboRiderTests
{
    private const string RiderFile = "combo-riders.json";

    private static ContentLoadException LoadFails(MutableSource source) =>
        Assert.Throws<ContentLoadException>(() => ContentLoader.Load(source.Build()));

    [Fact]
    public void Anthem_rider_hastes_the_party_for_120_ticks()
    {
        ContentRegistry reg = ContentLoader.Load();

        ComboRider anthem = reg.ComboRiders["anthem"];
        PartyEffectOp op = Assert.IsType<PartyEffectOp>(Assert.Single(anthem.Effects));
        Assert.Equal("hasted", op.EffectId);
        Assert.Equal(120, op.DurationTicks);
        Assert.Equal(1, op.Stacks);
        Assert.Equal(1500, reg.Effects["hasted"].Modifiers.HasteBp);
    }

    [Fact]
    public void Steam_rider_keeps_the_burst_and_opens_an_80_tick_blinding_cloud()
    {
        ContentRegistry reg = ContentLoader.Load();

        ComboRider steam = reg.ComboRiders["steam"];
        Assert.Equal(2, steam.Effects.Count);
        DamageOp burst = Assert.IsType<DamageOp>(steam.Effects[0]);
        Assert.Equal(150, burst.Amount);
        Assert.Equal("fire", burst.School);
        CloudOp cloud = Assert.IsType<CloudOp>(steam.Effects[1]);
        Assert.Equal("blinded", cloud.EffectId);
        Assert.Equal(80, cloud.DurationTicks);
        Assert.True(cloud.RadiusMm > 0);
    }

    [Fact]
    public void Only_anthem_and_steam_have_riders_and_the_frozen_combo_rows_are_unchanged()
    {
        ContentRegistry reg = ContentLoader.Load();

        Assert.Equal(new[] { "anthem", "steam" }, reg.ComboRiders.Keys.OrderBy(k => k, System.StringComparer.Ordinal).ToArray());
        Assert.IsType<ApplyEffectOp>(Assert.Single(reg.Combos["anthem"].Effects));
        Assert.Equal("blinded", Assert.IsType<ApplyEffectOp>(reg.Combos["steam"].Effects[1]).EffectId);
    }

    [Fact]
    public void Rider_file_is_listed_and_embedded()
    {
        Assert.Contains(RiderFile, ContentLoader.Files);
        Assert.Contains(RiderFile, EmbeddedContentSource.Instance.Paths);
    }

    [Fact]
    public void A_rider_for_an_unknown_combo_is_rejected()
    {
        var ex = LoadFails(new MutableSource().Edit(RiderFile, n => n[0]!["id"] = "nope"));
        Assert.Contains("unknown combo \"nope\"", ex.Message);
    }

    [Fact]
    public void A_rider_naming_an_unknown_effect_is_rejected()
    {
        var ex = LoadFails(new MutableSource().Edit(RiderFile, n => n[0]!["effects"]![0]!["effectId"] = "ghost"));
        Assert.Contains("unknown effect \"ghost\"", ex.Message);
    }

    [Fact]
    public void A_cloud_with_no_positive_radius_or_duration_is_rejected()
    {
        var ex = LoadFails(new MutableSource().Edit(RiderFile, n => n[1]!["effects"]![1]!["radiusMm"] = 0));
        Assert.Contains("radiusMm", ex.Message);
        ex = LoadFails(new MutableSource().Edit(RiderFile, n => n[1]!["effects"]![1]!["durationTicks"] = 0));
        Assert.Contains("durationTicks", ex.Message);
    }

    [Fact]
    public void A_party_or_cloud_op_outside_combo_riders_is_rejected()
    {
        var ex = LoadFails(new MutableSource().Edit("abilities/warden.json", n =>
            n[0]!["effects"]!.AsArray().Add(JsonNode.Parse("{\"op\":\"partyEffect\",\"effectId\":\"hasted\",\"durationTicks\":120,\"stacks\":1}"))));
        Assert.Contains("partyEffect", ex.Message);
        ex = LoadFails(new MutableSource().Edit("combos.json", n =>
            n[0]!["effects"]!.AsArray().Add(JsonNode.Parse("{\"op\":\"cloud\",\"effectId\":\"blinded\",\"durationTicks\":80,\"radiusMm\":2500}"))));
        Assert.Contains("cloud", ex.Message);
    }
}
