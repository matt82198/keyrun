using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Keyrun.Tools.Replay;
using Xunit;
using SimDungeon = Keyrun.Sim.Encounter.DungeonDef;

namespace Keyrun.Bots.Tests;

/// <summary>
/// PATH-PACKS on the real layout (finding f-82d9390e; BIBLE section 9.1; Matt 2026-10-20 "no more gates, just path to
/// boss with packs"): the hollow-vault k2 oracle dungeon placed in its rooms, the shipped packs armed the way the host
/// arms them (<see cref="LayoutPackGroups.Build"/>), the three seats driven as in <see cref="NavRouteTests"/> (seat 1
/// the autorun Warden on <see cref="Follow.LeadRoute"/>, seats 2 and 3 the real bots). Every enemy has 1 health, so the
/// run measures the route, not tuning. Walking forward, the party fights a teaching pull, then the required pack on the
/// line as one group, then the next teaching pull, and the boss last; it walks past every optional pack, so the forces
/// bar ends short of 100%: on this route only more packs fill it.
/// </summary>
public sealed class PathPackRouteTests
{
    private const long Seed = 1;
    private const int LeaderId = 1;
    private const int MaxTicks = 8000;

    /// <summary>
    /// The least distance, mm, any party member may stand from an enemy on the tick that enemy first enters the state
    /// (Matt 2026-10-20: "not literally spawning enemies on the player").
    /// </summary>
    private const long MinSpawnClearMm = 5000;

    /// <summary>One actor's first appearance: the tick it entered the state and its squared XZ distance (mm^2) to the nearest party member.</summary>
    private sealed record Appearance(int Tick, int ActorId, long NearestPartyD2);

    private sealed record PackRun(
        List<SimEvent> Events,
        Dictionary<string, IReadOnlyList<int>> MembersAtEngage,
        IReadOnlyList<PackGroup> Packs,
        LayoutDef Layout,
        Run Initial,
        List<Appearance> Appearances,
        Run Final);

    private static readonly Lazy<PackRun> Route = new(Drive);

    private static PackRun Drive()
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
        var (packs, scripts) = LayoutPackGroups.Build(layoutDef, dungeon, setup.Content.EnemyScripts, packOnly: PackOnlyTemplates(registry, setup.Dungeon));
        var party = NavLayouts.PlaceParty(layoutDef, setup.Party.Take(3).ToList(), dungeon.Pulls[0].Id);
        var content = setup.Content with { Layout = nav, Bounds = nav.Extent, Packs = packs, EnemyScripts = scripts };
        var run = RunRoot.CreateRun(Seed, party, dungeon, content, setup.Options);
        var initial = run;
        var appearances = run.State.Actors.Values
            .Where(a => a.Kind != ActorKind.Player)
            .Select(a => new Appearance(run.State.Tick, a.Id, NearestPartyD2(run.State, a.Pos)))
            .ToList();

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
        var members = new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal);
        while (run.State.Tick < MaxTicks && run.RunEnd.Result is null)
        {
            var tick = run.State.Tick;
            var view = BotViews.BuildBotView(run.State, run.Encounter.Telegraphs.Select(BotViews.ToBotTelegraph), ClassKitView.Empty, kits) with { Layout = run.Content.Layout };
            var batch = Bot.RunBots(view, bots, mem, prng);
            mem = batch.Memories;
            prng = batch.Prng;
            var commands = batch.Commands.ToList();
            if (!Follow.AnyLivingEnemy(run.State) && run.State.Actors[LeaderId] is { Alive: true } leader)
            {
                commands.RemoveAll(c => c.ActorId == LeaderId);
                if (Follow.LeadRoute(tick, leader, run) is { } walk)
                {
                    commands.Add(walk);
                }
            }
            var stepped = RunRoot.StepRun(run, commands);
            var previous = run.State;
            run = stepped.Run;
            events.AddRange(stepped.Events);
            foreach (var a in run.State.Actors.Values.Where(a => a.Kind != ActorKind.Player && !previous.Actors.ContainsKey(a.Id)))
            {
                appearances.Add(new Appearance(run.State.Tick, a.Id, NearestPartyD2(run.State, a.Pos)));
            }
            foreach (var e in stepped.Events.Where(e => e.Type == "pack_engage"))
            {
                var pack = packs.First(p => p.Id == ((JsString)e.Get("packId")!).Value);
                members[pack.Id] = pack.Spawns.Select(s => s.ActorId).Where(run.State.Actors.ContainsKey).ToList();
            }
        }
        return new PackRun(events, members, packs, layoutDef, initial, appearances, run);
    }

    // The oracle setup's templates for every layout pack enemy no oracle pull fields (the Hall Sentry, STATE.md D49):
    // the same 1-health weakening as the pulls, the content script Tempo-scaled to the oracle key, as the host's
    // HostRunSetup.PackOnlyTemplates scales it. Fixture only; the pack's own behaviour is the sim's.
    private static IReadOnlyDictionary<string, PackTemplate> PackOnlyTemplates(ContentRegistry registry, SimDungeon oracle)
    {
        var fielded = oracle.Pulls.SelectMany(p => p.Spawns).Select(s => s.ClassId).ToHashSet(StringComparer.Ordinal);
        var templates = new SortedDictionary<string, PackTemplate>(StringComparer.Ordinal);
        foreach (var id in registry.Layout.Packs.SelectMany(p => p.Enemies).Select(e => e.EnemyId).Where(id => !fielded.Contains(id)).Distinct())
        {
            var def = registry.Enemies[id];
            var spawn = new PullSpawn(0, def.Id, Vec3.Zero, 0, 1) { SpeedMmPerTick = def.SpeedMmPerTick, ChaseMarginMm = def.ChaseMarginMm };
            var script = def.Script.Select(s => s.At.Kind switch
            {
                ScriptAtKind.Tick => Keyrun.Sim.Encounter.ScriptStep.At(s.At.Tick, s.Cast, s.Every),
                ScriptAtKind.Loop => Keyrun.Sim.Encounter.ScriptStep.Loop(s.Every, s.Cast),
                _ => throw new InvalidOperationException($"pack-only enemy {id}: script trigger {s.At.Kind} is not built by this fixture"),
            }).ToList();
            templates[id] = new PackTemplate(spawn, Tempo.ScaleScript(script, 2));
        }
        return templates;
    }

    // Squared XZ distance from p to the nearest party (player) actor, alive or dead; long.MaxValue with no party.
    private static long NearestPartyD2(SimState state, Vec3 p) => state.Actors.Values
        .Where(a => a.Kind == ActorKind.Player)
        .Select(a => ((a.Pos.X - p.X) * (a.Pos.X - p.X)) + ((a.Pos.Z - p.Z) * (a.Pos.Z - p.Z)))
        .DefaultIfEmpty(long.MaxValue)
        .Min();

    [Fact(DisplayName = "Path packs: every pack member, required and optional, stands idle in the state from tick 0")]
    public void Every_pack_member_stands_idle_from_tick_zero()
    {
        var r = Route.Value;
        Assert.Equal(0, r.Initial.State.Tick);
        Assert.Equal(r.Layout.Packs.Select(p => p.Id), r.Packs.Select(p => p.Id));
        Assert.Contains(r.Layout.Packs, p => p.Optional);
        Assert.Contains(r.Layout.Packs, p => !p.Optional);
        foreach (var pack in r.Packs)
        {
            Assert.NotEmpty(pack.Spawns);
            foreach (var spawn in pack.Spawns)
            {
                Assert.True(r.Initial.State.Actors.TryGetValue(spawn.ActorId, out var a), $"pack {pack.Id} member {spawn.ActorId} is not in the state at tick 0");
                Assert.True(a!.Alive && a.Idle && a.Kind == ActorKind.Enemy, $"pack {pack.Id} member {spawn.ActorId} at tick 0: alive={a.Alive} idle={a.Idle} kind={a.Kind}");
            }
        }
    }

    [Fact(DisplayName = "Path packs: no enemy ever enters the state within 5 m of a party member")]
    public void No_enemy_enters_the_state_on_the_party()
    {
        var r = Route.Value;
        Assert.NotEmpty(r.Appearances);
        var close = r.Appearances.Where(x => x.NearestPartyD2 < MinSpawnClearMm * MinSpawnClearMm).ToList();
        Assert.True(close.Count == 0, "enemies entered the state within " + MinSpawnClearMm + " mm of a party member: " +
            string.Join(", ", close.Select(x => FormattableString.Invariant($"actor {x.ActorId} at tick {x.Tick} (d2 {x.NearestPartyD2})"))));
        // Every pack member is among the tick-0 appearances: none is added later.
        var packIds = r.Packs.SelectMany(p => p.Spawns).Select(s => s.ActorId).ToHashSet();
        Assert.All(r.Appearances.Where(x => packIds.Contains(x.ActorId)), x => Assert.Equal(0, x.Tick));
    }

    private static string Stop(SimEvent e) => e.Type == "pack_engage" ? ((JsString)e.Get("packId")!).Value : ((JsString)e.Get("pullId")!).Value;

    [Fact(DisplayName = "Path packs: the route runs pull, pack, pull and ends on the boss, and the run is timed")]
    public void The_route_runs_pull_pack_pull_then_the_boss()
    {
        var r = Route.Value;
        var stops = r.Events.Where(e => e.Type is "pull_start" or "pack_engage").Select(Stop).ToList();
        var required = r.Layout.Packs.Where(p => !p.Optional).Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        Assert.NotEmpty(required);
        Assert.Equal("timed", r.Final.RunEnd.Result?.ToJs());
        Assert.Equal(LayoutDef.BossPullId, stops[^1]);
        Assert.Equal(new[] { "choir", "pair", "seed", LayoutDef.BossPullId }, stops.Where(s => !required.Contains(s)).ToArray());
        foreach (var id in required)
        {
            int at = stops.IndexOf(id);
            Assert.True(at > 0 && at < stops.Count - 1, $"required pack {id} was not engaged between a pull and the boss: {string.Join(" -> ", stops)}");
        }
        // pull -> pack -> pull: some required pack is fought after a teaching pull and before the next one.
        Assert.Contains(Enumerable.Range(1, stops.Count - 2), i => required.Contains(stops[i]) && !required.Contains(stops[i - 1]) && stops[i + 1] is "pair" or "seed");
    }

    [Fact(DisplayName = "Path packs: a required pack engages as one group, every member on its engage tick")]
    public void A_required_pack_engages_as_one_group()
    {
        var r = Route.Value;
        var required = r.Packs.Where(p => r.Layout.Packs.Any(l => l.Id == p.Id && !l.Optional)).ToList();
        Assert.NotEmpty(required);
        foreach (var pack in required)
        {
            var e = Assert.Single(r.Events, x => x.Type == "pack_engage" && Stop(x) == pack.Id);
            var ids = ((JsArray)e.Get("actorIds")!).Items.Select(v => (int)((JsInt)v).Value).ToList();
            Assert.Equal(pack.Spawns.Select(s => s.ActorId), ids);
            Assert.Equal(ids, r.MembersAtEngage[pack.Id]);
            Assert.True(ids.Count >= 3, $"pack {pack.Id} has {ids.Count} members: a real pack, not a pair");
            Assert.All(ids, id => Assert.False(r.Final.State.Actors[id].Alive));
        }
    }

    /// <summary>Every actor id a pull or pack engaged on <paramref name="tick"/> (pull_start and pack_engage events).</summary>
    private static List<int> EngagedOn(PackRun r, int tick) => r.Events
        .Where(e => e.Tick == tick && e.Type is "pull_start" or "pack_engage")
        .SelectMany(e => ((JsArray)e.Get("actorIds")!).Items.Select(v => (int)((JsInt)v).Value))
        .ToList();

    [Theory(DisplayName = "Path packs: the Choir with its escort, and the Pair, engage on one tick as one Mythic+ pack, and every body dies")]
    [InlineData("choir", 6)]
    [InlineData("pair", 5)]
    public void A_teaching_pull_engages_with_its_escort_as_one_pack(string pullId, int minBodies)
    {
        var r = Route.Value;
        var start = Assert.Single(r.Events, e => e.Type == "pull_start" && Stop(e) == pullId);
        var bodies = EngagedOn(r, start.Tick);
        Assert.True(bodies.Count >= minBodies,
            $"{pullId} engaged {bodies.Count} bodies on tick {start.Tick} ({string.Join(", ", bodies.Select(id => r.Initial.State.Actors[id].ClassId))}): under the {minBodies}-body pack");
        Assert.All(bodies, id => Assert.False(r.Final.State.Actors[id].Alive, $"{pullId} body {id} ({r.Final.State.Actors[id].ClassId}) is still alive at the run end"));
    }

    [Fact(DisplayName = "Path packs: walking past every optional pack leaves the forces bar short")]
    public void Skipping_every_optional_pack_leaves_the_bar_short()
    {
        var r = Route.Value;
        var forces = ContentLoader.Load().Forces;
        var table = forces.Values.ToDictionary(v => v.EnemyId, v => v.Points, StringComparer.Ordinal);
        var optional = r.Layout.Packs.Where(p => p.Optional).Select(p => p.Id).ToList();
        Assert.NotEmpty(optional);
        Assert.DoesNotContain(r.Events, e => e.Type == "pack_engage" && optional.Contains(Stop(e)));
        var reading = Forces.Read(r.Final.State, table, forces.Required);
        int teaching = RouteForces.CriticalPathPoints(forces, ContentLoader.Load().Dungeons[r.Layout.Dungeon]);
        Assert.True(reading.Points > teaching, $"forces {reading.Points}: the required pack banked nothing over the teaching pulls ({teaching})");
        Assert.False(reading.Full, $"forces {reading.Points}/{forces.Required}: the bar filled with every optional pack skipped");
    }
}
