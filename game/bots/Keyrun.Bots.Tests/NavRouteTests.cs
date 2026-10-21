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
/// driven like the autorun Warden: it walks the route while no engaged enemy lives and fights as the tank bot otherwise.
/// Every enemy has 1 health so the run measures travel, not tuning (HudSceneTest run C plays the real fight). Under
/// PRESPAWN-AGGRO (BIBLE section 9.1) every group stands idle in its room from tick 0 and engages when a party member
/// reaches its aggro ring; the bots pull on purpose, the tank first.
/// </summary>
public sealed class NavRouteTests
{
    private const long Seed = 1;
    private const int LeaderId = 1;
    private const int MaxTicks = 6000;

    private static readonly string[] PullRooms = { "choir-room", "pair-hall", "seed-chamber", "warden-arena" };

    private sealed record PullStart(int Tick, Dictionary<int, Vec3> Party, Vec3 Center, long AggroMm, bool InRingBefore, bool InRingAfter, Vec3 LeaderPos);

    private sealed record RouteRun(List<SimEvent> Events, List<PullStart> PullStarts, Run Final);

    private static bool AnyInRing(SimState state, Vec3 center, long radius) =>
        state.Actors.Values.Any(a => a.Kind == ActorKind.Player && a.Alive && Aggro.InRing(a.Pos, center, radius));

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
        var starts = new List<PullStart>();
        Assert.Empty(run.Encounter.Engaged);

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
                var center = Pulls.RingCenter(pull);
                var partyNow = run.State.Actors.Values.Where(a => a.Kind == ActorKind.Player && a.Alive).ToDictionary(a => a.Id, a => a.Pos);
                starts.Add(new PullStart(e.Tick, partyNow, center, pull.AggroMm,
                    AnyInRing(before.State, center, pull.AggroMm), AnyInRing(run.State, center, pull.AggroMm), run.State.Actors[LeaderId].Pos));
            }
        }
        return new RouteRun(events, starts, run);
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

    [Fact(DisplayName = "Nav route: every pull engages on its aggro ring, never before, with the tank in it, and the run is cleared")]
    public void Every_pull_engages_on_its_aggro_ring_never_before()
    {
        var r = Route.Value;
        Assert.Equal("timed", r.Final.RunEnd.Result?.ToJs());
        Assert.Equal(r.Final.Dungeon.Pulls.Count, r.Final.Encounter.PullIndex);
        Assert.Equal(4, r.PullStarts.Count);
        Assert.NotEqual(0, r.PullStarts[0].Tick);
        var entries = FirstEntries(r.Events);
        for (var i = 0; i < r.PullStarts.Count; i++)
        {
            var s = r.PullStarts[i];
            if (PullRooms[i] == "seed-chamber")
            {
                // KF3 Seed spawn gate (BIBLE section 9.6): the Seed activates on the tick the tank enters its room, not on its ring.
                Assert.True(entries.TryGetValue((LeaderId, "seed-chamber"), out var roomEntry) && roomEntry == s.Tick,
                    $"pull_start at tick {s.Tick}: the Seed did not activate on the tank's entry into seed-chamber ({roomEntry})");
                continue;
            }
            Assert.False(s.InRingBefore, $"pull_start at tick {s.Tick}: a party member was already in the aggro ring the tick before");
            Assert.True(s.InRingAfter, $"pull_start at tick {s.Tick}: no party member stands in the aggro ring");
            Assert.True(Aggro.InRing(s.LeaderPos, s.Center, s.AggroMm), $"pull_start at tick {s.Tick}: the tank (leader) at {s.LeaderPos} is not in the ring; a follower pulled it");
        }
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

    // PRESPAWN-AGGRO: nothing appears on the party. A group engages on the tick the first member walks into its ring, so
    // at pull_start every living party member stands on or outside the ring's edge, less at most one tick's walk
    // (run.json speedMmPerTick 180, so 2 x 180 mm of slack covers a member whose last step was diagonal or pushed).
    [Fact(DisplayName = "Nav route: at every engage no party member stands deeper than one step inside the aggro ring")]
    public void No_group_engages_with_a_party_member_deep_inside_its_ring()
    {
        const long StepSlackMm = 2 * 180;
        var r = Route.Value;
        Assert.NotEmpty(r.PullStarts);
        foreach (var s in r.PullStarts)
        {
            foreach (var (id, pos) in s.Party)
            {
                var d = Distance(s.Center, pos);
                _output.WriteLine($"tick {s.Tick}: actor {id} at {pos.X},{pos.Z} is {d} mm from the ring centre {s.Center.X},{s.Center.Z} (radius {s.AggroMm})");
                Assert.True(d >= s.AggroMm - StepSlackMm, $"tick {s.Tick}: party actor {id} at {pos} stands {d} mm from the ring centre {s.Center}, deep inside the {s.AggroMm} mm ring");
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
