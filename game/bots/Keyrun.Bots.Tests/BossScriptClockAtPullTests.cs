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
using Xunit.Abstractions;
using SimDungeon = Keyrun.Sim.Encounter.DungeonDef;

namespace Keyrun.Bots.Tests;

/// <summary>
/// R-BOSS-5 / BIBLE s3 "every dangerous enemy action is a cast with a visible bar and a floor telegraph drawn ahead of
/// resolution" (finding f-0431d7f5): a scripted enemy's slam must never complete without an earlier telegraph of its own.
/// The hollow-vault k2 setup runs with the three bot seats; the party's walk starts after a delay, so the boss pull
/// engages on a different tick each run. For every slam cast that completes, a telegraph with the same actorId and
/// cast must have been emitted on an earlier tick.
/// </summary>
public sealed class BossScriptClockAtPullTests
{
    private const long Seed = 1;
    private const int LeaderId = 1;
    private const int MaxTicks = 8000;

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test with xUnit's output sink.</summary>
    /// <param name="output">The sink.</param>
    public BossScriptClockAtPullTests(ITestOutputHelper output) => _output = output;

    private sealed record Driven(List<SimEvent> Events, int PullStartTick, IReadOnlyList<(int Tick, string PullId)> Pulls);

    private static Driven Drive(int walkDelayTicks)
    {
        var registry = ContentLoader.Load();
        var layoutDef = registry.Layout;
        var nav = NavLayouts.ToSim(layoutDef);
        var setup = RunSetup.Load(Path.Combine(RolesOracleTraceTests.OracleDir(), "hollow-vault-k2.setup.json"), Seed);
        SimDungeon dungeon = NavLayouts.PlaceDungeon(layoutDef, setup.Dungeon);
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
        var pulls = new List<(int Tick, string PullId)>();

        while (run.State.Tick < MaxTicks && run.RunEnd.ResultTick is null)
        {
            var tick = run.State.Tick;
            var view = BotViews.BuildBotView(run.State, run.Encounter.Telegraphs.Select(BotViews.ToBotTelegraph), ClassKitView.Empty, kits) with { Layout = run.Content.Layout };
            var batch = Bot.RunBots(view, bots, mem, prng);
            mem = batch.Memories;
            prng = batch.Prng;
            var commands = batch.Commands.ToList();
            if (run.State.Actors[LeaderId] is { Alive: true } leader)
            {
                if (!Follow.AnyLivingEnemy(run.State))
                {
                    commands.RemoveAll(c => c.ActorId == LeaderId);
                    if (tick >= walkDelayTicks && Follow.LeadRoute(tick, leader, run) is { } walk)
                    {
                        commands.Add(walk);
                    }
                }
                else
                {
                    commands.RemoveAll(c => c.ActorId == LeaderId && c is MoveCommand or DashCommand);
                }
            }

            var stepped = RunRoot.StepRun(run, commands);
            run = stepped.Run;
            events.AddRange(stepped.Events);
            foreach (var e in stepped.Events.Where(e => e.Type == "pull_start"))
            {
                pulls.Add((e.Tick, ((JsString)e.Get("pullId")!).Value));
            }
        }
        var pullStart = pulls.Where(p => p.PullId == "boss").Select(p => p.Tick).DefaultIfEmpty(-1).First();
        return new Driven(events, pullStart, pulls);
    }

    private static string S(SimEvent e, string key) => ((JsString)e.Get(key)!).Value;

    private static int I(SimEvent e, string key) => (int)((JsInt)e.Get(key)!).Value;

    /// <summary>
    /// Every slam cast that completes has an earlier telegraph with the same actor and cast. Returns the violations.
    /// </summary>
    private static List<string> UnannouncedSlams(Driven r)
    {
        var slamCasts = r.Events.Where(e => e.Type == "cast_complete" && (S(e, "abilityId") is "hollowWarden.slam" or "seed.slam"));
        var violations = new List<string>();
        foreach (var c in slamCasts)
        {
            var actor = I(c, "actorId");
            var announced = r.Events.Any(t => t.Type == "telegraph" && t.Tick < c.Tick
                && t.Get("actorId") is JsInt a && a.Value == actor && S(t, "cast") == S(c, "abilityId"));
            if (!announced)
            {
                violations.Add($"{S(c, "abilityId")} by actor {actor} completed at tick {c.Tick} with no earlier telegraph");
            }
        }
        return violations;
    }

    [Theory(DisplayName = "Boss pull at a varied tick: no slam completes before its own telegraph (R-BOSS-5, f-0431d7f5)")]
    [InlineData(0)]
    [InlineData(37)]
    [InlineData(211)]
    public void Boss_slams_are_telegraphed_before_they_complete(int walkDelayTicks)
    {
        var r = Drive(walkDelayTicks);
        _output.WriteLine($"walk delay {walkDelayTicks}: boss pull_start {r.PullStartTick}; pulls {string.Join(", ", r.Pulls.Select(p => $"{p.PullId}@{p.Tick}"))}");
        Assert.True(r.PullStartTick >= 0, "the boss pull never started");
        var slams = r.Events.Count(e => e.Type == "cast_complete" && (S(e, "abilityId") is "hollowWarden.slam" or "seed.slam"));
        _output.WriteLine($"walk delay {walkDelayTicks}: slam cast_complete count {slams}");
        Assert.True(slams > 0, "no slam completed, so the property is vacuous");
        var violations = UnannouncedSlams(r);
        Assert.True(violations.Count == 0, string.Join("; ", violations));
    }
}
