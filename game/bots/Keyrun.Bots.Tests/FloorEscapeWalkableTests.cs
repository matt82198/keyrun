using System;
using System.Linq;
using Keyrun.Bots.Shared;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Xunit;
using static Keyrun.Bots.Tests.RolesTestKit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// FLOOR-ESCAPE WALKABLE (finding f-4250969b, follows R-DODGE-2 from PR #631): a floor escape on a layout run must pick
/// only walkable safe points and walk the sim's path to them. The sim clamps every actor into its open boxes after each
/// tick (Run.StepLayout), so a straight move toward a point in a wall leaves the seat pinned against the wall inside the
/// hazard. These tests run the seat the way the sim does: the escape decision is applied each tick, then the walkable
/// clamp is applied. Off a layout run (every oracle run) the straight escape is unchanged.
/// </summary>
public sealed class FloorEscapeWalkableTests
{
    /// <summary>Ticks allowed for the seat to clear the hazard.</summary>
    private const int MaxEscapeTicks = 15;

    /// <summary>Walk step per move command, mm (the bots' walk is 180 mm per tick; the escape tests use the same step as RolesFloorEscapeTests).</summary>
    private const long StepMm = 2000;

    /// <summary>
    /// A 1200 mm corridor (x in [-600, 600]) running along z. Its walls sit at x = +/-600.
    /// </summary>
    private static readonly Bounds Corridor = new(-600, 600, -10000, 10000);

    /// <summary>
    /// Hostile floor patch centred at (-2000, 0), radius 2250 plus the default 500 mm margin = 2750 mm. The seat at the
    /// origin is inside it. The nearest point outside it on the seat's own z line is (1000, 0): inside the wall at x = 600,
    /// so the walkable rule must reject it. (500, 0) is still inside the hazard, so the search cannot stop there.
    /// </summary>
    private static readonly BotTelegraph Patch = Circle(Floor.FloorPatchPrefix + "enemy-fire", -2000, 0, 2250);

    private static NavLayout CorridorLayout() => new(
        new[] { new NavBox("corridor", Corridor, true, null) },
        new[] { new NavTrigger("p1", Corridor) },
        new[] { new Vec3(0, 0, 0) });

    /// <summary>
    /// Runs a damage seat from the origin through up to <see cref="MaxEscapeTicks"/> ticks on <paramref name="layout"/>
    /// (null = open field), applying every move the decision carries and then the walkable clamp, and returns its final
    /// position.
    /// </summary>
    private static PointMm RunEscape(NavLayout? layout, Bounds? walkable)
    {
        var bot = Bot.MakeBot(RoleSet.Default);
        var memory = BotMemory.Default(100);
        var prng = Skill.SeedPrng(1);
        var self = Actor(id: 1, x: 0, z: 0);
        for (var tick = 0; tick < MaxEscapeTicks; tick++)
        {
            var pos = PointMm.Of(self.Pos);
            if (!Positioning.IsInAnyDanger(pos, new[] { Inflated() }))
            {
                break;
            }
            var view = ViewOf(tick, KitRegistry.Empty, null, new[] { Patch }, self) with { Layout = layout };
            var result = bot.Decide(view, memory, self.Id, prng);
            memory = result.Memory;
            prng = result.Prng;
            var move = BotTestKit.FirstMove(result.Commands);
            var next = BotTestKit.ApplyMove(pos, move, StepMm);
            if (walkable is { } box)
            {
                next = new PointMm(Math.Clamp(next.X, box.MinX, box.MaxX), Math.Clamp(next.Z, box.MinZ, box.MaxZ));
            }
            BotTestKit.SetPos(self, next);
        }
        return PointMm.Of(self.Pos);
    }

    /// <summary>The patch as the escape sees it: danger, radius plus the default 500 mm margin.</summary>
    private static BotTelegraph Inflated() => Patch with { Danger = true, Radius = (Patch.Radius ?? 0) + 500 };

    [Fact(DisplayName = "floor escape walkable: corridor > a seat in a corridor narrower than the hazard leaves it along the walkable path, not into the wall")]
    public void SeatLeavesFloorPatchInsideCorridorNotIntoWall()
    {
        var layout = CorridorLayout();

        var final = RunEscape(layout, Corridor);

        Assert.False(
            Positioning.IsInAnyDanger(final, new[] { Inflated() }),
            $"the seat is still on the floor hazard after {MaxEscapeTicks} ticks, final position ({final.X}, {final.Z})");
    }

    [Fact(DisplayName = "floor escape walkable: open field > the straight escape from a floor patch is unchanged with no layout")]
    public void OpenFieldEscapeStillClearsFloorPatch()
    {
        var final = RunEscape(null, null);

        Assert.False(
            Positioning.IsInAnyDanger(final, new[] { Inflated() }),
            $"the seat is still on the floor hazard after {MaxEscapeTicks} ticks, final position ({final.X}, {final.Z})");
    }
}
