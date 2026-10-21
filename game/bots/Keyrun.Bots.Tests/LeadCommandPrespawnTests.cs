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
/// BOTS-ENGAGE-CHOIR x PRESPAWN-AGGRO: the bot leader seat (<see cref="Follow.LeadCommand"/>) must treat an idle
/// prespawned group as "no fight yet" (no ENGAGED enemy lives), and must hold at the pull room's hold point while the
/// Choir stands idle in its room and the party is still outside, exactly as the scripted leader does
/// (<see cref="LeadRouteHoldTests"/>, LEADER-WAITS re-derived for PRESPAWN-AGGRO). Before this, LeadCommand held only
/// in a room with no enemy body at all, so with every group prespawned it walked straight through the hold into the
/// Choir's ring and engaged it alone while the party stood at the Entrance.
/// </summary>
public sealed class LeadCommandPrespawnTests
{
    private const long Seed = 1;
    private const int LeaderId = 1;

    /// <summary>The designed hold (LEADER-WAITS): 4000 mm past the entry doorway. Literal on purpose.</summary>
    private const long DesignedHoldMm = 4000;

    private const int AloneTicks = 600;
    private const int CatchUpTicks = 600;
    private const int ReleaseTicks = 100;

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test with xUnit's output sink.</summary>
    /// <param name="output">The sink.</param>
    public LeadCommandPrespawnTests(ITestOutputHelper output) => _output = output;

    private static long Progress(Vec3 pos, Vec3 door, Vec3 far)
    {
        var dx = far.X - door.X;
        var dz = far.Z - door.Z;
        var length = Movement.Isqrt((dx * dx) + (dz * dz));
        return (((pos.X - door.X) * dx) + ((pos.Z - door.Z) * dz)) / length;
    }

    [Fact(DisplayName = "Bot lead seat with prespawn: holds past the Choir doorway while the Choir stands idle and the party is out, then engages it first")]
    public void Bot_leader_holds_at_an_idle_pull_until_the_party_is_inside()
    {
        var registry = ContentLoader.Load();
        var layoutDef = registry.Layout;
        var nav = NavLayouts.ToSim(layoutDef);
        var kits = KitRegistry.From(registry);
        var setup = RunSetup.Load(Path.Combine(RolesOracleTraceTests.OracleDir(), "hollow-vault-k2.setup.json"), Seed);
        SimDungeon dungeon = NavLayouts.PlaceDungeon(layoutDef, setup.Dungeon);
        var party = NavLayouts.PlaceParty(layoutDef, setup.Party.Take(3).ToList(), dungeon.Pulls[0].Id);
        var content = setup.Content with { Layout = nav, Bounds = nav.Extent };
        var run = RunRoot.CreateRun(Seed, party, dungeon, content, setup.Options);
        Assert.True(Pulls.IsSpawned(run.State, dungeon, 0), "pull 0 does not stand in its room at tick 0 (PRESPAWN-AGGRO)");
        Assert.False(Pulls.IsEngaged(run.Encounter, dungeon, 0), "pull 0 engaged at tick 0");

        BotView View(Run r) =>
            BotViews.BuildBotView(r.State, r.Encounter.Telegraphs.Select(BotViews.ToBotTelegraph), ClassKitView.Empty, kits) with { Layout = r.Content.Layout };

        var room = nav.RoomFor(dungeon.Pulls[0].Id)!;
        var k = nav.Boxes.ToList().FindIndex(b => string.Equals(b.Id, room.Id, StringComparison.Ordinal));
        Assert.True(k > 0);
        var door = nav.Route[k - 1];
        var far = nav.Route[k];

        // Phase 1: the bot leader walks alone; seats 2 and 3 stand at the Entrance. The idle Choir is not a fight, so
        // the leader travels, and it holds at the hold point instead of walking into the ring.
        var events = new List<SimEvent>();
        long maxProgress = long.MinValue;
        for (var i = 0; i < AloneTicks; i++)
        {
            var commands = new List<Command>();
            if (Follow.LeadCommand(View(run), run.State.Actors[LeaderId]) is { } walk)
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
        _output.WriteLine($"alone {AloneTicks} ticks: leader {held} mm past {room.Id}'s doorway (max {maxProgress})");
        Assert.DoesNotContain(events, e => e.Type == "pull_start");
        Assert.True(Movement.Contains(room.Box, leader.Pos), $"the bot leader at {leader.Pos} never entered {room.Id}");
        Assert.True(Math.Abs(held - DesignedHoldMm) <= Follow.ArriveMm, $"the bot leader holds {held} mm past the doorway, want {DesignedHoldMm} +- {Follow.ArriveMm}");
        Assert.Null(Follow.LeadCommand(View(run), leader));

        // Phase 2: the trailing seats walk in; nothing engages while they do.
        var trailing = new[] { 2, 3 };
        var inside = false;
        for (var i = 0; i < CatchUpTicks && !inside; i++)
        {
            var commands = new List<Command>();
            foreach (var id in trailing.Where(id => !Movement.Contains(room.Box, run.State.Actors[id].Pos)))
            {
                if (Follow.WalkRoute(run.State.Tick, run.State.Actors[id], nav) is { } step)
                {
                    commands.Add(step);
                }
            }
            if (Follow.LeadCommand(View(run), run.State.Actors[LeaderId]) is { } walk)
            {
                commands.Add(walk);
            }
            var stepped = RunRoot.StepRun(run, commands);
            run = stepped.Run;
            Assert.DoesNotContain(stepped.Events, e => e.Type == "pull_start");
            inside = NavLayout.PartyInside(run.State, room.Box);
        }
        Assert.True(inside, $"seats 2 and 3 never both entered {room.Id}");

        // Phase 3: the party is in, the Choir is still idle (no engaged enemy), so the leader walks on and engages it first.
        Assert.NotNull(Follow.LeadCommand(View(run), run.State.Actors[LeaderId]));
        int? startTick = null;
        for (var i = 0; i < ReleaseTicks && startTick is null; i++)
        {
            var commands = new List<Command>();
            if (Follow.LeadCommand(View(run), run.State.Actors[LeaderId]) is { } walk)
            {
                commands.Add(walk);
            }
            var stepped = RunRoot.StepRun(run, commands);
            run = stepped.Run;
            if (stepped.Events.FirstOrDefault(e => e.Type == "pull_start") is { } start)
            {
                startTick = start.Tick;
            }
        }
        Assert.NotNull(startTick);
        var center = Pulls.RingCenter(dungeon.Pulls[0]);
        var radius = dungeon.Pulls[0].AggroMm;
        Assert.True(Aggro.InRing(run.State.Actors[LeaderId].Pos, center, radius), "the bot leader is not in the Choir's ring at its engage");
        Assert.All(trailing, id => Assert.False(Aggro.InRing(run.State.Actors[id].Pos, center, radius), $"seat {id} stood in the Choir's ring before the leader"));

        // In the fight (an engaged enemy lives) the lead seat yields to its role.
        Assert.Null(Follow.LeadCommand(View(run), run.State.Actors[LeaderId]));
    }
}
