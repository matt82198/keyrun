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
/// JOINER-DOORWAY (design/BIBLE.md sections 4 and 10; the room gate of STATE.md D27 is gone since D44). A backfill run
/// driven to its end: seat 2 is abandoned in the Choir, refilled when pair-hall engages and arrives with the party there
/// (BIBLE s9.1/s10 "a joiner arrives at the next pull start ... inside the party's taken branch"; Backfill.Step places it
/// on the staying member nearest the leaver), then follows it through every later doorway. A room entry is one event: the joiner (and every seat) emits
/// <c>room_enter</c> once per room, never flickering across a doorway.
/// </summary>
public sealed class JoinerDoorwayTests
{
    private const long Seed = 1;
    private const int LeaderId = 1;
    private const int JoinerId = 2;
    private const int MaxTicks = 12000;
    // Scenario setup, not an assertion. 1500 -> 1000 (prespawn aggro): the healerless Warden died at 1273 in the Pair,
    // so nobody led the joiner on.
    private const int HealthCap = 1000;
    private const string GatedRoom = "pair-hall";

    /// <param name="Events">Every event of the drive.</param>
    /// <param name="FillTick">The refill's pull start tick, or null.</param>
    /// <param name="Final">The run at the end.</param>
    /// <param name="AbandonPos">Where the leaver stood at the abandon.</param>
    /// <param name="JoinerAtFill">The joiner's position on the fill step's state.</param>
    /// <param name="StayingAtFill">The living staying party members' positions on the fill step's state.</param>
    private sealed record Drive(List<SimEvent> Events, int? FillTick, Run Final, Vec3? AbandonPos, Vec3? JoinerAtFill, List<Vec3> StayingAtFill);

    private static readonly Lazy<Drive> Driven = new(DriveRun);

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test with xUnit's output sink (the room_enter ticks are printed for the PR).</summary>
    /// <param name="output">The sink.</param>
    public JoinerDoorwayTests(ITestOutputHelper output) => _output = output;

    private static Drive DriveRun()
    {
        var registry = ContentLoader.Load();
        var layoutDef = registry.Layout;
        var nav = NavLayouts.ToSim(layoutDef);
        var setup = RunSetup.Load(Path.Combine(RolesOracleTraceTests.OracleDir(), "hollow-vault-k2.setup.json"), Seed);
        var capped = setup.Dungeon with
        {
            Pulls = setup.Dungeon.Pulls.Select(p => p with { Spawns = p.Spawns.Select(s => s with { Health = Math.Min(s.Health, HealthCap) }).ToList() }).ToList(),
        };
        SimDungeon dungeon = NavLayouts.PlaceDungeon(layoutDef, capped);
        var party = NavLayouts.PlaceParty(layoutDef, setup.Party.Take(3).ToList(), dungeon.Pulls[0].Id);
        var content = setup.Content with { Layout = nav, Bounds = nav.Extent };
        var run = RunRoot.CreateRun(Seed, party, dungeon, content, setup.Options);

        var kits = KitRegistry.From(registry);
        var bots = new SortedDictionary<int, IBot>();
        var memories = new SortedDictionary<int, BotMemory>();
        foreach (var p in party)
        {
            bots[p.Id] = Bot.MakeBot(RoleSet.Default);
            memories[p.Id] = BotMemory.Default(80);
        }
        IReadOnlyDictionary<int, BotMemory> mem = memories;
        var prng = Skill.SeedPrng(Seed);
        var events = new List<SimEvent>();
        var book = BackfillState.Empty;
        var queue = new List<QueueRequest> { new(dungeon.Id, run.Encounter.KeyLevel, "none") };
        var abandoned = false;
        int? fillTick = null;
        Vec3? abandonPos = null;
        Vec3? joinerAtFill = null;
        var stayingAtFill = new List<Vec3>();

        while (run.State.Tick < MaxTicks && run.RunEnd.ResultTick is null)
        {
            var tick = run.State.Tick;
            if (!abandoned && run.Encounter.PullIndex == 0 && run.State.Actors.Values.Count(a => a.Alive && a.Kind == ActorKind.Enemy && !a.Idle) == 1)
            {
                // Seat 2 leaves as the Choir's last enemy is fought.
                book = Backfill.Abandon(book, run, JoinerId);
                abandoned = true;
                abandonPos = run.State.Actors[JoinerId].Pos;
            }

            var view = BotViews.BuildBotView(run.State, run.Encounter.Telegraphs.Select(BotViews.ToBotTelegraph), ClassKitView.Empty, kits) with { Layout = run.Content.Layout };
            var batch = Bot.RunBots(view, bots, mem, prng);
            mem = batch.Memories;
            prng = batch.Prng;
            var commands = batch.Commands.ToList();
            if (run.State.Actors[LeaderId] is { Alive: true } leader)
            {
                if (!Follow.AnyLivingEnemy(run.State))
                {
                    // ScriptedWarden's travel: the route, holding for the party, not the tank role.
                    commands.RemoveAll(c => c.ActorId == LeaderId);
                    if (Follow.LeadRoute(tick, leader, run, book.Open.Select(s => s.ActorId).ToList()) is { } walk)
                    {
                        commands.Add(walk);
                    }
                }
                else
                {
                    // ScriptedWarden outside the Choir: he fights where he stands (no approach), casts only.
                    commands.RemoveAll(c => c.ActorId == LeaderId && c is MoveCommand or DashCommand);
                }
            }

            var stepped = Backfill.Step(book, run, commands, queue);
            book = stepped.Backfill;
            foreach (var i in stepped.ConsumedQueueIndices.Reverse())
            {
                queue.RemoveAt(i);
            }
            if (stepped.Fills.Any(f => f.ActorId == JoinerId))
            {
                var filled = stepped.Step.Run.State;
                fillTick = filled.Tick;
                joinerAtFill = filled.Actors[JoinerId].Pos;
                stayingAtFill = filled.Actors.Values.Where(a => a.Kind == ActorKind.Player && a.Alive && a.Id != JoinerId).Select(a => a.Pos).ToList();
            }
            run = stepped.Step.Run;
            events.AddRange(stepped.Step.Events);
        }
        return new Drive(events, fillTick, run, abandonPos, joinerAtFill, stayingAtFill);
    }

    /// <summary>
    /// HudSceneTest run F's pair-hall doorway on <c>gate_open</c> (tick 2332 at seed 1): the Warden stands just past
    /// pair-hall's entry wall (x 32000), the Cinder (seat 3) is pressed against that wall inside the room, and the
    /// refilled Chanter (seat 2) waits outside in corridor-2. The run starts at the seed pull, so the cleared pair-hall and
    /// both corridors are open and no enemy lives: the Warden walks the route on and both bots follow him through.
    /// </summary>
    private static List<SimEvent> DriveDoorway()
    {
        var registry = ContentLoader.Load();
        var layoutDef = registry.Layout;
        var nav = NavLayouts.ToSim(layoutDef);
        var setup = RunSetup.Load(Path.Combine(RolesOracleTraceTests.OracleDir(), "hollow-vault-k2.setup.json"), Seed);
        SimDungeon placed = NavLayouts.PlaceDungeon(layoutDef, setup.Dungeon);
        var dungeon = placed with { Pulls = placed.Pulls.Where(p => p.Id is "seed" or "boss").ToList() };
        var at = new Dictionary<int, Vec3>
        {
            [LeaderId] = new Vec3(33010, 0, -4),
            [JoinerId] = new Vec3(30702, 0, -684),
            [CinderId] = new Vec3(32000, 0, 1706),
        };
        var party = setup.Party.Take(3).Select(m => m with { Pos = at[m.Id] with { Y = m.Pos.Y }, Yaw = 0 }).ToList();
        var content = setup.Content with { Layout = nav, Bounds = nav.Extent };
        var run = RunRoot.CreateRun(Seed, party, dungeon, content, setup.Options);

        var kits = KitRegistry.From(registry);
        var bots = new SortedDictionary<int, IBot>();
        var memories = new SortedDictionary<int, BotMemory>();
        foreach (var p in party.Where(p => p.Id != LeaderId))
        {
            bots[p.Id] = Bot.MakeBot(RoleSet.Default);
            memories[p.Id] = BotMemory.Default(80);
        }
        IReadOnlyDictionary<int, BotMemory> mem = memories;
        var prng = Skill.SeedPrng(Seed);
        var events = new List<SimEvent>();
        // KF3 Seed spawn gate (BIBLE section 9.6): the Seed activates when the tank enters its room, which is before the
        // two followers arrive, so the drive runs until all three seats have entered seed-chamber (or the tick cap),
        // not until the first enemy engages.
        bool AllSeatsInSeedChamber() => new[] { LeaderId, CinderId, JoinerId }.All(id => events.Any(e => e.Type == "room_enter" && KeyOf(e) == (id, "seed-chamber")));
        while (run.State.Tick < DoorwayTicks && run.RunEnd.ResultTick is null && !AllSeatsInSeedChamber())
        {
            var view = BotViews.BuildBotView(run.State, run.Encounter.Telegraphs.Select(BotViews.ToBotTelegraph), ClassKitView.Empty, kits) with { Layout = run.Content.Layout };
            var batch = Bot.RunBots(view, bots, mem, prng);
            mem = batch.Memories;
            prng = batch.Prng;
            var commands = batch.Commands.ToList();
            if (Follow.LeadRoute(run.State.Tick, run.State.Actors[LeaderId], run) is { } walk)
            {
                commands.Add(walk);
            }
            var stepped = RunRoot.StepRun(run, commands);
            run = stepped.Run;
            events.AddRange(stepped.Events);
        }
        return events;
    }

    /// <summary>The doorway drive stops by this tick (or once all three seats stand in seed-chamber): 30 s at 20 Hz.</summary>
    private const int DoorwayTicks = 600;

    private const int CinderId = 3;

    [Fact(DisplayName = "Joiner doorway: at pair-hall's opened gate the joiner enters once and a bot already inside never steps back out to re-enter")]
    public void Bots_at_an_opened_doorway_enter_the_room_once()
    {
        var events = DriveDoorway();
        var enters = events.Where(e => e.Type == "room_enter").ToList();
        foreach (var e in enters)
        {
            _output.WriteLine($"{e.Tick} room_enter {KeyOf(e).Actor} {KeyOf(e).Room}");
        }

        // Non-vacuity: the party walks through pair-hall into seed-chamber, the joiner through pair-hall's doorway.
        Assert.Contains(enters, e => KeyOf(e) == (LeaderId, "seed-chamber"));
        Assert.Contains(enters, e => KeyOf(e) == (CinderId, "seed-chamber"));
        Assert.Contains(enters, e => KeyOf(e) == (JoinerId, "seed-chamber"));

        var joiner = enters.Where(e => KeyOf(e) == (JoinerId, GatedRoom)).Select(e => e.Tick).ToList();
        Assert.True(joiner.Count == 1, $"joiner {JoinerId} room_enter {GatedRoom} x{joiner.Count} at {string.Join(",", joiner)} (want exactly 1)");
        var cinder = enters.Where(e => KeyOf(e) == (CinderId, GatedRoom)).Select(e => e.Tick).ToList();
        Assert.True(cinder.Count == 0, $"seat {CinderId} started inside {GatedRoom} and re-entered it x{cinder.Count} at {string.Join(",", cinder)} (stepped back out through the doorway)");
        var repeats = enters.GroupBy(KeyOf).Where(g => g.Count() > 1).Select(g => $"{g.Key.Actor} {g.Key.Room} x{g.Count()}").ToList();
        Assert.True(repeats.Count == 0, $"room_enter repeats: {string.Join("; ", repeats)}");
    }

    private static (int Actor, string Room) KeyOf(SimEvent e) => ((int)((JsInt)e.Get("actorId")!).Value, ((JsString)e.Get("room")!).Value);

    [Fact(DisplayName = "Joiner doorway: a refilled joiner enters each room once, never flickering room_enter across a doorway")]
    public void Joiner_enters_each_room_once()
    {
        var r = Driven.Value;
        var enters = r.Events.Where(e => e.Type == "room_enter").ToList();
        foreach (var e in r.Events.Where(e => e.Type is "room_enter" or "pull_start" or "pull_clear" or "gate_closed" or "gate_open" or "run_end"))
        {
            _output.WriteLine($"{e.Tick} {e.Type} {(e.Type == "room_enter" ? KeyOf(e).Actor + " " + KeyOf(e).Room : e.Type is "gate_closed" or "gate_open" ? ((JsString)e.Get("room")!).Value : e.Type == "run_end" ? string.Empty : ((JsString)e.Get("pullId")!).Value)}");
        }
        foreach (var e in r.Events.Where(e => e.Type == "death"))
        {
            _output.WriteLine($"{e.Tick} death actor {((JsInt)e.Get("actorId")!).Value}");
        }
        foreach (var a in r.Final.State.Actors.Values.Where(a => a.Kind == ActorKind.Player || (a.Alive && !a.Idle)))
        {
            _output.WriteLine($"final tick {r.Final.State.Tick}: {a.Id} {a.ClassId} alive={a.Alive} idle={a.Idle} hp={a.Health} pos=({a.Pos.X},{a.Pos.Z}) target={a.TargetId}");
        }

        // BIBLE s9.1 and s10: "a joiner arrives at the next pull start ... inside the party's taken branch". The seat is
        // refilled when pair-hall engages, and on that step the joiner stands with the party (on a living staying member),
        // not where the leaver idled in the Choir.
        Assert.NotNull(r.FillTick);
        Assert.NotNull(r.JoinerAtFill);
        Assert.Contains(r.JoinerAtFill!.Value, r.StayingAtFill);
        Assert.NotEqual(r.AbandonPos, r.JoinerAtFill);

        var joined = enters.Where(e => KeyOf(e).Actor == JoinerId && e.Tick >= r.FillTick!.Value).ToList();
        var gated = joined.Count(e => KeyOf(e).Room == GatedRoom);
        Assert.True(gated <= 1,$"joiner {JoinerId} emitted room_enter {GatedRoom} {gated} times after its fill at {r.FillTick}: ticks {string.Join(",", joined.Where(e => KeyOf(e).Room == GatedRoom).Select(e => e.Tick))}");

        var repeats = enters.GroupBy(KeyOf).Where(g => g.Count() > 1).Select(g => $"{g.Key.Actor} {g.Key.Room} x{g.Count()} at {string.Join(",", g.Select(e => e.Tick))}").ToList();
        Assert.True(repeats.Count == 0, $"room_enter repeats: {string.Join("; ", repeats)}");
    }
}
