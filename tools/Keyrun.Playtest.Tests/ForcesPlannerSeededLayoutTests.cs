using System.Linq;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Playtest;
using Xunit;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// f-7aa25aa6 on the seeded two-branch dungeon (D45/D46): the layout's packs list carries the packs of the branch the party
/// does not walk (their rooms are no box of the walked layout), and the planner must never send the leader at one of them.
/// </summary>
public sealed class ForcesPlannerSeededLayoutTests
{
    [Theory(DisplayName = "Seeded layouts: the pack the leader is sent at stands in a box of the walked layout, seeds 1 to 5")]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Planner_targets_only_packs_on_the_walked_layout(long seed)
    {
        var seeded = SeededLayout.Build(ContentLoader.Load(), seed);
        var forces = ForcesPlan.Of(seeded.Registry)!;
        var checkedTicks = 0;
        var opts = new PlaytestOptions(2, seed, null)
        {
            Observe = (tick, run, _) =>
            {
                if (tick % 20 != 0 || tick > 3000)
                {
                    return;
                }
                var lead = run.State.Actors.Values.OrderBy(a => a.Id).First(a => a.Kind == Keyrun.Sim.Core.ActorKind.Player && a.Alive);
                if (ForcesPlanner.NextPackPoint(run, forces, lead) is { } point)
                {
                    checkedTicks++;
                    Assert.True(run.Content.Layout!.BoxIndexAt(point) >= 0, $"seed {seed} tick {tick}: pack point {point} is off the walked layout");
                }
            },
        };
        PlaytestRunner.Play(opts, seeded.Registry);
        Assert.True(checkedTicks > 0, "the planner never picked a pack, so the test proved nothing");
    }

    [Theory(DisplayName = "Seeded layouts: the party enters a room within 1500 ticks, seeds 1 to 5")]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Party_enters_a_room_early(long seed)
    {
        var seeded = SeededLayout.Build(ContentLoader.Load(), seed);
        var entered = -1;
        var opts = new PlaytestOptions(2, seed, null)
        {
            Observe = (tick, run, _) =>
            {
                if (entered < 0 && tick <= 1500 && run.State.Actors.Values.Any(a => a.Kind == Keyrun.Sim.Core.ActorKind.Player && a.Alive && run.Content.Layout!.BoxIndexAt(a.Pos) is var i && i >= 0 && run.Content.Layout.Boxes[i].IsRoom))
                {
                    entered = tick;
                }
            },
        };
        PlaytestRunner.Play(opts, seeded.Registry);
        Assert.True(entered >= 0, $"seed {seed}: no seat entered a room in 1500 ticks");
    }
}
