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
using SimDungeon = Keyrun.Sim.Encounter.DungeonDef;

namespace Keyrun.Bots.Tests;

/// <summary>
/// The Hollow Vault route on the real layout (game/content/data/layout.json; STATE.md D18, design/ART-PLAN.md section 3
/// and lane 1): the hollow-vault k2 oracle setup's dungeon placed into its rooms, the host's three seats (Warden 1,
/// Chanter 2, Cinder 3) at the Entrance, the real bot roles plus <see cref="Follow"/> on seats 2 and 3, and seat 1
/// driven like the autorun Warden: it walks the route while no enemy lives and fights as the tank bot otherwise. Every
/// enemy has 1 health so the run measures travel, not tuning (HudSceneTest run C plays the real fight).
/// </summary>
public sealed class NavRouteTests
{
    private const long Seed = 1;
    private const int LeaderId = 1;
    private const int MaxTicks = 6000;

    private static readonly string[] PullRooms = { "choir-room", "pair-hall", "seed-chamber", "warden-arena" };

    private sealed record RouteRun(
        List<SimEvent> Events,
        List<(int Tick, Dictionary<int, Vec3> Party, IReadOnlyList<Vec3> Spawns, bool InTriggerBefore, bool InTriggerAfter)> PullStarts,
        Run Final);

    private static readonly Lazy<RouteRun> Route = new(Drive);

    private static RouteRun Drive()
    {
        var registry = ContentLoader.Load();
        var layoutDef = registry.Layout;
        var nav = NavLayouts.ToSim(layoutDef);
        var setup = RunSetup.Load(Path.Combine(RolesOracleTraceTests.OracleDir(), "hollow-vault-k2.setup.json"), Seed);
        var weak = setup.Dungeon with
        {
            Pulls = setup.Dungeon.Pulls.Select(p => p with { Spawns = p.Spawns.Select(s => s with { Health = 1 }).ToList() }).ToList(),
        };
        SimDungeon dungeon = NavLayouts.PlaceDungeon(layoutDef, weak);
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
        var starts = new List<(int, Dictionary<int, Vec3>, IReadOnlyList<Vec3>, bool, bool)>();
        if (Pulls.IsSpawned(run.State, dungeon, 0))
        {
            // createRun spawned pull 0 at tick 0 (it has no pull_start): the flat-arena behaviour this lane replaces.
            var at0 = run.State.Actors.Values.Where(a => a.Kind == ActorKind.Player).ToDictionary(a => a.Id, a => a.Pos);
            starts.Add((0, at0, dungeon.Pulls[0].Spawns.Select(s => s.Pos).ToList(), true, true));
        }

        // Past run_end the sim keeps stepping (the bots still walk), so the followers can reach the arena the boss died in.
        while (run.State.Tick < MaxTicks && (run.RunEnd.ResultTick is not { } endTick || run.State.Tick < endTick + 600))
        {
            var tick = run.State.Tick;
            // BotCommandSource's view: the run's layout rides along, so the bots travel.
            var view = BotViews.BuildBotView(run.State, run.Encounter.Telegraphs.Select(BotViews.ToBotTelegraph), ClassKitView.Empty, kits) with { Layout = run.Content.Layout };
            var batch = Bot.RunBots(view, bots, mem, prng);
            mem = batch.Memories;
            prng = batch.Prng;
            var commands = batch.Commands.ToList();
            if (!Follow.AnyLivingEnemy(run.State) && run.State.Actors[LeaderId] is { Alive: true } leader)
            {
                // The autorun Warden's travel (ScriptedWarden): the route, holding for the party, not the tank role.
                commands.RemoveAll(c => c.ActorId == LeaderId);
                if (Follow.LeadRoute(tick, leader, run) is { } walk)
                {
                    commands.Add(walk);
                }
            }

            var before = run;
            var stepped = RunRoot.StepRun(run, commands);
            run = stepped.Run;
            events.AddRange(stepped.Events);
            foreach (var e in stepped.Events.Where(e => e.Type == "pull_start"))
            {
                var index = (int)((JsInt)e.Get("pullIndex")!).Value;
                var pull = dungeon.Pulls[index];
                var trigger = nav.RoomFor(pull.Id)!.Box;
                var partyNow = run.State.Actors.Values.Where(a => a.Kind == ActorKind.Player && a.Alive).ToDictionary(a => a.Id, a => a.Pos);
                starts.Add((e.Tick, partyNow, pull.Spawns.Select(s => s.Pos).ToList(),
                    NavLayout.PartyInside(before.State, trigger), NavLayout.PartyInside(run.State, trigger)));
            }
        }
        return new RouteRun(events, starts.Select(s => (s.Item1, s.Item2, s.Item3, s.Item4, s.Item5)).ToList(), run);
    }

    private static long Distance(Vec3 a, Vec3 b) => Movement.Isqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Z - b.Z) * (a.Z - b.Z)));

    private static Dictionary<(int Actor, string Room), int> FirstEntries(IEnumerable<SimEvent> events)
    {
        var first = new Dictionary<(int, string), int>();
        foreach (var e in events.Where(e => e.Type == "room_enter"))
        {
            var key = ((int)((JsInt)e.Get("actorId")!).Value, ((JsString)e.Get("room")!).Value);
            first.TryAdd(key, e.Tick);
        }
        return first;
    }

    [Fact(DisplayName = "Nav route: every pull spawns on room entry, never before, and the run is cleared")]
    public void Every_pull_spawns_on_room_entry_never_before()
    {
        var r = Route.Value;
        Assert.Equal("timed", r.Final.RunEnd.Result?.ToJs());
        Assert.Equal(r.Final.Dungeon.Pulls.Count, r.Final.Encounter.PullIndex);
        Assert.Equal(4, r.PullStarts.Count);
        Assert.NotEqual(0, r.PullStarts[0].Tick);
        foreach (var s in r.PullStarts)
        {
            Assert.False(s.InTriggerBefore, $"pull_start at tick {s.Tick}: a party member was already in the trigger the tick before");
            Assert.True(s.InTriggerAfter, $"pull_start at tick {s.Tick}: no party member stands in the trigger");
        }
        var entries = FirstEntries(r.Events);
        for (var i = 0; i < PullRooms.Length; i++)
        {
            Assert.True(entries.TryGetValue((LeaderId, PullRooms[i]), out var entered), $"the leader never entered {PullRooms[i]}");
            Assert.True(entered <= r.PullStarts[i].Tick, $"pull {i} started at {r.PullStarts[i].Tick} before the leader entered {PullRooms[i]} at {entered}");
            if (i > 0)
            {
                Assert.True(entries[(LeaderId, PullRooms[i - 1])] < entered, $"{PullRooms[i]} entered before {PullRooms[i - 1]}");
            }
        }
    }

    // Every living party member, the scripted leader included (LEADER-WAITS): under STATE.md D27 a pull starts when the
    // LAST living member enters, so the leader holds at the room's standoff point until the party is inside instead of
    // walking on toward the spawns.
    [Fact(DisplayName = "Nav route: no enemy spawns within 6 m of a party member at pull_start")]
    public void No_enemy_spawns_within_6_m_of_a_party_member()
    {
        var r = Route.Value;
        Assert.NotEmpty(r.PullStarts);
        foreach (var s in r.PullStarts)
        {
            foreach (var spawn in s.Spawns)
            {
                foreach (var (id, pos) in s.Party)
                {
                    var d = Distance(spawn, pos);
                    _output.WriteLine($"tick {s.Tick}: actor {id} at {pos.X},{pos.Z} is {d} mm from the spawn at {spawn.X},{spawn.Z}");
                    Assert.True(d >= 6000, $"tick {s.Tick}: an enemy spawned at {spawn}, {d} mm from party actor {id} at {pos}");
                }
            }
        }
    }

    private readonly Xunit.Abstractions.ITestOutputHelper _output;

    public NavRouteTests(Xunit.Abstractions.ITestOutputHelper output) => _output = output;

    [Fact(DisplayName = "Nav route: every bot enters each room within 600 ticks (10 s) of the leader")]
    public void Every_bot_enters_each_room_within_600_ticks_of_the_leader()
    {
        var entries = FirstEntries(Route.Value.Events);
        foreach (var room in PullRooms)
        {
            Assert.True(entries.TryGetValue((LeaderId, room), out var leaderTick), $"the leader never entered {room}");
            _output.WriteLine($"{room}: leader {leaderTick}, bot 2 {(entries.TryGetValue((2, room), out var b2) ? b2 : -1)}, bot 3 {(entries.TryGetValue((3, room), out var b3) ? b3 : -1)}");
            foreach (var bot in new[] { 2, 3 })
            {
                Assert.True(entries.TryGetValue((bot, room), out var botTick), $"bot {bot} never entered {room} (leader at tick {leaderTick})");
                Assert.True(botTick - leaderTick <= 600, $"bot {bot} entered {room} at tick {botTick}, {botTick - leaderTick} ticks after the leader");
            }
        }
    }
}
