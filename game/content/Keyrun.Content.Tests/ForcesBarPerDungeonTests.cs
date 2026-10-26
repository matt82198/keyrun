using System.Collections.Generic;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// S1 forces-seam (fix/forces-seam): every reader of the Forces bar must read the layout's dungeon table
/// (<see cref="ContentRegistry.ForcesFor"/>), never the global <see cref="ContentRegistry.Forces"/>. The test registries are
/// built in memory from the shipped content; no data file is edited. The seeded roster and scoreboard readers live in
/// Keyrun.Bots.Tests (ForcesBarPerDungeonReadersTests.cs), because they are not reachable from this project.
/// </summary>
public sealed class ForcesBarPerDungeonTests
{
    private const string Vault = "hollow-vault";

    /// <summary>
    /// The bar the validator must judge the layout against. The shipped layout (the Vault's) passes the SKIPPABLE-PACKS rule
    /// at 1000 (its packs total 1632, and 1632 less the biggest optional pack, 400, is 1232).
    /// </summary>
    private const int OwnRequired = 1000;

    /// <summary>
    /// The global table's bar: above 1232, so the SKIPPABLE-PACKS rule fails at this value. A validator that reads the global
    /// table instead of the layout's dungeon table rejects the layout.
    /// </summary>
    private const int GlobalRequired = 1300;

    [Fact(DisplayName = "Forces bar per dungeon: the layout validator uses the layout dungeon's own forces bar (1000), not the global table's (1300)")]
    public void Layout_validator_uses_the_dungeon_own_forces_bar()
    {
        ContentRegistry reg = ContentLoader.Load();
        ForcesTable own = reg.ForcesFor(Vault) with { Required = OwnRequired };
        ForcesTable global = reg.Forces with { Required = GlobalRequired };
        ContentRegistry split = reg with
        {
            Forces = global,
            ForcesByDungeon = new ValueMap<ForcesTable>(new[] { new KeyValuePair<string, ForcesTable>(Vault, own) }),
        };

        Assert.Equal(OwnRequired, split.ForcesFor(Vault).Required);
        LayoutValidator.Check(split);
    }
}
