using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// The BIBLE s10 Key band (ENGINE-PLAN 3D): a stepper over keys 2-20, the Clause pick re-offered per key, and Queue,
/// which locks the band and hands the host one key and one Clause.
/// </summary>
public sealed class ScreenStateTests
{
    private const string Dungeon = "hollow-vault";

    private static readonly IReadOnlyList<string> Four = new[] { "echo", "tide", "hush", "pact" };

    [Fact]
    public void The_key_stepper_clamps_to_two_through_twenty()
    {
        var s = ScreenState.Open(Dungeon, Four, 2);
        Assert.Equal(2, s.StepKey(-1).KeyLevel);
        Assert.Equal(3, s.StepKey(1).KeyLevel);
        Assert.Equal(20, s.StepKey(100).KeyLevel);
        Assert.Equal(2, ScreenState.Open(Dungeon, Four, 0).KeyLevel);
        Assert.Equal(20, ScreenState.Open(Dungeon, Four, 99).KeyLevel);
    }

    [Fact]
    public void Stepping_the_key_reoffers_the_clauses_for_that_key()
    {
        var s = ScreenState.Open(Dungeon, Four, 2);
        for (var k = 2; k < 20; k++)
        {
            s = s.StepKey(1);
            var expected = ClausePick.Open(Four, Dungeon, s.KeyLevel).Offers;
            Assert.Equal(expected, s.Clause.Offers);
            Assert.Contains(s.Clause.Picked, s.Clause.Offers);
        }
    }

    [Fact]
    public void A_pick_that_is_still_offered_survives_a_key_step()
    {
        var s = ScreenState.Open(Dungeon, Four, 2);
        for (var k = 2; k < 20; k++)
        {
            var next = s.StepKey(1);
            foreach (var id in s.Clause.Offers)
            {
                var picked = s.PickClause(id).StepKey(1);
                if (next.Clause.Offers.Contains(id))
                {
                    Assert.Equal(id, picked.Clause.Picked);
                }
            }
            s = next;
        }
    }

    [Fact]
    public void Queue_hands_the_host_one_key_and_one_clause_then_locks()
    {
        var s = ScreenState.Open(Dungeon, Four, 4).StepKey(2);
        var pick = s.Clause.Offers[2];
        s = s.PickClause(pick);
        var (locked, request) = s.Queue();

        Assert.NotNull(request);
        Assert.Equal(new QueueRequest(Dungeon, 6, pick), request);
        Assert.Equal(pick, request!.ToRunOptions().ClauseId);
        Assert.True(locked.Locked);

        Assert.Same(locked, locked.StepKey(1));
        Assert.Same(locked, locked.PickClause(locked.Clause.Offers[0]));
        Assert.Null(locked.Queue().Request);
    }

    [Fact]
    public void Picking_a_clause_not_offered_is_refused()
    {
        var s = ScreenState.Open(Dungeon, Four, 9);
        foreach (var id in Four)
        {
            if (!s.Clause.Offers.Contains(id))
            {
                Assert.Same(s, s.PickClause(id));
            }
        }
    }
}
