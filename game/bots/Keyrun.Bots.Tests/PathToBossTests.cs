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
/// PATH-TO-BOSS (STATE.md D44; Matt 2026-10-21, verbatim: "no more gates, just path to boss with packs as you said, make
/// the rest of the decisions and we will tune later"; design/BIBLE.md section 9.1, M+ design). On the shipped layout
/// (game/content/data/layout.json) the party walks one forward path to the boss with every pack prespawned along it:
/// (1) no room gate shuts behind an engage and no corridor is walled until a pull clears; (2) the run wipes when no
/// living player who is actually in the run remains, so an abandoned or unfilled seat never holds a dead party's run
/// open, in or out of combat; (3) the bots follow the lowest-seat LIVING player, so a dead leader passes the lead on.
/// </summary>
public sealed class PathToBossTests
{
    private const long Seed = 1;
    private const int WardenId = 1;
    private const int ChanterId = 2;
    private const int CinderId = 3;
    private const int WalkTicks = 800;

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test with xUnit's output sink.</summary>
    /// <param name="output">The sink.</param>
    public PathToBossTests(ITestOutputHelper output) => _output = output;

    private static (Run Run, NavLayout Nav, KitRegistry Kits) Setup()
    {
        var registry = ContentLoader.Load();
        var layoutDef = registry.Layout;
        var nav = NavLayouts.ToSim(layoutDef);
        var setup = RunSetup.Load(Path.Combine(RolesOracleTraceTests.OracleDir(), "hollow-vault-k2.setup.json"), Seed);
        SimDungeon dungeon = NavLayouts.PlaceDungeon(layoutDef, setup.Dungeon);
        var party = NavLayouts.PlaceParty(layoutDef, setup.Party.Take(3).ToList(), dungeon.Pulls[0].Id);
        var content = setup.Content with { Layout = nav, Bounds = nav.Extent };
        var run = RunRoot.CreateRun(Seed, party, dungeon, content, setup.Options);
        return (run, nav, KitRegistry.From(registry));
    }

    private static BotView View(Run r, KitRegistry kits) =>
        BotViews.BuildBotView(r.State, r.Encounter.Telegraphs.Select(BotViews.ToBotTelegraph), ClassKitView.Empty, kits) with { Layout = r.Content.Layout };

    private static Bounds Box(NavLayout nav, string id) => nav.Boxes.Single(b => b.Id == id).Box;

    private static void Kill(Run run, int id)
    {
        run.State.Actors[id].Health = 0;
        run.State.Actors[id].Alive = false;
    }

    private static MoveCommand Walk(Run run, int id, long dx) => new(run.State.Tick, id, new Dir2(dx, 0), 0);

    [Fact(DisplayName = "Path to boss: no gate shuts on an engage, so the tank walks back out of the Choir's room mid-fight")]
    public void No_gate_holds_the_party_in_an_engaged_room()
    {
        var (run, nav, _) = Setup();
        var room = Box(nav, "choir-room");
        var warden = run.State.Actors[WardenId];
        warden.MaxHealth = 1_000_000;
        warden.Health = 1_000_000;
        // Inside the Choir's 4000 mm aggro ring (room origin 21500, 0): the first step engages it.
        warden.Pos = new Vec3(19000, 0, 0);
        var events = new List<SimEvent>();
        var engaged = RunRoot.StepRun(run, Array.Empty<Command>());
        events.AddRange(engaged.Events);
        run = engaged.Run;
        Assert.Contains(events, e => e.Type == "pull_start" && ((JsString)e.Get("pullId")!).Value == "choir");

        for (var i = 0; i < 200 && Movement.Contains(room, run.State.Actors[WardenId].Pos); i++)
        {
            var s = RunRoot.StepRun(run, new Command[] { Walk(run, WardenId, -1000) });
            events.AddRange(s.Events);
            run = s.Run;
        }
        var at = run.State.Actors[WardenId].Pos;
        _output.WriteLine($"tick {run.State.Tick}: warden at ({at.X},{at.Z}); choir engaged={Pulls.IsEngaged(run.Encounter, "choir")}");
        Assert.True(Pulls.IsEngaged(run.Encounter, "choir"));
        Assert.True(run.State.Actors.Values.Any(a => a.Kind == ActorKind.Enemy && a.Alive && !a.Idle), "the Choir fight ended before the walk out");
        Assert.False(Movement.Contains(room, at), $"the tank is still inside choir-room at ({at.X},{at.Z}): a gate held him in");
        Assert.DoesNotContain(events, e => e.Type is "gate_closed" or "gate_open");
    }

    [Fact(DisplayName = "Path to boss: the corridor past an uncleared pack is open; the path is the layout, not a wall")]
    public void The_corridor_past_an_uncleared_pack_is_walkable()
    {
        var (run, nav, _) = Setup();
        var corridor = Box(nav, "corridor-2");
        // Just inside corridor-2 past the Choir room (max x 26000), clear of the idle Choir's ring (4000 mm around x 21500).
        run.State.Actors[WardenId].Pos = new Vec3(26500, 0, 0);
        for (var i = 0; i < 80 && run.State.Actors[WardenId].Pos.X <= corridor.MaxX; i++)
        {
            run = RunRoot.StepRun(run, new Command[] { Walk(run, WardenId, 1000) }).Run;
        }
        var at = run.State.Actors[WardenId].Pos;
        _output.WriteLine($"tick {run.State.Tick}: warden at ({at.X},{at.Z}); pull index {run.Encounter.PullIndex}");
        Assert.Equal(0, run.Encounter.PullIndex);
        Assert.False(Pulls.IsEngaged(run.Encounter, "choir"), "the Choir engaged: the walk did not start clear of its ring");
        Assert.True(at.X > corridor.MaxX, $"the tank never walked through corridor-2 (at {at.X}, end {corridor.MaxX}) with the Choir uncleared");
    }

    [Fact(DisplayName = "Path to boss: with every seated player dead, an abandoned seat standing out of combat does not hold the wipe open")]
    public void An_abandoned_seat_out_of_combat_never_holds_a_dead_party_wipe_open()
    {
        // R-BACKFILL-2 (f-47a7a6db): the wipe counts despite the abandoned Chanter, and it releases the seated dead
        // (respawn at the entrance after RunWiring.ReleaseDelayTicks) instead of ending the run after the wipe grace.
        var (run, _, _) = Setup();
        var opts = new StepRunOpts(OpenSeats: new[] { ChanterId });
        Kill(run, WardenId);
        Kill(run, CinderId);
        var wipeStart = run.State.Tick + 1;
        int? respawned = null;
        for (var i = 0; i < RunWiring.ReleaseDelayTicks + 20 && respawned is null; i++)
        {
            var s = RunRoot.StepRun(run, Array.Empty<Command>(), opts);
            run = s.Run;
            Assert.DoesNotContain(s.Events, e => e.Type == "run_end");
            if (run.State.Tick == wipeStart)
            {
                Assert.Equal(wipeStart, run.RunEnd.WipeStartTick);
            }
            respawned = s.Events.FirstOrDefault(e => e.Type == "respawn")?.Tick;
        }
        Assert.True(respawned is not null, $"no respawn within {RunWiring.ReleaseDelayTicks + 20} ticks of the wipe at {wipeStart}");
        Assert.Equal(wipeStart + RunWiring.ReleaseDelayTicks, respawned);
        Assert.Null(run.RunEnd.Result);
        Assert.True(run.State.Actors[WardenId].Alive);
        Assert.True(run.State.Actors[CinderId].Alive);
        Assert.True(run.State.Actors[ChanterId].Alive);
        Assert.Empty(run.Encounter.Engaged);
    }

    [Fact(DisplayName = "Path to boss control: a living seated player keeps the run going, in or out of a room")]
    public void A_living_seated_player_is_not_a_wipe()
    {
        var (run, _, _) = Setup();
        Kill(run, WardenId);
        Kill(run, CinderId);
        for (var i = 0; i < RunWiring.WipeGraceTicks * 3; i++)
        {
            var s = RunRoot.StepRun(run, Array.Empty<Command>());
            Assert.DoesNotContain(s.Events, e => e.Type == "run_end");
            run = s.Run;
        }
        Assert.Null(run.RunEnd.Result);
        Assert.Null(run.RunEnd.WipeStartTick);
    }

    [Fact(DisplayName = "Path to boss: a dead leader passes the lead to the lowest living seat, which walks the path, and the party follows it")]
    public void A_dead_leader_passes_the_lead_to_the_lowest_living_seat()
    {
        var (run, nav, kits) = Setup();
        var room = Box(nav, "choir-room");
        Kill(run, WardenId);

        Assert.Equal(ChanterId, Follow.Leader(run.State)?.Id);
        Assert.NotNull(Follow.LeadCommand(View(run, kits), run.State.Actors[ChanterId]));
        Assert.Null(Follow.LeadCommand(View(run, kits), run.State.Actors[CinderId]));

        var cinderStart = run.State.Actors[CinderId].Pos;
        for (var i = 0; i < WalkTicks && !(Movement.Contains(room, run.State.Actors[ChanterId].Pos) && Movement.Contains(room, run.State.Actors[CinderId].Pos)); i++)
        {
            var view = View(run, kits);
            var commands = new List<Command>();
            if (Follow.LeadCommand(view, run.State.Actors[ChanterId]) is { } lead)
            {
                commands.Add(lead);
            }
            if (Follow.FollowCommand(view, run.State.Actors[CinderId]) is { } follow)
            {
                commands.Add(follow);
            }
            run = RunRoot.StepRun(run, commands).Run;
        }
        var chanter = run.State.Actors[ChanterId].Pos;
        var cinder = run.State.Actors[CinderId].Pos;
        _output.WriteLine($"tick {run.State.Tick}: chanter ({chanter.X},{chanter.Z}), cinder ({cinder.X},{cinder.Z}) from ({cinderStart.X},{cinderStart.Z})");
        Assert.True(Movement.Contains(room, chanter), $"the new leader (seat {ChanterId}) never walked the path into choir-room: ({chanter.X},{chanter.Z})");
        Assert.True(Movement.Contains(room, cinder), $"seat {CinderId} never followed the new leader into choir-room: ({cinder.X},{cinder.Z})");
    }

    [Fact(DisplayName = "Path to boss: with every player dead there is no leader")]
    public void No_leader_when_every_player_is_dead()
    {
        var (run, _, _) = Setup();
        foreach (var a in run.State.Actors.Values.Where(a => a.Kind == ActorKind.Player))
        {
            a.Health = 0;
            a.Alive = false;
        }
        Assert.Null(Follow.Leader(run.State));
    }
}
