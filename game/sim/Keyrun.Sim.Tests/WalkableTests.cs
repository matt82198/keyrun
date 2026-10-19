using System.Collections.Generic;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// The walkable-space rule (STATE.md D18, design/ART-PLAN.md section 3): a move is kept when it lands in any open box,
/// else it is clamped into the box that held the pre-move position. The flat-arena oracle fixtures keep their meaning
/// only if a one-box list is exactly today's <see cref="Movement.ClampToBounds(ActorState, Bounds)"/>.
/// </summary>
public sealed class WalkableTests
{
    // game/content/data/run.json arenaBounds, the flat arena every oracle run uses.
    private static readonly Bounds Arena = new(-4000, 18000, -6000, 9000);

    private static Vec3 Clamped(Vec3 p, Bounds b)
    {
        var a = new ActorState { Id = 1, Kind = ActorKind.Player, ClassId = "c", Pos = p };
        Movement.ClampToBounds(a, b);
        return a.Pos;
    }

    [Fact(DisplayName = "Walkable: a one-box list equals today's ClampToBounds over a grid of positions")]
    public void One_box_walkable_list_equals_today_clamp_on_a_grid()
    {
        var one = new[] { Arena };
        var pres = new List<Vec3>
        {
            new(0, 0, 4000), new(-4000, 0, -6000), new(18000, 0, 9000), new(7000, 0, 1500), // inside (edges included)
            new(-9000, 0, 0), new(25000, 0, 20000),                                       // outside: nearest-box fallback
        };
        var compared = 0;
        foreach (var pre in pres)
        {
            for (long x = -9000; x <= 23000; x += 1000)
            {
                for (long z = -11000; z <= 14000; z += 1000)
                {
                    var post = new Vec3(x, 37, z);
                    Assert.Equal(Clamped(post, Arena), Movement.ClampToWalkable(pre, post, one));
                    compared++;
                }
            }
        }
        Assert.Equal(6 * 33 * 26, compared);
    }

    [Fact(DisplayName = "Walkable: a move inside any open box is kept, across a doorway overlap")]
    public void A_move_into_any_open_box_is_kept()
    {
        var room = new Bounds(-5000, 5000, -5000, 5000);
        var corridor = new Bounds(4000, 12000, -1000, 1000);
        var open = new[] { room, corridor };
        Assert.Equal(new Vec3(4800, 0, 0), Movement.ClampToWalkable(new Vec3(4600, 0, 0), new Vec3(4800, 0, 0), open));
        Assert.Equal(new Vec3(5200, 0, 500), Movement.ClampToWalkable(new Vec3(4900, 0, 500), new Vec3(5200, 0, 500), open));
        Assert.Equal(new Vec3(9000, 0, 0), Movement.ClampToWalkable(new Vec3(8800, 0, 0), new Vec3(9000, 0, 0), open));
    }

    [Fact(DisplayName = "Walkable: a move out of every open box is clamped into the box that held the pre-move position")]
    public void A_move_out_of_the_open_space_is_clamped_into_the_pre_move_box()
    {
        var room = new Bounds(-5000, 5000, -5000, 5000);
        var corridor = new Bounds(4000, 12000, -1000, 1000);
        var open = new[] { room, corridor };
        // From the room past its +x wall beside the corridor mouth: clamped onto the room wall, not into the corridor.
        Assert.Equal(new Vec3(5000, 0, 3000), Movement.ClampToWalkable(new Vec3(4900, 0, 3000), new Vec3(5100, 0, 3000), open));
        // From the corridor through its side wall (pre is only in the corridor).
        Assert.Equal(new Vec3(8000, 0, 1000), Movement.ClampToWalkable(new Vec3(8000, 0, 900), new Vec3(8000, 0, 1100), open));
        // A closed corridor: only the room is open, so the doorway is a wall.
        Assert.Equal(new Vec3(5000, 0, 0), Movement.ClampToWalkable(new Vec3(4900, 0, 0), new Vec3(5100, 0, 0), new[] { room }));
    }
}
