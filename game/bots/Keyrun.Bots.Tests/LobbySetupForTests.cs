using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// f-416df6aa: the lobby's run setup (<see cref="Lobby.SetupFor"/>) must build a run for a seed-generated dungeon the way
/// <see cref="DungeonPick.BuildSetup"/> does, passing the run seed as the dungeon seed. Before the fix it threw
/// "pass dungeonSeed" for sunken-bellfoundry.
/// </summary>
public sealed class LobbySetupForTests
{
    private const string Dungeon = "sunken-bellfoundry";

    [Fact(DisplayName = "Lobby.SetupFor builds a run for sunken-bellfoundry and carries the dungeon id through")]
    public void SetupFor_builds_the_seeded_dungeon()
    {
        var reg = ContentLoader.Load();
        var request = new QueueRequest(Dungeon, 2, null);

        var setup = Lobby.SetupFor(reg, request, "warden", runSeed: 7);

        Assert.Equal(Dungeon, setup.Dungeon.Id);
        Assert.Equal(DungeonPick.BuildSetup(reg, Dungeon, runSeed: 7, dungeonSeed: null, keyLevel: 2, humanClassId: "warden").Layout, setup.Layout);
    }
}
