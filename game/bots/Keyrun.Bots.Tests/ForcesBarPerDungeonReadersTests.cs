using System.Collections.Generic;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Hud;
using Xunit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// S1 forces-seam: the seeded roster and the scoreboard are readers of the Forces bar. For sunken-bellfoundry they must
/// read that dungeon's own table (<see cref="ContentRegistry.ForcesFor"/>, 1000 here), not the Hollow Vault's (1100). The
/// registry is built in memory from the shipped content; no data file is edited.
/// </summary>
public sealed class ForcesBarPerDungeonReadersTests
{
    private const string Vault = "hollow-vault";
    private const string Foundry = "sunken-bellfoundry";
    private const int VaultRequired = 1100;
    private const int FoundryRequired = 1000;

    /// <summary>The shipped content with the two bars split: hollow-vault 1100, sunken-bellfoundry 1000.</summary>
    private static ContentRegistry SplitBars()
    {
        ContentRegistry reg = ContentLoader.Load();
        ForcesTable vault = reg.ForcesFor(Vault) with { Required = VaultRequired };
        ForcesTable foundry = reg.ForcesFor(Foundry) with { Required = FoundryRequired };
        return reg with
        {
            Forces = vault,
            ForcesByDungeon = new ValueMap<ForcesTable>(new[]
            {
                new KeyValuePair<string, ForcesTable>(Vault, vault),
                new KeyValuePair<string, ForcesTable>(Foundry, foundry),
            }),
        };
    }

    [Fact(DisplayName = "Forces bar per dungeon: the seeded roster target uses sunken-bellfoundry's own forces bar (1000), not the Hollow Vault's (1100)")]
    public void Seeded_roster_uses_the_dungeon_own_forces_bar()
    {
        ContentRegistry reg = SplitBars();

        Assert.Equal(reg.ForcesFor(Foundry).Required, SeededRoster.Params(reg, Foundry).ForcesRequired);
    }

    [Fact(DisplayName = "Forces bar per dungeon: the scoreboard forces bar uses sunken-bellfoundry's own forces bar (1000), not the Hollow Vault's (1100)")]
    public void Scoreboard_uses_the_dungeon_own_forces_bar()
    {
        ContentRegistry reg = SplitBars();

        var (_, required) = ScoreboardReading.ForcesOf(reg, Foundry);

        Assert.Equal(reg.ForcesFor(Foundry).Required, required);
    }
}
