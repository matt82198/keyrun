using System.Linq;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// MVP-U8 (MVP-MAP G5, BIBLE sections 9.1, 9.2, 9.5; WoW Mythic+ way): the three teaching pulls and the boss are the only
/// mandatory encounters. The Hall Sentries are no longer a required layout pack; they are an optional branch pack in
/// packs.json. The critical path alone stays under the 1000-point bar, and the critical path plus one optional branch
/// reaches it. The content loader fails closed on a malformed pack entry.
/// </summary>
public sealed class OptionalBranchForcesTests
{
    private const string PacksFile = "packs.json";

    [Fact]
    public void No_layout_pack_is_mandatory_so_the_walking_line_carries_no_trash()
    {
        LayoutDef layout = ContentLoader.Load().Layout;

        Assert.DoesNotContain(layout.Packs, p => !p.Optional);
        Assert.DoesNotContain(layout.Packs, p => p.Id == "hall-sentries");
    }

    [Fact]
    public void The_hall_sentries_are_an_optional_pack_in_packs_json_with_forces_values()
    {
        ContentRegistry reg = ContentLoader.Load();
        ForcesTable forces = reg.Forces;
        PackDef sentries = reg.Packs["hall-sentries"];

        Assert.Equal("Hall Sentries", sentries.Name);
        Assert.All(sentries.Enemies, e => Assert.True(forces.Has(e.EnemyId), $"{e.EnemyId} has no forces value"));
        Assert.Equal(240, RouteForces.PackPoints(forces, sentries));
    }

    [Fact]
    public void The_critical_path_alone_is_under_the_bar_and_the_teaching_pulls_stay_forward_only()
    {
        ContentRegistry reg = ContentLoader.Load();
        DungeonDef vault = reg.Dungeons[reg.Layout.Dungeon];

        Assert.Equal(new[] { "choir", "pair", "seed", "boss" }, reg.Layout.Boxes.Where(b => b.Pull is not null).Select(b => b.Pull!).ToArray());
        Assert.Equal(600, RouteForces.CriticalPathPoints(reg.Forces, vault));
        Assert.True(RouteForces.CriticalPathPoints(reg.Forces, vault) < reg.Forces.Required);
    }

    [Fact]
    public void The_critical_path_plus_one_optional_branch_reaches_the_bar()
    {
        ContentRegistry reg = ContentLoader.Load();
        ForcesTable forces = reg.Forces;
        DungeonDef vault = reg.Dungeons[reg.Layout.Dungeon];

        Assert.Single(forces.Route.Branches);
        BranchDef branch = forces.Route.Branches[0];
        int critical = RouteForces.CriticalPathPoints(forces, vault);
        int withBranch = RouteForces.BranchPoints(reg, branch);

        Assert.True(critical < forces.Required, $"critical path alone banks {critical} of {forces.Required}");
        Assert.True(withBranch >= forces.Required, $"critical path plus branch {branch.Id} banks {withBranch} of {forces.Required}");
    }

    [Fact]
    public void Every_pack_stands_in_the_optional_branch_so_the_critical_path_never_carries_one()
    {
        ContentRegistry reg = ContentLoader.Load();
        BranchDef branch = reg.Forces.Route.Branches.Single();

        Assert.NotEmpty(reg.Packs);
        Assert.All(reg.Packs.Values, p => Assert.Contains(p.Room, branch.Rooms));
    }

    [Fact]
    public void A_pack_entry_with_an_unknown_property_fails_closed()
    {
        const string Malformed = """
            [
              { "id": "hall-sentries", "name": "Hall Sentries", "room": "sentry-hall", "ringMm": 3000, "optional": true,
                "center": { "x": 0, "z": 0 },
                "enemies": [ { "enemyId": "hall.sentry", "count": 5, "spawn": { "x": 0, "z": 0 } } ] }
            ]
            """;
        var source = new MutableSource().Add(PacksFile, Malformed);

        var ex = Assert.Throws<ContentLoadException>(() => ContentLoader.Load(source.Build()));
        Assert.Contains(PacksFile, ex.Message);
    }

    [Fact]
    public void A_pack_entry_missing_its_ring_radius_fails_closed()
    {
        const string Malformed = """
            [
              { "id": "hall-sentries", "name": "Hall Sentries", "room": "sentry-hall",
                "center": { "x": 0, "z": 0 },
                "enemies": [ { "enemyId": "hall.sentry", "count": 5, "spawn": { "x": 0, "z": 0 } } ] }
            ]
            """;
        var source = new MutableSource().Add(PacksFile, Malformed);

        var ex = Assert.Throws<ContentLoadException>(() => ContentLoader.Load(source.Build()));
        Assert.Contains(PacksFile, ex.Message);
    }
}
