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
/// PARTY-TOGETHER (KF2c; design/BIBLE.md sections 4 and 9: every pull, the party lands together). Run C's shape on the
/// real layout: the hollow-vault k2 oracle setup placed into its rooms, the host's three seats (Warden 1, Chanter 2,
/// Cinder 3) at the Entrance, the real bot roles plus <see cref="Follow"/> on seats 2 and 3, and seat 1 driven like the
/// autorun Warden outside the Choir: it walks the route while no enemy lives, and in a fight it holds where it stands
/// (the tank role's casts kept, its moves dropped), just inside the trigger it crossed. Enemy health is capped at
/// <see cref="HealthCap"/> so the party clears all four pulls in a test-sized run while every fight still outlasts
/// <see cref="WithinTicks"/>.
/// </summary>
public sealed class PartyTogetherTests
{
    /// <summary>Every living bot must stand in the pull's room this many ticks after its <c>pull_start</c> (3 s at 20 Hz).</summary>
    public const int WithinTicks = 60;

    private const long Seed = 1;
    private const int LeaderId = 1;
    private const int MaxTicks = 12000;
    private const int HealthCap = 1500;
    private static readonly int[] BotIds = { 2, 3 };

    private static readonly Dictionary<string, string> RoomOfPull = new(StringComparer.Ordinal)
    {
        ["choir"] = "choir-room",
        ["pair"] = "pair-hall",
        ["seed"] = "seed-chamber",
        ["boss"] = "warden-arena",
    };

    private sealed record Check(string PullId, int StartTick, bool Fighting, Dictionary<int, Vec3> Bots);

    private sealed record Drive(List<Check> Checks, List<SimEvent> Events, NavLayout Nav, Run Final);

    private static readonly Lazy<Drive> Driven = new(DriveRun);

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test with xUnit's output sink (the room_enter ticks are printed for the PR).</summary>
    /// <param name="output">The sink.</param>
    public PartyTogetherTests(ITestOutputHelper output) => _output = output;

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
        var checks = new List<Check>();
        var due = new List<(int Tick, string PullId, int StartTick)>();

        while (run.State.Tick < MaxTicks && run.RunEnd.ResultTick is null)
        {
            var tick = run.State.Tick;
            foreach (var d in due.Where(d => d.Tick == tick).ToList())
            {
                var living = BotIds.Where(id => run.State.Actors.TryGetValue(id, out var a) && a.Alive).ToDictionary(id => id, id => run.State.Actors[id].Pos);
                checks.Add(new Check(d.PullId, d.StartTick, Follow.AnyLivingEnemy(run.State), living));
                due.Remove(d);
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
                    if (Follow.LeadRoute(tick, leader, run) is { } walk)
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

            var stepped = RunRoot.StepRun(run, commands);
            run = stepped.Run;
            events.AddRange(stepped.Events);
            foreach (var e in stepped.Events.Where(e => e.Type == "pull_start"))
            {
                var pullId = ((JsString)e.Get("pullId")!).Value;
                due.Add((e.Tick + WithinTicks, pullId, e.Tick));
            }
        }
        return new Drive(checks, events, nav, run);
    }

    [Fact(DisplayName = "Party together: within 60 ticks of every pull_start each living bot stands in that pull's room, past its doorway")]
    public void Every_living_bot_is_in_the_pull_room_within_60_ticks()
    {
        var r = Driven.Value;
        foreach (var e in r.Events.Where(e => e.Type is "room_enter" or "pull_start" or "pull_clear" or "run_end"))
        {
            _output.WriteLine($"{e.Tick} {e.Type} {(e.Type == "room_enter" ? ((JsInt)e.Get("actorId")!).Value + " " + ((JsString)e.Get("room")!).Value : e.Type == "run_end" ? string.Empty : ((JsString)e.Get("pullId")!).Value)}");
        }
        Assert.Equal(RoomOfPull.Keys.OrderBy(k => k, StringComparer.Ordinal), r.Checks.Select(c => c.PullId).OrderBy(k => k, StringComparer.Ordinal));
        var failures = new List<string>();
        foreach (var c in r.Checks)
        {
            // Non-vacuity: the fight must still be on when the party is checked, or a cleared pull would pass by default.
            Assert.True(c.Fighting, $"pull {c.PullId} (start {c.StartTick}) was over before tick {c.StartTick + WithinTicks}; the check would be vacuous");
            Assert.NotEmpty(c.Bots);
            var room = r.Nav.Boxes.Single(b => b.Id == RoomOfPull[c.PullId]);
            foreach (var (id, pos) in c.Bots)
            {
                var inRoom = Movement.Contains(room.Box, pos);
                var inCorridor = r.Nav.Boxes.Any(b => !b.IsRoom && Movement.Contains(b.Box, pos));
                if (!inRoom || inCorridor)
                {
                    failures.Add($"{c.PullId}: bot {id} at ({pos.X},{pos.Z}) on tick {c.StartTick + WithinTicks} is {(inRoom ? "in the doorway of" : "outside")} {room.Id}");
                }
            }
        }
        Assert.True(failures.Count == 0, string.Join("; ", failures));
    }

    [Fact(DisplayName = "Party together: a slot behind a doorway-holding leader turns to his flank inside the room; a slot already inside is kept")]
    public void Fight_goal_flanks_a_doorway_leader_and_keeps_an_inside_slot()
    {
        var hall = new Bounds(32000, 56000, -6000, 6000);
        var leader = new Vec3(34000, 0, 0);
        // Behind 2500 and side +-1000 (layout.json formation): 3500 out on the slot's side, pulled 2500 inside the walls.
        Assert.Equal(new Vec3(34500, 0, 3500), Follow.FightGoal(hall, leader, new Vec3(31500, 0, 1000)));
        Assert.Equal(new Vec3(34500, 0, -3500), Follow.FightGoal(hall, leader, new Vec3(31500, 0, -1000)));
        var deep = new Vec3(40000, 0, 1000);
        Assert.Equal(deep, Follow.FightGoal(hall, new Vec3(42500, 0, 0), deep));
        Assert.Equal(new Bounds(33500, 54500, -4500, 4500), Follow.Inset(hall, Follow.FightInsetMm));
        Assert.Equal(new Bounds(5, 5, 2500, 7500), Follow.Inset(new Bounds(0, 10, 0, 10000), 2500));
    }
}
