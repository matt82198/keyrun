using System.Linq;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// f-8e3e199d (2): <c>--pull N</c> on a multi-boss dungeon. N is the Nth pull in route order across the segments, and a
/// segment boss counts as its own pull (the Bellfoundry is pulls 1 boss-s1, 2 boss-s2, 3 boss). A start at a segment boss
/// places the party in that boss's room and arms that boss first, so the bosses before it never fight and never gate the
/// run. Also pins (f-8e3e199d (3)) that the pre-run screen's pick is built through the same seam SimDriver.StartRun calls.
/// </summary>
public sealed class MultiBossPullStartTests
{
    private const string Dungeon = "sunken-bellfoundry";
    private const long Seed = 7;
    private const int Key = 2;

    [Fact(DisplayName = "--pull 2 on the Bellfoundry arms the pull 2 boss (boss-s2), never the boss of a pull the run does not fight")]
    public void Pull_two_arms_the_segment_two_boss()
    {
        var registry = ContentLoader.Load();
        var classId = Lobby.DefaultClassId;
        var setup = HostRunSetup.Build(registry, Dungeon, Key, classId, withBots: true, rotationIndex: 0, dungeonSeed: Seed);
        var run = HostRunFactory.Create(setup, new RunRequest(Seed, registry, classId) { StartPull = 2 }).Run;

        Assert.Equal(new[] { "boss-s2", "boss" }, run.Dungeon.Pulls.Select(p => p.Id));
        Assert.Equal(HostRunSetup.BossActorIdOf(1), run.Encounter.Boss!.Config.BossActorId);
        Assert.Equal(new[] { HostRunSetup.BossActorIdOf(2) }, run.Encounter.NextBosses.Select(c => c.BossActorId));
        Assert.True(run.State.Actors.ContainsKey(run.Encounter.Boss.Config.BossActorId), "the armed boss stands in the run");
    }

    [Fact(DisplayName = "--pull 3 on the Bellfoundry starts at the final boss alone: it is the only armed boss")]
    public void Pull_three_arms_the_final_boss_only()
    {
        var registry = ContentLoader.Load();
        var classId = Lobby.DefaultClassId;
        var setup = HostRunSetup.Build(registry, Dungeon, Key, classId, withBots: true, rotationIndex: 0, dungeonSeed: Seed);
        var run = HostRunFactory.Create(setup, new RunRequest(Seed, registry, classId) { StartPull = 3 }).Run;

        Assert.Equal(new[] { "boss" }, run.Dungeon.Pulls.Select(p => p.Id));
        Assert.Equal(HostRunSetup.BossActorIdOf(2), run.Encounter.Boss!.Config.BossActorId);
        Assert.Empty(run.Encounter.NextBosses);
    }

    [Fact(DisplayName = "--pull 2 on the Bellfoundry places the party in the pull 2 boss room, the route's start for that pull")]
    public void Pull_two_places_the_party_in_its_boss_room()
    {
        var registry = ContentLoader.Load();
        var classId = Lobby.DefaultClassId;
        var setup = HostRunSetup.Build(registry, Dungeon, Key, classId, withBots: true, rotationIndex: 0, dungeonSeed: Seed);
        var made = HostRunFactory.Create(setup, new RunRequest(Seed, registry, classId) { StartPull = 2 });
        var run = made.Run;

        var room = Assert.Single(setup.Layout!.Boxes, b => b.Pull == "boss-s2");
        var tank = made.Party.First();
        var at = run.State.Actors[tank.Id].Pos;
        Assert.Equal(room.Start!.X, at.X);
        Assert.Equal(room.Start!.Z, at.Z);
    }

    [Fact(DisplayName = "A pre-run screen pick of the Bellfoundry is built through the seam SimDriver.StartRun calls: the pick's id, the run seed")]
    public void A_screen_pick_builds_the_picked_dungeon_at_the_run_seed()
    {
        var registry = ContentLoader.Load();
        var pick = new QueueRequest(Dungeon, Key, null);
        var setup = DungeonPick.BuildSetup(registry, pick.DungeonId, runSeed: Seed, dungeonSeed: null, keyLevel: pick.KeyLevel, humanClassId: Lobby.DefaultClassId);

        Assert.Equal(Dungeon, setup.Dungeon.Id);
        Assert.Equal(SeededLayout.ArmSegmented(registry, Dungeon, Seed).Layout, setup.Layout);
        Assert.NotEqual(SeededLayout.ArmSegmented(registry, Dungeon, Seed + 1).Layout, setup.Layout);
    }
}
