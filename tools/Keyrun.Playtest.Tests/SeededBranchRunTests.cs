using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Playtest;
using Keyrun.Sim.Core;
using Keyrun.Sim.Dungeon;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// The seeded segmented dungeon played headless (STATE.md D45/D46/D65, R-MULTIBOSS-1, PROOF receipt for R-K6-2): for seeds 1
/// to 5 and each branch of segment 0, the leader is forced onto that branch (<see cref="PlaytestOptions.LeaderBranch"/>),
/// and the run must (a) reach the final boss within par, and (b) keep every bot seat within follow range of the leader at
/// the fork tick, the first tick the leader stands in the walked branch's first room (D44: the lowest living seat leads,
/// the others follow). Data-driven: one case per seed x branch.
/// </summary>
public sealed class SeededBranchRunTests
{
    /// <summary>The segmented dungeon (the Sunken Bellfoundry, D65/D67): the seeded layout's model on main.</summary>
    private const string Dungeon = "sunken-bellfoundry";

    /// <summary>The seeds the receipt covers.</summary>
    private static readonly long[] Seeds = { 1, 2, 3, 4, 5 };

    /// <summary>Seed x branch cases: every branch of segment 0 is led for each seed.</summary>
    public static TheoryData<long, int> SeedBranchCases()
    {
        var data = new TheoryData<long, int>();
        foreach (var seed in Seeds)
        {
            for (var branch = 0; branch < RoomGraphGen.BranchCount; branch++)
            {
                data.Add(seed, branch);
            }
        }
        return data;
    }

    [Theory(DisplayName = "Seeded branches (key 2, seeds 1-5, each branch led): the party reaches the boss within par")]
    [MemberData(nameof(SeedBranchCases))]
    public void Leader_on_branch_reaches_boss_within_par(long seed, int branch)
    {
        var (result, _, _) = PlayLed(seed, branch);

        Assert.Equal(RunResult.Timed, result.Run.RunEnd.Result);
        Assert.NotNull(result.Run.RunEnd.ResultTick);
        var par = result.Setup.Content.ParTicks;
        Assert.NotNull(par);
        Assert.True(
            result.Run.RunEnd.ResultTick <= par,
            $"seed {seed} branch {branch}: ended at tick {result.Run.RunEnd.ResultTick}, par {par}");
    }

    [Theory(DisplayName = "Seeded branches (key 2, seeds 1-5, each branch led): every bot seat is within follow range of the leader at the fork")]
    [MemberData(nameof(SeedBranchCases))]
    public void Bots_follow_the_leader_at_the_fork(long seed, int branch)
    {
        var (_, fork, seats) = PlayLed(seed, branch);

        Assert.True(fork.Tick >= 0, $"seed {seed} branch {branch}: the leader never reached the walked branch's first room");
        Assert.NotEmpty(seats);
        foreach (var (seatId, gapMm) in seats)
        {
            Assert.True(
                gapMm <= ChainPull.PartyLeashMm,
                $"seed {seed} branch {branch} tick {fork.Tick}: seat {seatId} stands {gapMm} mm from the leader (follow range {ChainPull.PartyLeashMm} mm)");
        }
    }

    /// <summary>
    /// Plays the segmented dungeon for <paramref name="seed"/> with the leader forced onto <paramref name="branch"/> of segment 0,
    /// and records the fork tick (the leader's first tick in the walked branch's first room) and every living bot seat's
    /// distance to the leader at that tick.
    /// </summary>
    private static (PlaytestResult Result, ForkRecord Fork, List<(int SeatId, double GapMm)> Seats) PlayLed(long seed, int branch)
    {
        var reg = ContentLoader.Load();
        var segmented = SeededLayout.BuildSegmented(reg, Dungeon, seed);
        var armed = SeededLayout.ArmSegmented(reg, Dungeon, seed);
        var firstRoom = SeededLayout.RoomBoxId(
            segmented.Fill.Branches.Single(b => b.Segment == 0 && b.Branch == branch).Rooms[0]);
        var fork = new ForkRecord(-1);
        var seats = new List<(int, double)>();
        var options = new PlaytestOptions(2, seed, null)
        {
            DungeonId = Dungeon,
            LeaderBranch = branch,
            Observe = (tick, run, _) =>
            {
                if (fork.Tick >= 0 || run.Content.Layout is not { } layout)
                {
                    return;
                }
                var leader = Follow.Leader(run.State);
                if (leader is null)
                {
                    return;
                }
                var box = layout.BoxIndexAt(leader.Pos);
                if (box < 0 || layout.Boxes[box].Id != firstRoom)
                {
                    return;
                }
                fork.Tick = tick;
                foreach (var seat in run.State.Actors.Values.Where(a => a.Kind == ActorKind.Player && a.Alive && a.Id != leader.Id))
                {
                    var dx = (double)(seat.Pos.X - leader.Pos.X);
                    var dz = (double)(seat.Pos.Z - leader.Pos.Z);
                    seats.Add((seat.Id, Math.Sqrt((dx * dx) + (dz * dz))));
                }
            },
        };
        var result = PlaytestRunner.Play(options, armed);
        return (result, fork, seats);
    }

    /// <summary>The fork tick, set by the observer (-1 until the leader enters the walked branch).</summary>
    private sealed class ForkRecord
    {
        public ForkRecord(int tick) => Tick = tick;

        public int Tick { get; set; }
    }
}
