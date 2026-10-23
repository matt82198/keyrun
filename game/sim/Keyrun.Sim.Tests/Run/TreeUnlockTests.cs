// G2.2 design/ABILITY-TREES.md s5.5 item 4 TreeUnlockTests: a point per completion only; key gates; refund cascade; TreeState.AllUnlocked.
// R-TREES-1 shape choice (the design leaves the API open): TreeState is an immutable record in Keyrun.Sim.Runs with
// Earned, BestKey and Nodes. Award, Take and Refund return a new TreeState. Take throws TreeUnlockException on a refused
// node. AllUnlocked builds the fully unlocked state from the node list. Red until S1 PR 4 adds these types.
using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>Tree points, key gates and refunds (ABILITY-TREES s2, s5.5 item 4; PIVOT s1.3 gates).</summary>
public sealed class TreeUnlockTests
{
    public static TheoryData<RunResult, int> PointPerCompletion => new()
    {
        // result, points earned by one run
        { RunResult.Timed, 1 },
        { RunResult.Depleted, 1 },
        { RunResult.Abandoned, 0 },
    };

    [Theory]
    [MemberData(nameof(PointPerCompletion))]
    public void A_point_is_earned_per_completion_only(RunResult result, int points)
    {
        // s5.5 item 4: a completion (timed, or depleted past par) earns a point; an abandon earns none.
        // Open shape gap: RunResult has no timer-cap-end or wipe value, so those two s2 cases cannot be pinned yet.
        TreeState after = TreeState.Empty.Award(result, keyLevel: 5);
        Assert.Equal(points, after.Earned);
    }

    [Fact]
    public void Points_are_capped_at_20_per_class()
    {
        // s5.5 item 4: the cap is 20 tree points per class (ABILITY-TREES s2).
        TreeState state = TreeState.Empty;
        for (var i = 0; i < 25; i++)
        {
            state = state.Award(RunResult.Timed, keyLevel: 10);
        }
        Assert.Equal(20, state.Earned);
    }

    [Fact]
    public void Choice_node_needs_key_4_and_Rank_III_needs_key_7()
    {
        // s5.5 item 4: key gates. Choice needs bestKey 4; Rank III needs bestKey 7 (ABILITY-TREES s2).
        TreeState keyThree = TreeState.Empty.Award(RunResult.Timed, keyLevel: 3).Award(RunResult.Timed, keyLevel: 3);
        Assert.Throws<TreeUnlockException>(() => keyThree.Take("warden.strike.choice", minKey: 4, requires: "warden.strike.rank2"));

        TreeState keySeven = TreeState.Empty.Award(RunResult.Timed, keyLevel: 7).Award(RunResult.Timed, keyLevel: 7)
            .Award(RunResult.Timed, keyLevel: 7).Award(RunResult.Timed, keyLevel: 7);
        TreeState afterRankIII = keySeven.Take("warden.strike.rank2", minKey: 0, requires: null)
            .Take("warden.strike.enhanced", minKey: 0, requires: "warden.strike.rank2")
            .Take("warden.strike.choice", minKey: 4, requires: "warden.strike.enhanced")
            .Take("warden.strike.rank3", minKey: 7, requires: "warden.strike.choice");
        Assert.Equal(4, afterRankIII.Nodes.Count);
    }

    [Fact]
    public void Rank_II_and_Enhanced_are_ungated_so_a_new_player_gets_one_after_two_completions()
    {
        // s5.5 item 4: Rank II and Enhanced have minKey 0; two completions (bestKey 1) are enough for the enhanced node.
        TreeState twoCompletions = TreeState.Empty.Award(RunResult.Timed, keyLevel: 1).Award(RunResult.Timed, keyLevel: 1);
        TreeState rankTwo = twoCompletions.Take("warden.strike.rank2", minKey: 0, requires: null);
        TreeState enhanced = rankTwo.Take("warden.strike.enhanced", minKey: 0, requires: "warden.strike.rank2");
        Assert.Equal(2, enhanced.Nodes.Count);
    }

    [Fact]
    public void A_node_is_refused_until_its_requires_is_taken()
    {
        // s5.5 item 4: requires chains; a node whose requires is not yet owned is refused.
        TreeState state = TreeState.Empty.Award(RunResult.Timed, keyLevel: 1).Award(RunResult.Timed, keyLevel: 1);
        Assert.Throws<TreeUnlockException>(() => state.Take("warden.strike.enhanced", minKey: 0, requires: "warden.strike.rank2"));
    }

    [Fact]
    public void Refund_removes_the_node_and_every_node_after_it_in_the_chain()
    {
        // s5.5 item 4: refund cascade. Right-click on rank2 drops rank2, enhanced and choice together; no cost.
        IReadOnlyList<string> chain = new[] { "warden.strike.rank2", "warden.strike.enhanced", "warden.strike.choice" };
        TreeState state = TreeState.Empty.Award(RunResult.Timed, keyLevel: 7).Award(RunResult.Timed, keyLevel: 7)
            .Award(RunResult.Timed, keyLevel: 7).Award(RunResult.Timed, keyLevel: 7);
        state = state.Take("warden.strike.rank2", minKey: 0, requires: null)
            .Take("warden.strike.enhanced", minKey: 0, requires: "warden.strike.rank2")
            .Take("warden.strike.choice", minKey: 4, requires: "warden.strike.enhanced");

        TreeState refunded = state.Refund("warden.strike.rank2", chain);

        Assert.Empty(refunded.Nodes);
        Assert.Equal(4, refunded.Earned);
    }

    [Fact]
    public void AllUnlocked_owns_every_node_and_leaves_no_free_points()
    {
        // s5.5 item 4: TreeState.AllUnlocked. Free points are Earned minus Nodes.Count, derived and never stored.
        IReadOnlyList<string> nodes = new[] { "warden.strike.rank2", "warden.strike.enhanced", "warden.strike.choice", "warden.strike.rank3" };
        TreeState all = TreeState.AllUnlocked(nodes, earned: 20, bestKey: 10);
        Assert.Equal(nodes.ToArray(), all.Nodes.ToArray());
        Assert.Equal(16, all.Earned - all.Nodes.Count);
    }
}
