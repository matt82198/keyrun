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
/// LEADER-DEATH (BACKFILL-GATE, STATE.md D27; design/BIBLE.md sections 1 and 10, backfill instead of abandonment): the
/// party's leader is the lowest player seat, alive or dead, the same seat <see cref="Follow.LeadCommand"/> leads from. When
/// the leader falls, the next living seat must not become a leader that walks nowhere: the other bots regroup on the
/// fallen leader's body. Before this, <see cref="Follow.Leader"/> took the lowest LIVING seat, so seat 2 led itself with
/// no route (LeadCommand keeps the dead seat 1) and a refilled joiner standing outside the party's room never walked in.
/// </summary>
public sealed class LeaderDeathTests
{
    private const long Seed = 1;
    private const int LeaderId = 1;
    private const int BotId = 2;
    private const int WalkTicks = 600;

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test with xUnit's output sink.</summary>
    /// <param name="output">The sink.</param>
    public LeaderDeathTests(ITestOutputHelper output) => _output = output;

    private static (Run Run, NavLayout Nav, NavBox Room, int K, KitRegistry Kits) Setup()
    {
        var registry = ContentLoader.Load();
        var layoutDef = registry.Layout;
        var nav = NavLayouts.ToSim(layoutDef);
        var setup = RunSetup.Load(Path.Combine(RolesOracleTraceTests.OracleDir(), "hollow-vault-k2.setup.json"), Seed);
        SimDungeon dungeon = NavLayouts.PlaceDungeon(layoutDef, setup.Dungeon);
        var party = NavLayouts.PlaceParty(layoutDef, setup.Party.Take(3).ToList(), dungeon.Pulls[0].Id);
        var content = setup.Content with { Layout = nav, Bounds = nav.Extent };
        var run = RunRoot.CreateRun(Seed, party, dungeon, content, setup.Options);
        var room = nav.RoomFor(dungeon.Pulls[0].Id)!;
        var k = nav.Boxes.ToList().FindIndex(b => string.Equals(b.Id, room.Id, StringComparison.Ordinal));
        return (run, nav, room, k, KitRegistry.From(registry));
    }

    private static BotView View(Run r, KitRegistry kits) =>
        BotViews.BuildBotView(r.State, r.Encounter.Telegraphs.Select(BotViews.ToBotTelegraph), ClassKitView.Empty, kits) with { Layout = r.Content.Layout };

    [Fact(DisplayName = "Leader death: a fallen leader stays the party's leader, and a living bot outside his room walks in to him")]
    public void A_bot_regroups_on_a_fallen_leader_instead_of_leading_itself()
    {
        var (run, nav, room, k, kits) = Setup();
        Assert.True(k > 0);
        Assert.False(Follow.AnyLivingEnemy(run.State), "the Choir is engaged at tick 0; the regroup walk needs no fight");

        // The leader fell inside the Choir's room, at the LEADER-WAITS hold point (outside the Choir's aggro ring).
        var fallen = run.State.Actors[LeaderId];
        fallen.Pos = Follow.HoldPoint(nav, k);
        fallen.Health = 0;
        fallen.Alive = false;
        Assert.False(Movement.Contains(room.Box, run.State.Actors[BotId].Pos), $"seat {BotId} starts inside {room.Id}");

        var leader = Follow.Leader(run.State);
        Assert.NotNull(leader);
        Assert.Equal(LeaderId, leader!.Id);
        Assert.Null(Follow.LeadCommand(View(run, kits), run.State.Actors[BotId]));

        for (var i = 0; i < WalkTicks && !Movement.Contains(room.Box, run.State.Actors[BotId].Pos); i++)
        {
            var commands = new List<Command>();
            if (Follow.FollowCommand(View(run, kits), run.State.Actors[BotId]) is { } walk)
            {
                commands.Add(walk);
            }
            run = RunRoot.StepRun(run, commands).Run;
        }
        var bot = run.State.Actors[BotId];
        _output.WriteLine($"tick {run.State.Tick}: seat {BotId} at ({bot.Pos.X},{bot.Pos.Z}), fallen leader at ({fallen.Pos.X},{fallen.Pos.Z})");
        Assert.True(Movement.Contains(room.Box, bot.Pos), $"seat {BotId} at ({bot.Pos.X},{bot.Pos.Z}) never walked into {room.Id} to its fallen leader within {WalkTicks} ticks");
    }

    [Fact(DisplayName = "Leader death: with every player dead there is no leader")]
    public void No_leader_when_every_player_is_dead()
    {
        var (run, _, _, _, _) = Setup();
        foreach (var a in run.State.Actors.Values.Where(a => a.Kind == ActorKind.Player))
        {
            a.Health = 0;
            a.Alive = false;
        }
        Assert.Null(Follow.Leader(run.State));
    }
}
