using System.Linq;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Xunit;
using SimSpawn = Keyrun.Sim.Encounter.PullSpawn;

namespace Keyrun.Bots.Tests;

/// <summary>
/// MPLUS-PATROLS: <see cref="NavLayouts.ToSimPatrols"/> (the conversion HostRunSetup arms the game with) turns the
/// shipped layout.json patrol into a sim <see cref="PatrolDef"/>: ids in order from the first free actor id, each member
/// at its content offset, the layout's waypoints and speed, and the idle packs' default aggro radius.
/// </summary>
public sealed class ShippedPatrolTests
{
    [Fact(DisplayName = "Shipped patrol: NavLayouts.ToSimPatrols builds the corridor-2 patrol from layout.json (ids, offsets, loop, speed, default radius)")]
    public void ToSimPatrols_builds_the_shipped_patrol()
    {
        var layout = ContentLoader.Load().Layout;
        var built = NavLayouts.ToSimPatrols(layout, 109, (enemyId, actorId) => new SimSpawn(actorId, enemyId, new Vec3(1, 2, 3), 0, 500));
        var patrol = Assert.Single(built);
        Assert.Equal("corridor-patrol", patrol.Id);
        Assert.Equal(new[] { 109, 110 }, patrol.Spawns.Select(s => s.ActorId).ToArray());
        Assert.All(patrol.Spawns, s => Assert.Equal("choir.melee", s.ClassId));
        Assert.Equal(new[] { new Vec3(0, 0, -600), new Vec3(0, 0, 600) }, patrol.Spawns.Select(s => s.Pos).ToArray());
        Assert.Equal(new[] { new Vec3(32500, 0, 0), new Vec3(26500, 0, 0) }, patrol.Waypoints.ToArray());
        Assert.Equal(50, patrol.SpeedMmPerTick);
        Assert.Equal(Pulls.DefaultAggroMm, patrol.AggroMm);
        Assert.Equal(new Vec3(29500, 0, 0), Patrols.PointAt(patrol, 60));
    }
}
