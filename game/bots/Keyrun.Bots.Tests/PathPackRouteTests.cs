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

    private sealed record PackRun(List<SimEvent> Events, Dictionary<string, IReadOnlyList<int>> MembersAtEngage, IReadOnlyList<PackGroup> Packs, LayoutDef Layout, Run Final);

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
        var (packs, scripts) = LayoutPackGroups.Build(layoutDef, dungeon, setup.Content.EnemyScripts);
        var party = NavLayouts.PlaceParty(layoutDef, setup.Party.Take(3).ToList(), dungeon.Pulls[0].Id);
        var content = setup.Content with { Layout = nav, Bounds = nav.Extent, Packs = packs, EnemyScripts = scripts };
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
            run = stepped.Run;
            events.AddRange(stepped.Events);
            foreach (var e in stepped.Events.Where(e => e.Type == "pack_engage"))
            {
                var pack = packs.First(p => p.Id == ((JsString)e.Get("packId")!).Value);
                members[pack.Id] = pack.Spawns.Select(s => s.ActorId).Where(run.State.Actors.ContainsKey).ToList();
            }
        }
        return new PackRun(events, members, packs, layoutDef, run);
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
