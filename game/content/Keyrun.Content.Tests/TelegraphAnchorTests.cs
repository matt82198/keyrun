using System.Linq;
using System.Text.Json.Nodes;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// TELEGRAPH-ANCHOR (finding f-ebb50027, BIBLE section 3 "shape plus colour, drawn ahead of resolution", BIBLE 9.3
/// Searing Hex "circle under the target"): a script telegraph's optional <c>anchor</c> is "caster" (the default) or
/// "target"; any other value fails the load, and a target-anchored shape must be a circle (a cone or line drawn from
/// the target along the caster's facing would mark ground the cast never reaches).
/// </summary>
public sealed class TelegraphAnchorTests
{
    // The Seed's slam step (script[1]): a circle telegraph on the embedded content.
    private static JsonObject SlamTelegraph(JsonNode root) =>
        root.AsArray()[0]!["script"]!.AsArray()[1]!["telegraph"]!.AsObject();

    // The Seed's lash step (script[2]): a line telegraph.
    private static JsonObject LashTelegraph(JsonNode root) =>
        root.AsArray()[0]!["script"]!.AsArray()[2]!["telegraph"]!.AsObject();

    [Fact]
    public void A_target_anchored_circle_loads_as_target()
    {
        var reg = ContentLoader.Load(new MutableSource().Edit("enemies/seed.json", n => SlamTelegraph(n)["anchor"] = "target").Build());
        var tg = reg.Enemies["seed.elite"].Script.Single(s => s.Cast == "seed.slam").Telegraph!;
        Assert.Equal(TelegraphAnchor.Target, tg.Anchor);
    }

    [Fact]
    public void A_caster_anchor_loads_as_caster()
    {
        var reg = ContentLoader.Load(new MutableSource().Edit("enemies/seed.json", n => SlamTelegraph(n)["anchor"] = "caster").Build());
        Assert.Equal(TelegraphAnchor.Caster, reg.Enemies["seed.elite"].Script.Single(s => s.Cast == "seed.slam").Telegraph!.Anchor);
    }

    [Fact]
    public void An_absent_anchor_stays_absent_and_means_caster()
    {
        var tg = ContentLoader.Load().Enemies["seed.elite"].Script.Single(s => s.Cast == "seed.slam").Telegraph!;
        Assert.Null(tg.Anchor);
    }

    [Fact]
    public void An_unknown_anchor_fails_the_load()
    {
        var ex = Assert.Throws<ContentLoadException>(() =>
            ContentLoader.Load(new MutableSource().Edit("enemies/seed.json", n => SlamTelegraph(n)["anchor"] = "ground").Build()));
        Assert.Contains("enemies/seed.json", ex.Message);
    }

    [Fact]
    public void A_target_anchored_line_fails_the_load()
    {
        var ex = Assert.Throws<ContentLoadException>(() =>
            ContentLoader.Load(new MutableSource().Edit("enemies/seed.json", n => LashTelegraph(n)["anchor"] = "target").Build()));
        Assert.Contains("anchor", ex.Message);
        Assert.Contains("seed.lash", ex.Message);
    }
}
