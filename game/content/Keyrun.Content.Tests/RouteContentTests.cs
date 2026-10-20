using System.Linq;
using System.Text.Json.Nodes;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// ROUTE-CONTENT-SCHEMA (game/design/FORCES-ROUTE-PLAN.md increment 2; BIBLE section 9.2): the forces table
/// (1000 points = 100%), the route's two branches with their fork and rejoin pulls, the optional packs and the hazard
/// rooms load as typed content and fail closed. No sim behaviour: these are the files the FORCES-SIM, PACKS-SIM and
/// PACK-CONTENT increments read.
/// </summary>
public sealed class RouteContentTests
{
    private const string ForcesFile = "forces.json";
    private const string PacksFile = "packs.json";
    private const string HazardsFile = "hazards.json";
    private const string FixtureEnemiesFile = "enemies/seed.json";

    /// <summary>The BIBLE 9.2 new enemies, as minimal fixture rows (PACK-CONTENT ships the real kits).</summary>
    private const string VaultEnemies = """
        [
          { "id": "vault.hound", "name": "Hollow Hound", "maxHealth": 100, "role": "melee", "abilities": [], "script": [] },
          { "id": "vault.brute", "name": "Vault Brute", "maxHealth": 100, "role": "melee", "abilities": [], "script": [] },
          { "id": "vault.hexer", "name": "Ash Hexer", "maxHealth": 100, "role": "caster", "abilities": [], "script": [] },
          { "id": "vault.keeper", "name": "Bellows Keeper", "maxHealth": 100, "role": "caster", "abilities": [], "script": [] },
          { "id": "vault.chorister", "name": "Drowned Chorister", "maxHealth": 100, "role": "caster", "abilities": [], "script": [] }
        ]
        """;

    /// <summary>BIBLE 9.2 point values and the 9.1 map (fork after the Pair, rejoin at the Seed).</summary>
    private const string Forces = """
        {
          "required": 1000,
          "values": [
            { "enemyId": "choir.melee", "points": 40 },
            { "enemyId": "choir.lieutenant", "points": 80 },
            { "enemyId": "pair.melee", "points": 52 },
            { "enemyId": "seed.elite", "points": 180 },
            { "enemyId": "vault.hound", "points": 35 },
            { "enemyId": "vault.brute", "points": 90 },
            { "enemyId": "vault.hexer", "points": 80 },
            { "enemyId": "vault.keeper", "points": 70 },
            { "enemyId": "vault.chorister", "points": 60 }
          ],
          "route": {
            "dungeonId": "hollow-vault",
            "forkAfterPull": "pair",
            "rejoinPull": "seed",
            "branches": [
              { "id": "west", "name": "West", "rooms": ["undercroft-steps", "bellows-hall"] },
              { "id": "east", "name": "East", "rooms": ["reading-gallery", "cantors-loft", "sluice-gallery"] }
            ]
          }
        }
        """;

    /// <summary>The five BIBLE 9.2 packs: W1 220, W2 400, E1 140, E2 180, E3 260.</summary>
    private const string Packs = """
        [
          { "id": "w1", "name": "Hound Kennel", "room": "undercroft-steps", "center": { "x": 0, "z": 0 }, "ringMm": 4000,
            "enemies": [ { "enemyId": "vault.hound", "count": 4, "spawn": { "x": 0, "z": 0 } },
                         { "enemyId": "vault.hexer", "count": 1, "spawn": { "x": 0, "z": 0 } } ] },
          { "id": "w2", "name": "Bellows Crew", "room": "bellows-hall", "center": { "x": 0, "z": 0 }, "ringMm": 4000,
            "enemies": [ { "enemyId": "vault.brute", "count": 2, "spawn": { "x": 0, "z": 0 } },
                         { "enemyId": "vault.keeper", "count": 2, "spawn": { "x": 0, "z": 0 } },
                         { "enemyId": "vault.hexer", "count": 1, "spawn": { "x": 0, "z": 0 } } ] },
          { "id": "e1", "name": "Reading Watch", "room": "reading-gallery", "center": { "x": 0, "z": 0 }, "ringMm": 4000,
            "enemies": [ { "enemyId": "choir.melee", "count": 2, "spawn": { "x": 0, "z": 0 } },
                         { "enemyId": "vault.chorister", "count": 1, "spawn": { "x": 0, "z": 0 } } ] },
          { "id": "e2", "name": "Loft Choir", "room": "cantors-loft", "center": { "x": 0, "z": 0 }, "ringMm": 4000,
            "enemies": [ { "enemyId": "choir.melee", "count": 1, "spawn": { "x": 0, "z": 0 } },
                         { "enemyId": "choir.lieutenant", "count": 1, "spawn": { "x": 0, "z": 0 } },
                         { "enemyId": "vault.chorister", "count": 1, "spawn": { "x": 0, "z": 0 } } ] },
          { "id": "e3", "name": "Floodgate Watch", "room": "sluice-gallery", "center": { "x": 0, "z": 0 }, "ringMm": 4000,
            "enemies": [ { "enemyId": "vault.brute", "count": 1, "spawn": { "x": 0, "z": 0 } },
                         { "enemyId": "choir.melee", "count": 1, "spawn": { "x": 0, "z": 0 } },
                         { "enemyId": "vault.chorister", "count": 1, "spawn": { "x": 0, "z": 0 } },
                         { "enemyId": "vault.keeper", "count": 1, "spawn": { "x": 0, "z": 0 } } ] }
        ]
        """;

    private const string Hazards = """
        [
          { "id": "vent-gout", "name": "Vent Gout", "room": "bellows-hall" },
          { "id": "floodjet", "name": "Floodjet", "room": "sluice-gallery" }
        ]
        """;

    // The fixture enemies ride in the shipped enemies/seed.json: a new enemies/ file would be an unlisted file, and
    // enemies/vault.json itself ships with PACK-CONTENT (see the PR body).
    private static MutableSource Routed() =>
        new MutableSource()
            .Edit(FixtureEnemiesFile, node =>
            {
                foreach (JsonNode? enemy in JsonNode.Parse(VaultEnemies)!.AsArray().ToArray())
                    node.AsArray().Add(enemy!.DeepClone());
            })
            .Add(ForcesFile, Forces).Add(PacksFile, Packs).Add(HazardsFile, Hazards);

    private static string LoadFails(MutableSource source) =>
        Assert.Throws<ContentLoadException>(() => ContentLoader.Load(source.Build())).Message;

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
    public void Shipped_route_forks_after_the_pair_and_rejoins_at_the_seed_with_no_packs_or_hazards_yet()
    {
        ContentRegistry reg = ContentLoader.Load();

        Assert.Equal("hollow-vault", reg.Forces.Route.DungeonId);
        Assert.Equal("pair", reg.Forces.Route.ForkAfterPull);
        Assert.Equal("seed", reg.Forces.Route.RejoinPull);
        Assert.Empty(reg.Forces.Route.Branches);
        Assert.Empty(reg.Packs);
        Assert.Empty(reg.Hazards);
        foreach (string file in new[] { ForcesFile, PacksFile, HazardsFile })
        {
            Assert.Contains(file, ContentLoader.Files);
            Assert.Contains(file, EmbeddedContentSource.Instance.Paths);
        }
    }

    [Fact]
    public void Section_9_2_routes_load_and_total_west_1220_and_east_1180()
    {
        ContentRegistry reg = ContentLoader.Load(Routed().Build());
        ForcesTable forces = reg.Forces;

        Assert.Equal(new[] { 220, 400, 140, 180, 260 },
            new[] { "w1", "w2", "e1", "e2", "e3" }.Select(id => RouteForces.PackPoints(forces, reg.Packs[id])).ToArray());
        BranchDef west = forces.Route.Branches.Single(b => b.Id == "west");
        BranchDef east = forces.Route.Branches.Single(b => b.Id == "east");
        Assert.Equal(1220, RouteForces.BranchPoints(reg, west));
        Assert.Equal(1180, RouteForces.BranchPoints(reg, east));
        Assert.Equal("bellows-hall", reg.Hazards["vent-gout"].Room);
        Assert.Equal(4000, reg.Packs["w2"].RingMm);
    }

    [Fact]
    public void A_pack_enemy_with_no_forces_value_fails_closed()
    {
        string message = LoadFails(Routed().EditText(ForcesFile, t => t.Replace("""{ "enemyId": "vault.hound", "points": 35 },""", "")));

        Assert.Contains("pack \"w1\"", message);
        Assert.Contains("\"vault.hound\" has no forces value", message);
    }

    [Fact]
    public void A_duplicate_pack_id_fails_closed()
    {
        string message = LoadFails(Routed().EditText(PacksFile, t => t.Replace("\"id\": \"e2\"", "\"id\": \"e1\"")));

        Assert.Contains("duplicate pack id \"e1\"", message);
    }

    [Fact]
    public void A_pack_in_an_unknown_room_fails_closed()
    {
        string message = LoadFails(Routed().EditText(PacksFile, t => t.Replace("\"room\": \"cantors-loft\"", "\"room\": \"cantors-attic\"")));

        Assert.Contains("pack \"e2\" is in unknown room \"cantors-attic\"", message);
    }

    [Fact]
    public void A_branch_whose_critical_path_plus_packs_is_under_required_fails_closed()
    {
        // Moving W2 (400) to the East leaves West 600 + 220 = 820.
        string message = LoadFails(Routed().EditText(PacksFile, t => t.Replace("\"room\": \"bellows-hall\"", "\"room\": \"sluice-gallery\"")));

        Assert.Contains("branch \"west\" reaches 820 of the 1000 forces required", message);
    }

    [Fact]
    public void A_branch_one_point_short_fails_and_exactly_required_loads()
    {
        // West without W1 is 600 + 400 = 1000 exactly (BIBLE 9.2: W2 alone fills the bar). Taking one point off the
        // Ash Hexer (W2's single Hexer) takes it to 999.
        MutableSource exact = Routed().EditText(PacksFile, t => t.Replace("\"room\": \"undercroft-steps\"", "\"room\": \"reading-gallery\""));
        ContentRegistry reg = ContentLoader.Load(exact.Build());
        Assert.Equal(1000, RouteForces.BranchPoints(reg, reg.Forces.Route.Branches.Single(b => b.Id == "west")));

        MutableSource shortByOne = Routed()
            .EditText(PacksFile, t => t.Replace("\"room\": \"undercroft-steps\"", "\"room\": \"reading-gallery\""))
            .EditText(ForcesFile, t => t.Replace("\"enemyId\": \"vault.hexer\", \"points\": 80", "\"enemyId\": \"vault.hexer\", \"points\": 79"));
        Assert.Contains("branch \"west\" reaches 999 of the 1000 forces required", LoadFails(shortByOne));
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
        string message = LoadFails(Routed().EditText(ForcesFile, t => t.Replace(
            """{ "enemyId": "seed.elite", "points": 180 },""",
            """{ "enemyId": "seed.elite", "points": 180 }, { "enemyId": "hollowWarden.twinA", "points": 5 },""")));

        Assert.Contains("\"hollowWarden.twinA\" is worth 5 forces but is in no pull or pack", message);
    }

    [Fact]
    public void A_forces_value_for_an_unknown_or_repeated_enemy_fails_closed()
    {
        Assert.Contains("forces value references unknown enemy \"vault.ghost\"", LoadFails(Routed().EditText(ForcesFile,
            t => t.Replace("\"enemyId\": \"vault.hound\"", "\"enemyId\": \"vault.ghost\""))));
        Assert.Contains("duplicate forces value for enemy \"pair.melee\"", LoadFails(Routed().EditText(ForcesFile,
            t => t.Replace("\"enemyId\": \"seed.elite\"", "\"enemyId\": \"pair.melee\""))));
    }

    [Fact]
    public void The_rejoin_pull_must_come_after_the_fork_pull()
    {
        string message = LoadFails(Routed().EditText(ForcesFile, t => t.Replace("\"rejoinPull\": \"seed\"", "\"rejoinPull\": \"choir\"")));

        Assert.Contains("route rejoinPull \"choir\" must come after forkAfterPull \"pair\"", message);
    }

    [Fact]
    public void A_room_claimed_by_both_branches_fails_closed()
    {
        string message = LoadFails(Routed().EditText(ForcesFile, t => t.Replace("\"cantors-loft\"", "\"bellows-hall\"")));

        Assert.Contains("room \"bellows-hall\" is in more than one branch", message);
    }

    [Fact]
    public void A_hazard_in_an_unknown_room_fails_closed()
    {
        string message = LoadFails(Routed().EditText(HazardsFile, t => t.Replace("\"room\": \"sluice-gallery\"", "\"room\": \"sluice\"")));

        Assert.Contains("hazard \"floodjet\" is in unknown room \"sluice\"", message);
    }
}
