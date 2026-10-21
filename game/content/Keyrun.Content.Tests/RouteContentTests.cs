using System.Linq;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// ROUTE-CONTENT-SCHEMA (game/design/FORCES-ROUTE-PLAN.md increment 2; BIBLE section 9.2): the forces table
/// (1000 points = 100%), the route's fork and rejoin pulls and the hazard rooms load as typed content and fail closed.
/// FOLD-U3 (ruling R7, one pack layer): optional packs live only in layout.json (<see cref="LayoutPack"/>), so the
/// packs.json layer and its branch-bar checks are gone; see SkippablePackTests for the pack checks.
/// </summary>
public sealed class RouteContentTests
{
    private const string ForcesFile = "forces.json";
    private const string HazardsFile = "hazards.json";
    private const string LayoutFile = "layout.json";

    /// <summary>One hazard in a room that layout.json names (a Room box); the room check reads layout.json.</summary>
    private const string Hazards = """
        [
          { "id": "floodjet", "name": "Floodjet", "room": "seed-chamber" }
        ]
        """;

    [Fact]
    public void Shipped_forces_table_gives_choir_160_pair_260_seed_180_of_a_1000_point_bar()
    {
        ContentRegistry reg = ContentLoader.Load();
        ForcesTable forces = reg.Forces;
        DungeonDef vault = reg.Dungeons["hollow-vault"];

        Assert.Equal(1000, forces.Required);
        Assert.Equal(40, forces.PointsOf("choir.melee"));
        Assert.Equal(80, forces.PointsOf("choir.lieutenant"));
        Assert.Equal(52, forces.PointsOf("pair.melee"));
        Assert.Equal(180, forces.PointsOf("seed.elite"));
        Assert.Equal(0, forces.PointsOf("hollowWarden.boss"));
        Assert.Equal(new[] { 160, 260, 180 }, vault.Pulls.Select(p => RouteForces.PullPoints(forces, p)).ToArray());
        Assert.Equal(600, RouteForces.CriticalPathPoints(forces, vault));
    }

    [Fact]
    public void Shipped_route_carries_its_optional_packs_in_layout_json_and_no_packs_json_layer_exists()
    {
        ContentRegistry reg = ContentLoader.Load();

        Assert.Equal("hollow-vault", reg.Forces.Route.DungeonId);
        Assert.Equal("pair", reg.Forces.Route.ForkAfterPull);
        Assert.Equal("seed", reg.Forces.Route.RejoinPull);
        Assert.Contains(reg.Layout.Packs, p => p.Optional);
        Assert.Empty(reg.Hazards);
        Assert.Contains(ForcesFile, ContentLoader.Files);
        Assert.Contains(HazardsFile, ContentLoader.Files);
        Assert.DoesNotContain("packs.json", ContentLoader.Files);
        Assert.DoesNotContain("packs.json", EmbeddedContentSource.Instance.Paths);
    }

    [Fact]
    public void A_hazard_in_a_layout_room_loads_with_its_room()
    {
        ContentRegistry reg = ContentLoader.Load(new MutableSource().Add(HazardsFile, Hazards).Build());

        Assert.Equal("seed-chamber", reg.Hazards["floodjet"].Room);
    }

    [Fact]
    public void A_teaching_pull_enemy_with_no_forces_value_fails_closed()
    {
        string message = LoadFails(new MutableSource().EditText(ForcesFile, t => t.Replace("""{ "enemyId": "pair.melee", "points": 52 },""", "")));

        Assert.Contains("pull \"pair\" enemy \"pair.melee\" has no forces value", message);
    }

    [Fact]
    public void Points_on_an_enemy_outside_every_pull_and_pack_fail_closed()
    {
        // BIBLE 9.2: the boss, the Twins and Echo adds are worth 0, so nothing can be farmed.
        string message = LoadFails(new MutableSource().EditText(ForcesFile, t => t.Replace(
            """{ "enemyId": "seed.elite", "points": 180 },""",
            """{ "enemyId": "seed.elite", "points": 180 }, { "enemyId": "hollowWarden.twinA", "points": 5 },""")));

        Assert.Contains("\"hollowWarden.twinA\" is worth 5 forces but is in no pull or pack", message);
    }

    [Fact]
    public void A_forces_value_for_an_unknown_or_repeated_enemy_fails_closed()
    {
        Assert.Contains("forces value references unknown enemy \"vault.ghost\"", LoadFails(new MutableSource().EditText(ForcesFile,
            t => t.Replace("\"enemyId\": \"choir.melee\"", "\"enemyId\": \"vault.ghost\""))));
        Assert.Contains("duplicate forces value for enemy \"pair.melee\"", LoadFails(new MutableSource().EditText(ForcesFile,
            t => t.Replace("\"enemyId\": \"seed.elite\"", "\"enemyId\": \"pair.melee\""))));
    }

    [Fact]
    public void The_rejoin_pull_must_come_after_the_fork_pull()
    {
        string message = LoadFails(new MutableSource().EditText(ForcesFile, t => t.Replace("\"rejoinPull\": \"seed\"", "\"rejoinPull\": \"choir\"")));

        Assert.Contains("route rejoinPull \"choir\" must come after forkAfterPull \"pair\"", message);
    }

    [Fact]
    public void A_hazard_in_an_unknown_room_fails_closed()
    {
        string message = LoadFails(new MutableSource().Add(HazardsFile, Hazards.Replace("\"seed-chamber\"", "\"sluice\"")));

        Assert.Contains("hazard \"floodjet\" is in unknown room \"sluice\"", message);
    }

    [Fact]
    public void Each_branch_total_is_critical_path_plus_neutral_and_own_packs_and_a_short_branch_fails_naming_it()
    {
        ContentRegistry reg = ContentLoader.Load();
        DungeonDef vault = reg.Dungeons["hollow-vault"];

        // Shipped: choir = 600 teaching + 240 hall (neutral, required) + 400 cantor; seed = 600 + 240 + 420 warrior.
        Assert.Equal(1240, RouteForces.BranchTotalPoints(reg.Forces, vault, reg.Layout.Packs, "choir-room"));
        Assert.Equal(1260, RouteForces.BranchTotalPoints(reg.Forces, vault, reg.Layout.Packs, "seed-chamber"));

        // A second optional seed-chamber pack (one pair.melee, 52) lifts every single-pack skip and the seed branch to
        // 1312 but leaves the choir branch at 1240, 10 short of a 1250 bar. Only the branch check can catch this.
        string message = LoadFails(new MutableSource()
            .EditText(ForcesFile, t => t.Replace("\"required\": 1000", "\"required\": 1250"))
            .EditText(LayoutFile, t => t.Replace("\"packs\": [", "\"packs\": [ { \"id\": \"warrior-second\", \"name\": \"Warrior Second\", \"room\": \"seed-chamber\", \"optional\": true, \"branch\": \"seed-chamber\", \"center\": { \"x\": 69000, \"z\": -5500 }, \"aggroMm\": 4000, \"enemies\": [ { \"enemyId\": \"pair.melee\", \"count\": 1, \"spawn\": { \"x\": 0, \"z\": 0 } } ] },")));

        Assert.Contains("branch \"choir-room\" reaches 1240 of the 1250 forces required (short by 10)", message);
    }

    private static string LoadFails(MutableSource source) =>
        Assert.Throws<ContentLoadException>(() => ContentLoader.Load(source.Build())).Message;
}
