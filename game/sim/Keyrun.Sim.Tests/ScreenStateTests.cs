using System.Collections.Generic;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// The BIBLE s10 Key band (ENGINE-PLAN 3D, ruling R1 2026-10-21 "bible wins"): a stepper over keys 2-20, the key's
/// Clause read-only (BIBLE s8 "the key sets it": <see cref="ClauseRotation.ForKey"/>, no pre-queue pick), and Queue,
/// which locks the band and hands the host one key and that key's Clause.
/// </summary>
public sealed class ScreenStateTests
{
    private const string Dungeon = "hollow-vault";
    private const int StartKey = 5;
    private const int Rotation = 3;

    private static readonly IReadOnlyList<string> Four = new[] { "echo", "tide", "hush", "pact" };

    [Fact]
    public void The_key_stepper_clamps_to_two_through_twenty()
    {
        var s = ScreenState.Open(Dungeon, Four, 2, Rotation, StartKey);
        Assert.Equal(2, s.StepKey(-1).KeyLevel);
        Assert.Equal(3, s.StepKey(1).KeyLevel);
        Assert.Equal(20, s.StepKey(100).KeyLevel);
        Assert.Equal(2, ScreenState.Open(Dungeon, Four, 0, Rotation, StartKey).KeyLevel);
        Assert.Equal(20, ScreenState.Open(Dungeon, Four, 99, Rotation, StartKey).KeyLevel);
    }

    [Fact]
    public void The_key_clause_is_the_read_only_rotation_for_that_key_on_every_key()
    {
        var s = ScreenState.Open(Dungeon, Four, 2, Rotation, StartKey);
        for (var k = ScreenState.MinKey; k <= ScreenState.MaxKey; k++)
        {
            Assert.Equal(k, s.KeyLevel);
            Assert.Equal(ClauseRotation.ForKey(Four, k, Rotation, StartKey), s.KeyClause);
            s = s.StepKey(1);
        }
    }

    [Fact]
    public void The_key_sets_the_clause_from_the_sorted_rotation_so_the_offer_is_never_a_choice()
    {
        // Ordinal order of the four ids is echo, hush, pact, tide: rotation 3 picks slot 3 (tide), rotation 4 wraps to slot 0.
        Assert.Equal("tide", ScreenState.Open(Dungeon, Four, 5, Rotation, StartKey).KeyClause);
        Assert.Equal("echo", ScreenState.Open(Dungeon, Four, 5, Rotation + 1, StartKey).KeyClause);
    }

    [Fact]
    public void No_clause_runs_below_the_start_key()
    {
        Assert.Null(ScreenState.Open(Dungeon, Four, 4, Rotation, StartKey).KeyClause);
        Assert.NotNull(ScreenState.Open(Dungeon, Four, 5, Rotation, StartKey).KeyClause);
    }

    [Fact]
    public void Queue_hands_the_host_one_key_and_that_keys_clause_then_locks()
    {
        var s = ScreenState.Open(Dungeon, Four, 4, Rotation, StartKey).StepKey(2);
        var expected = ClauseRotation.ForKey(Four, 6, Rotation, StartKey);
        var (locked, request) = s.Queue();

        Assert.NotNull(request);
        Assert.Equal(new QueueRequest(Dungeon, 6, expected), request);
        Assert.Equal(expected, request!.ToRunOptions().ClauseId);
        Assert.True(locked.Locked);

        Assert.Same(locked, locked.StepKey(1));
        Assert.Null(locked.Queue().Request);
    }

    [Fact]
    public void Queue_below_the_start_key_carries_no_clause()
    {
        var (_, request) = ScreenState.Open(Dungeon, Four, 3, Rotation, StartKey).Queue();

        Assert.NotNull(request);
        Assert.Equal(3, request!.KeyLevel);
        Assert.Null(request.ClauseId);
        Assert.Null(request.ToRunOptions().ClauseId);
    }
}
