using System.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Keyrun.Content.Tests;

/// <summary>
/// FINISH-NEEDS-FORCES (ledger f-8c50a787, WoW Mythic+): a key completes only at 100% Enemy Forces with the boss dead,
/// so a dungeon whose enemies cannot bank the requirement could never complete. Every enemy the content places (the
/// teaching pulls, every layout pack and every patrol) must together reach forces.json <c>required</c>.
/// </summary>
public sealed class AvailableForcesTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test with xUnit's output sink.</summary>
    /// <param name="output">The sink.</param>
    public AvailableForcesTests(ITestOutputHelper output) => _output = output;

    [Fact(DisplayName = "FINISH-NEEDS-FORCES: every placed enemy together reaches the forces requirement")]
    public void Total_available_forces_reach_the_requirement()
    {
        ContentRegistry reg = ContentLoader.Load();
        ForcesTable forces = reg.Forces;
        DungeonDef dungeon = reg.Dungeons[reg.Layout.Dungeon];

        int critical = RouteForces.CriticalPathPoints(forces, dungeon);
        int packs = reg.Layout.Packs.Sum(p => RouteForces.LayoutPackPoints(forces, p));
        int patrols = reg.Layout.Patrols.Sum(p => p.Enemies.Sum(e => e.Count * forces.PointsOf(e.EnemyId)));
        int total = critical + packs + patrols;
        _output.WriteLine($"available forces: critical={critical} packs={packs} patrols={patrols} total={total} required={forces.Required}");

        Assert.True(forces.Required > 0, "forces.json required must be positive");
        Assert.True(total >= forces.Required, $"placed enemies bank {total} of the {forces.Required} forces required");
    }
}
