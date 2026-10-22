using System.Collections.Generic;
using System.Linq;
using Keyrun.Host;
using Xunit;
using C = Keyrun.Content;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// TELEGRAPH-ANCHOR (finding f-ebb50027, BIBLE section 3): the host carries a content telegraph's <c>anchor</c> into the
/// sim script it runs. A content <c>"anchor": "target"</c> becomes <c>TelegraphSpec.AnchorTarget</c> on every scaled
/// copy of the step (pull and pack scripts alike); absent stays false, the frozen TS rule.
/// </summary>
public sealed class HostTelegraphAnchorTests
{
    private const string Seed = "seed.elite";
    private const string Slam = "seed.slam";

    private static C.ContentRegistry WithSlamAnchor(C.ContentRegistry reg, C.TelegraphAnchor? anchor)
    {
        var seed = reg.Enemies[Seed];
        var script = new C.ValueList<C.ScriptStep>(seed.Script.Select(s =>
            s.Cast == Slam ? s with { Telegraph = s.Telegraph! with { Anchor = anchor } } : s));
        var enemies = new C.ValueMap<C.EnemyDef>(reg.Enemies.Select(kv =>
            kv.Key == Seed ? new KeyValuePair<string, C.EnemyDef>(kv.Key, seed with { Script = script }) : kv));
        return reg with { Enemies = enemies };
    }

    private static List<bool> SlamAnchors(C.ContentRegistry reg) =>
        HostRunSetup.Build(reg, "hollow-vault", 2, HostRunSetup.Roster[0].ClassId, withBots: false)
            .Content.EnemyScripts!.Values.SelectMany(s => s).Where(s => s.Cast == Slam && s.Telegraph is not null)
            .Select(s => s.Telegraph!.AnchorTarget).ToList();

    [Fact]
    public void A_target_anchor_reaches_every_sim_copy_of_the_step()
    {
        var anchors = SlamAnchors(WithSlamAnchor(C.ContentLoader.Load(), C.TelegraphAnchor.Target));
        Assert.NotEmpty(anchors);
        Assert.All(anchors, Assert.True);
    }

    [Fact]
    public void Caster_and_absent_anchors_stay_caster()
    {
        Assert.All(SlamAnchors(WithSlamAnchor(C.ContentLoader.Load(), C.TelegraphAnchor.Caster)), Assert.False);
        var shipped = SlamAnchors(C.ContentLoader.Load());
        Assert.NotEmpty(shipped);
        Assert.All(shipped, Assert.False);
    }
}
