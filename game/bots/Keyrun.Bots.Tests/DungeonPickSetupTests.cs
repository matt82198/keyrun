using System;
using System.Linq;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Host;
using Xunit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// D65 / f-61f01ca3: the seam SimDriver builds a run through (<see cref="DungeonPick.BuildSetup"/>). A launch with
/// <c>--dungeon sunken-bellfoundry --seed 7</c> lays the Bellfoundry out seeded with 7 (the run seed), never a constant,
/// and <c>--dungeon-seed N</c> overrides the run seed for the layout alone.
/// </summary>
public sealed class DungeonPickSetupTests
{
    private const string Dungeon = "sunken-bellfoundry";

    [Fact(DisplayName = "--dungeon sunken-bellfoundry with run seed 7 builds the Bellfoundry layout seeded with 7")]
    public void The_run_seed_seeds_the_layout()
    {
        var reg = ContentLoader.Load();
        var setup = DungeonPick.BuildSetup(reg, Dungeon, runSeed: 7, dungeonSeed: null, keyLevel: 2, humanClassId: "warden");

        Assert.Equal(Dungeon, setup.Dungeon.Id);
        Assert.Equal(SeededLayout.ArmSegmented(reg, Dungeon, 7).Layout, setup.Layout);
        Assert.NotEqual(SeededLayout.ArmSegmented(reg, Dungeon, 8).Layout, setup.Layout);
    }

    [Fact(DisplayName = "--dungeon-seed N seeds the Bellfoundry layout instead of the run seed")]
    public void The_dungeon_seed_overrides_the_run_seed()
    {
        var reg = ContentLoader.Load();
        var setup = DungeonPick.BuildSetup(reg, Dungeon, runSeed: 7, dungeonSeed: 9, keyLevel: 2, humanClassId: "warden");

        Assert.Equal(SeededLayout.ArmSegmented(reg, Dungeon, 9).Layout, setup.Layout);
    }

    [Fact(DisplayName = "The Hollow Vault ignores the seeds: its hand-built layout is the same for every run seed")]
    public void The_hollow_vault_layout_is_hand_built()
    {
        var reg = ContentLoader.Load();
        var seven = DungeonPick.BuildSetup(reg, DungeonPick.DefaultId, runSeed: 7, dungeonSeed: null, keyLevel: 2, humanClassId: "warden");
        var one = DungeonPick.BuildSetup(reg, DungeonPick.DefaultId, runSeed: 1, dungeonSeed: null, keyLevel: 2, humanClassId: "warden");

        Assert.Equal(one.Layout, seven.Layout);
        Assert.Equal(new[] { "boss" }, seven.Dungeon.Pulls.Skip(seven.Dungeon.Pulls.Count - 1).Select(p => p.Id));
    }

    [Fact(DisplayName = "HasMap is true for the Bellfoundry (a layout generated from its seed) and the Hollow Vault")]
    public void Both_dungeons_have_a_map()
    {
        var reg = ContentLoader.Load();
        Assert.All(DungeonPick.Ids(reg), id => Assert.True(DungeonPick.HasMap(reg, id), $"{id} has no map"));
    }
}
