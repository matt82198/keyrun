using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Keyrun.Tools.Replay;
using Xunit;

namespace Keyrun.Sim.Tests.Encounter;

/// <summary>
/// MPLUS-PATROLS (design/BIBLE.md section 9.1 "it does not move unless it is a patrol"; Matt 2026-10-20 "lean into m+
/// design heavily while in the dungeon"): one patrol pack walks corridor-2 between (32500,0) and (26500,0) at 50 mm/tick
/// on a 240-tick loop. Its point is a pure integer function of the tick, its members stand idle on it from tick 0, it
/// never leaves its corridor, and it engages on the shared aggro rule as a pack (pull index -1, <c>pack_engage</c>).
/// </summary>
public sealed class PatrolTests
{
    /// <summary>corridor-2 in layout.json: x 25000..33000, z -1500..1500.</summary>
    private const long CorridorMinX = 25000, CorridorMaxX = 33000, CorridorHalfZ = 1500;

    private const int MemberA = 700, MemberB = 701;

    /// <summary>The shipped patrol: two choir.melee members, one either side of the path, the loop from (32500,0) to (26500,0) and back.</summary>
    private static PatrolPack Corridor(PullSpawn template) => new(
        "corridor-patrol",
        new[]
        {
            template with { ActorId = MemberA, Pos = new Vec3(0, 0, -600), Yaw = 0 },
            template with { ActorId = MemberB, Pos = new Vec3(0, 0, 600), Yaw = 0 },
        },
        new[] { new Vec3(32500, 0, 0), new Vec3(26500, 0, 0) },
        50);

    private static (Run Run, PatrolPack Patrol) RunWithPatrol(Func<int, Vec3> seatAt)
    {
        var (nav, _) = SeedEngageTests.Layout();
        var setup = RunSetup.Load(OracleFixtures.PathOf("hollow-vault-k2.setup.json"), 1);
        var choir = setup.Dungeon.Pulls.Single(p => p.Id == "choir");
        var patrol = Corridor(choir.Spawns[0]);
        var party = setup.Party.Take(3).Select((m, i) => m with { Pos = seatAt(i), Yaw = 0 }).ToList();
        var content = setup.Content with { Layout = nav, Bounds = nav.Extent, Patrols = new[] { patrol } };
        return (RunRoot.CreateRun(1, party, setup.Dungeon, content, setup.Options), patrol);
    }

    // The party at the layout start (the Entrance, x 4000), seats 1200 mm apart on z: far from corridor-2.
    private static Vec3 AtEntrance(int seat) => new(4000, 0, (seat - 1) * 1200);

    // The party in corridor-2 beside the patrol's first point (32500,0): seat 1 is 2500 mm from it, inside the 4000 mm ring.
    private static Vec3 InCorridor(int seat) => new(30000, 0, (seat - 1) * 1200);

    [Fact(DisplayName = "Patrol (MPLUS-PATROLS): the patrol point starts at its first waypoint, reaches the other after 120 ticks and returns after 240; it stays on the axis between them")]
    public void Point_walks_the_loop_and_oscillates_between_the_endpoints()
    {
        var (_, patrol) = RunWithPatrol(AtEntrance);
        Assert.Equal(new Vec3(32500, 0, 0), Patrols.PointAt(patrol, 0));
        Assert.Equal(new Vec3(29500, 0, 0), Patrols.PointAt(patrol, 60));
        Assert.Equal(new Vec3(26500, 0, 0), Patrols.PointAt(patrol, 120));
        Assert.Equal(new Vec3(32500, 0, 0), Patrols.PointAt(patrol, 240));
        Assert.Equal(new Vec3(26500, 0, 0), Patrols.PointAt(patrol, 360));
        for (var t = 0; t <= 2400; t++)
        {
            var p = Patrols.PointAt(patrol, t);
            Assert.Equal(0, p.Z);
            Assert.InRange(p.X, 26500, 32500);
        }
    }

    [Fact(DisplayName = "Patrol (MPLUS-PATROLS): over 2400 ticks every member stays inside corridor-2 and never in a room")]
    public void Patrol_never_leaves_its_corridor()
    {
        var (_, patrol) = RunWithPatrol(AtEntrance);
        for (var t = 0; t <= 2400; t++)
        {
            foreach (var spawn in patrol.Spawns)
            {
                var at = Patrols.MemberAt(patrol, spawn, t);
                Assert.InRange(at.X, CorridorMinX, CorridorMaxX);
                Assert.InRange(at.Z, -CorridorHalfZ, CorridorHalfZ);
            }
        }
    }

    [Fact(DisplayName = "Patrol (MPLUS-PATROLS): the members stand idle on the patrol point from tick 0 and walk it on every tick, with the party at the Entrance nothing engages")]
    public void Members_stand_idle_on_the_loop_and_walk_it_with_nothing_engaged()
    {
        var (run, patrol) = RunWithPatrol(AtEntrance);
        foreach (var spawn in patrol.Spawns)
        {
            var a = run.State.Actors[spawn.ActorId];
            Assert.True(a.Idle, $"member {spawn.ActorId} is not idle at tick 0");
            Assert.Equal(Patrols.MemberAt(patrol, spawn, 0), a.Pos);
        }
        for (var i = 0; i < 1000; i++)
        {
            var stepped = RunRoot.StepRun(run, Array.Empty<Command>());
            run = stepped.Run;
            Assert.DoesNotContain(stepped.Events, e => e.Type is "pack_engage" or "pull_start");
            foreach (var spawn in patrol.Spawns)
            {
                var a = run.State.Actors[spawn.ActorId];
                Assert.True(a.Idle, $"tick {run.State.Tick}: member {spawn.ActorId} engaged with the party at the Entrance");
                Assert.Equal(Patrols.MemberAt(patrol, spawn, run.State.Tick), a.Pos);
            }
        }
    }

    [Fact(DisplayName = "Patrol (MPLUS-PATROLS): a party member inside the patrol's aggro ring engages the whole patrol as one pack_engage on that tick")]
    public void A_party_member_in_the_patrol_ring_engages_it_as_a_pack()
    {
        var (run, patrol) = RunWithPatrol(InCorridor);
        var stepped = RunRoot.StepRun(run, Array.Empty<Command>());
        var engage = Assert.Single(stepped.Events.Where(e => e.Type == "pack_engage"));
        Assert.Equal("corridor-patrol", ((JsString)engage.Get("packId")!).Value);
        foreach (var spawn in patrol.Spawns)
        {
            Assert.False(stepped.Run.State.Actors[spawn.ActorId].Idle, $"member {spawn.ActorId} did not engage with the patrol");
        }
        Assert.DoesNotContain(stepped.Events, e => e.Type == "pull_start");
    }

    [Fact(DisplayName = "Patrol (MPLUS-PATROLS): the party outside the ring while the patrol walks past does not engage it")]
    public void A_party_outside_the_ring_does_not_engage_the_patrol()
    {
        var (run, _) = RunWithPatrol(AtEntrance);
        var events = new List<SimEvent>();
        for (var i = 0; i < 240; i++)
        {
            var stepped = RunRoot.StepRun(run, Array.Empty<Command>());
            events.AddRange(stepped.Events);
            run = stepped.Run;
        }
        Assert.DoesNotContain(events, e => e.Type == "pack_engage");
    }
}
