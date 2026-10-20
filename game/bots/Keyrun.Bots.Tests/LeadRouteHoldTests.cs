using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Keyrun.Tools.Replay;
using Xunit;
using Xunit.Abstractions;
using SimDungeon = Keyrun.Sim.Encounter.DungeonDef;

namespace Keyrun.Bots.Tests;

/// <summary>
/// LEADER-WAITS (STATE.md D27; <see cref="Follow.LeadRoute"/>, <see cref="Follow.HoldPastDoorMm"/>). <see cref="NavRouteTests"/>
/// never reaches the hold: its followers keep their formation slots 2500 mm behind the leader, so the last one crosses
/// the doorway (and the pull spawns, ending the hold) while the leader is still short of the hold point. Here the
/// Warden walks the real Hollow Vault route alone while seats 2 and 3 stand still at the Entrance, so he must reach the
/// Choir's hold point and wait there; then the two seats walk in, the pull starts with him still holding, and he is
/// released toward the far doorway.
/// </summary>
public sealed class LeadRouteHoldTests
{
    private const long Seed = 1;
    private const int LeaderId = 1;

    /// <summary>The designed hold (LEADER-WAITS): 4000 mm past the entry doorway, the room's layout start. Literal on purpose.</summary>
    private const long DesignedHoldMm = 4000;

    /// <summary>Ticks the leader walks alone: 30 s at 20 Hz, far more than the walk to the hold point needs.</summary>
    private const int AloneTicks = 600;

    /// <summary>Ticks the trailing seats get to walk in after the leader holds: 30 s at 20 Hz.</summary>
    private const int CatchUpTicks = 600;

    /// <summary>Ticks after the pull starts in which the released leader must walk on past the hold.</summary>
    private const int ReleaseTicks = 100;

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test with xUnit's output sink (the leader's progress past the doorway is printed).</summary>
    /// <param name="output">The sink.</param>
    public LeadRouteHoldTests(ITestOutputHelper output) => _output = output;

    /// <summary>The leader's distance past <paramref name="door"/> along the doorway-to-far-doorway line, mm.</summary>
    private static long Progress(Vec3 pos, Vec3 door, Vec3 far)
    {
        var dx = far.X - door.X;
        var dz = far.Z - door.Z;
        var length = Movement.Isqrt((dx * dx) + (dz * dz));
        return (((pos.X - door.X) * dx) + ((pos.Z - door.Z) * dz)) / length;
    }

    [Fact(DisplayName = "Lead route hold: alone in the Choir the leader holds 4000 mm past the doorway until the party is in, then walks on")]
    public void Leader_holds_past_the_doorway_until_the_party_is_inside()
    {
        var registry = ContentLoader.Load();
        var layoutDef = registry.Layout;
        var nav = NavLayouts.ToSim(layoutDef);
        var setup = RunSetup.Load(Path.Combine(RolesOracleTraceTests.OracleDir(), "hollow-vault-k2.setup.json"), Seed);
        SimDungeon dungeon = NavLayouts.PlaceDungeon(layoutDef, setup.Dungeon);
        var party = NavLayouts.PlaceParty(layoutDef, setup.Party.Take(3).ToList(), dungeon.Pulls[0].Id);
        var content = setup.Content with { Layout = nav, Bounds = nav.Extent };
        var run = RunRoot.CreateRun(Seed, party, dungeon, content, setup.Options);
        Assert.False(Pulls.IsSpawned(run.State, dungeon, 0), "pull 0 spawned at tick 0: the party did not start outside its room");

        var room = nav.RoomFor(dungeon.Pulls[0].Id)!;
        var k = nav.Boxes.ToList().FindIndex(b => string.Equals(b.Id, room.Id, StringComparison.Ordinal));
        Assert.True(k > 0, $"pull 0's room {room.Id} is box {k}, not a box past the start");
        var door = nav.Route[k - 1];
        var far = nav.Route[k];
        var roomLength = Progress(far, door, far);
        Assert.True(roomLength > 2 * DesignedHoldMm, $"{room.Id} is {roomLength} mm doorway to doorway: too short to tell a {DesignedHoldMm} mm hold from a longer one");

        // Phase 1: the leader walks the route alone; seats 2 and 3 get no commands and stay at the Entrance.
        var events = new List<SimEvent>();
        long maxProgress = long.MinValue;
        for (var i = 0; i < AloneTicks; i++)
        {
            var commands = new List<Command>();
            if (Follow.LeadRoute(run.State.Tick, run.State.Actors[LeaderId], run) is { } walk)
            {
                commands.Add(walk);
            }
            var stepped = RunRoot.StepRun(run, commands);
            run = stepped.Run;
            events.AddRange(stepped.Events);
            maxProgress = Math.Max(maxProgress, Progress(run.State.Actors[LeaderId].Pos, door, far));
        }
        var leader = run.State.Actors[LeaderId];
        var held = Progress(leader.Pos, door, far);
        _output.WriteLine($"alone {AloneTicks} ticks: leader at {leader.Pos.X},{leader.Pos.Z}, {held} mm past {room.Id}'s doorway (max {maxProgress}), room {roomLength} mm");

        Assert.DoesNotContain(events, e => e.Type == "pull_start");
        Assert.True(Movement.Contains(room.Box, leader.Pos), $"the leader at {leader.Pos} never entered {room.Id}");
        Assert.True(Math.Abs(held - DesignedHoldMm) <= Follow.ArriveMm,
            $"the leader holds {held} mm past {room.Id}'s doorway, want {DesignedHoldMm} +- {Follow.ArriveMm}");
        Assert.True(maxProgress <= DesignedHoldMm + Follow.ArriveMm,
            $"the leader walked {maxProgress} mm past {room.Id}'s doorway while the party was outside (hold {DesignedHoldMm})");
        Assert.Null(Follow.LeadRoute(run.State.Tick, leader, run));

        // Phase 2: the trailing seats walk the route in; the last one's entry starts the pull with the leader still held.
        int? startTick = null;
        for (var i = 0; i < CatchUpTicks && startTick is null; i++)
        {
            var commands = new List<Command>();
            foreach (var id in new[] { 2, 3 })
            {
                if (Follow.WalkRoute(run.State.Tick, run.State.Actors[id], nav) is { } step)
                {
                    commands.Add(step);
                }
            }
            if (Follow.LeadRoute(run.State.Tick, run.State.Actors[LeaderId], run) is { } walk)
            {
                commands.Add(walk);
            }
            var stepped = RunRoot.StepRun(run, commands);
            run = stepped.Run;
            if (stepped.Events.Any(e => e.Type == "pull_start"))
            {
                startTick = run.State.Tick;
            }
        }
        Assert.NotNull(startTick);
        var atStart = Progress(run.State.Actors[LeaderId].Pos, door, far);
        _output.WriteLine($"pull_start at tick {startTick}: leader {atStart} mm past the doorway");
        Assert.True(Math.Abs(atStart - DesignedHoldMm) <= Follow.ArriveMm,
            $"at pull_start the leader is {atStart} mm past the doorway, want held at {DesignedHoldMm} +- {Follow.ArriveMm}");

        // Phase 3: the pull is spawned, so the hold is over and the leader walks on toward the far doorway.
        Assert.NotNull(Follow.LeadRoute(run.State.Tick, run.State.Actors[LeaderId], run));
        for (var i = 0; i < ReleaseTicks; i++)
        {
            var commands = new List<Command>();
            if (Follow.LeadRoute(run.State.Tick, run.State.Actors[LeaderId], run) is { } walk)
            {
                commands.Add(walk);
            }
            run = RunRoot.StepRun(run, commands).Run;
        }
        var released = Progress(run.State.Actors[LeaderId].Pos, door, far);
        _output.WriteLine($"released {ReleaseTicks} ticks: leader {released} mm past the doorway");
        Assert.True(released > DesignedHoldMm + Follow.ArriveMm, $"{ReleaseTicks} ticks after pull_start the leader is still {released} mm past the doorway (not released)");
    }
}
