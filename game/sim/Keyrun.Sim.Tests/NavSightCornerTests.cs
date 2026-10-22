using System.Collections.Generic;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Nav;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// R-PATH-1, sight at an outer corner (f-eb9ac9de). The line-of-sight test sampled the segment every 250 mm, so a start
/// that clipped a room's outer corner on the way to a hall doorway passed, while the walker one step on, sampled at other
/// points, failed; it was then sent back to its cell centre, saw the corner again, and paced between the two for the rest
/// of the run. The fixture is the Sunken Bellfoundry on seed 7 (room 16, the hall to room 17): the chanter, sent to its
/// formation slot in room 17, never arrived, so the leader waited for it forever and the final boss was never engaged.
/// </summary>
public sealed class NavSightCornerTests
{
    private static readonly Bounds Room16 = new(430000, 454000, 28000, 52000);
    private static readonly Bounds Hall0 = new(453000, 458000, 39000, 42000);
    private static readonly Bounds Hall1 = new(455000, 458000, 34000, 42000);
    private static readonly Bounds Hall2 = new(457000, 474000, 34000, 37000);
    private static readonly Bounds Room17 = new(473000, 501000, 14000, 49500);
    private static readonly Vec3 Start = new(453305, 0, 37832);
    private static readonly Vec3 Slot = new(474776, 0, 34502);

    private static NavLayout Layout() => new(
        new[]
        {
            new NavBox("room-16", Room16, true, null),
            new NavBox("hall-0", Hall0, false, null),
            new NavBox("hall-1", Hall1, false, null),
            new NavBox("hall-2", Hall2, false, null),
            new NavBox("room-17", Room17, true, null),
        },
        new[] { new NavTrigger("p1", Room17) },
        new[] { new Vec3(431000, 0, 40000), new Vec3(453500, 0, 40500), new Vec3(457500, 0, 35500), new Vec3(473500, 0, 35500), new Vec3(484500, 0, 35500) });

    [Fact(DisplayName = "R-PATH-1: a segment that clips a room's outer corner between two samples is not in sight")]
    public void A_clipped_outer_corner_is_not_in_sight()
    {
        var grid = NavGrid.Of(Layout());
        // 453305,37832 to 454250,39250 leaves room 16 (x > 454000) for about 150 mm before it enters the hall (z >= 39000).
        Assert.False(grid.LineOfSight(Start, new Vec3(454250, 0, 39250)));
        // The same segment shifted clear of the corner is in sight, and so is a segment along the shared doorway overlap.
        Assert.True(grid.LineOfSight(new Vec3(453305, 0, 38600), new Vec3(454250, 0, 39250)));
        Assert.True(grid.LineOfSight(new Vec3(453500, 0, 40500), new Vec3(457500, 0, 40500)));
    }

    [Fact(DisplayName = "R-PATH-1: a walker beside room 16's outer corner walks through the hall to its slot in room 17, never pacing")]
    public void A_walker_at_the_outer_corner_arrives()
    {
        var layout = Layout();
        var grid = NavGrid.Of(layout);
        var open = new List<Bounds> { Room16, Hall0, Hall1, Hall2, Room17 };
        var pos = Start;
        var arrived = -1;
        for (var tick = 0; tick < 2000; tick++)
        {
            var next = grid.NextWaypoint(pos, Slot);
            var step = Fixed.NormaliseTo(new Vec3(next.X - pos.X, 0, next.Z - pos.Z), 180);
            pos = Movement.ClampToWalkable(pos, new Vec3(pos.X + step.X, 0, pos.Z + step.Z), open);
            var dx = pos.X - Slot.X;
            var dz = pos.Z - Slot.Z;
            if ((dx * dx) + (dz * dz) <= 300 * 300)
            {
                arrived = tick;
                break;
            }
        }

        Assert.True(arrived >= 0, $"the walker never reached its slot; it stopped at {pos}");
    }
}
