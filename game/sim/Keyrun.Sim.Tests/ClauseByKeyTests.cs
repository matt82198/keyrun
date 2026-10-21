using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// R1 (orchestrator ruling 2026-10-21, under Matt's "follow the bible"; BIBLE s8 "Nobody picks a Clause"): the key sets
/// the Clause. The Clause is a pure function of the key level and the host's rotation index, there is no pre-queue
/// pick, and a run starts from the key alone through <see cref="RunOptions.ForKey"/>. Replaces the pick-of-three
/// premise that ClausePickTests pinned.
/// </summary>
public sealed class ClauseByKeyTests
{
    private const int StartKey = 5;

    private static readonly IReadOnlyList<string> Four = new[] { "echo", "tide", "hush", "pact" };

    [Fact]
    public void The_key_and_rotation_alone_determine_the_clause()
    {
        for (var key = StartKey; key <= 20; key++)
        {
            for (var rotation = -3; rotation <= 7; rotation++)
            {
                var once = RunOptions.ForKey(Four, key, rotation, StartKey).ClauseId;
                var again = RunOptions.ForKey(Four.Reverse().ToList(), key, rotation, StartKey).ClauseId;
                Assert.Equal(once, again);
            }
        }
    }

    [Fact]
    public void A_run_starts_from_the_key_with_exactly_one_clause_and_no_pick()
    {
        for (var key = StartKey; key <= 20; key++)
        {
            var options = RunOptions.ForKey(Four, key, rotationIndex: 2, clauseStartKey: StartKey);
            Assert.NotNull(options.ClauseId);
            Assert.Contains(options.ClauseId!, Four);
        }
    }

    [Fact]
    public void No_clause_runs_below_the_starting_key()
    {
        for (var key = 2; key < StartKey; key++)
        {
            for (var rotation = 0; rotation < Four.Count; rotation++)
            {
                Assert.Null(RunOptions.ForKey(Four, key, rotation, StartKey).ClauseId);
            }
        }
    }

    [Fact]
    public void The_clause_rotates_so_every_clause_runs_at_a_fixed_key()
    {
        var seen = Enumerable.Range(0, Four.Count)
            .Select(rotation => RunOptions.ForKey(Four, 7, rotation, StartKey).ClauseId!)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Equal(Four.OrderBy(id => id, StringComparer.Ordinal), seen.OrderBy(id => id, StringComparer.Ordinal));
    }
}
