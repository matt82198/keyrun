using System.Linq;
using System.Text.Json.Nodes;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// PURPOSE: this artifact exists to build the rest of slice S3 PACK-SCHEMA of game/design/FORCES-ROUTE-PLAN.md, the pack
/// <c>links</c> field (N1 PACK-LINKS), so a chain pull is a route decision (BIBLE section 9.1; finding f-82d9390e);
/// status: real.
/// S3's patrol half landed in #528 (layout.json <c>patrols</c>, LayoutPatrolTests: a waypoint outside its corridor fails
/// the load). A layout pack's optional <c>links</c> names the packs or patrols that engage with it; the validator fails
/// the load on a link that names no pack or patrol, on a pack linked to itself, and on a link listed twice.
/// </summary>
public sealed class PackSchemaTests
{
    private const string LayoutFile = "layout.json";

    private static MutableSource WithLinks(string packId, params string[] links) =>
        new MutableSource().Edit(LayoutFile, n =>
        {
            JsonNode pack = n["packs"]!.AsArray().First(p => (string)p!["id"]! == packId)!;
            pack["links"] = new JsonArray(links.Select(l => (JsonNode)JsonValue.Create(l)!).ToArray());
        });

    private static string LoadFails(MutableSource source) =>
        Assert.Throws<ContentLoadException>(() => ContentLoader.Load(source.Build())).Message;

    [Fact(DisplayName = "Pack schema (N1): a pack's links load, naming another pack and a patrol")]
    public void A_pack_link_to_another_pack_or_a_patrol_loads()
    {
        ContentRegistry reg = ContentLoader.Load(WithLinks("hall-sentries", "cantor-vigil", "corridor-patrol").Build());
        LayoutPack pack = reg.Layout.Packs.Single(p => p.Id == "hall-sentries");
        Assert.Equal(new[] { "cantor-vigil", "corridor-patrol" }, pack.Links);
        Assert.Empty(reg.Layout.Packs.Single(p => p.Id == "cantor-vigil").Links);
    }

    [Fact(DisplayName = "Pack schema (N1): a pack link that names no pack or patrol fails the load")]
    public void A_pack_link_naming_an_unknown_id_fails_the_load()
    {
        string message = LoadFails(WithLinks("hall-sentries", "no-such-pack"));
        Assert.Contains("pack \"hall-sentries\" links unknown pack or patrol \"no-such-pack\"", message);
    }

    [Fact(DisplayName = "Pack schema (N1): a pack link that names a teaching pull fails the load (links join packs and patrols only)")]
    public void A_pack_link_naming_a_pull_fails_the_load()
    {
        string message = LoadFails(WithLinks("hall-sentries", "pair"));
        Assert.Contains("links unknown pack or patrol \"pair\"", message);
    }

    [Fact(DisplayName = "Pack schema (N1): a pack linked to itself fails the load")]
    public void A_pack_linked_to_itself_fails_the_load()
    {
        string message = LoadFails(WithLinks("hall-sentries", "hall-sentries"));
        Assert.Contains("pack \"hall-sentries\" links itself", message);
    }

    [Fact(DisplayName = "Pack schema (N1): a pack that lists the same link twice fails the load")]
    public void A_duplicate_link_fails_the_load()
    {
        string message = LoadFails(WithLinks("hall-sentries", "cantor-vigil", "cantor-vigil"));
        Assert.Contains("pack \"hall-sentries\" lists link \"cantor-vigil\" twice", message);
    }
}
