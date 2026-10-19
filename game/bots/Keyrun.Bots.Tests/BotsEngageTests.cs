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
/// BOTS-ENGAGE (KF2, Matt 2026-10-19: "the bots didnt engage first room"): the default run C party (seed 1, key 2, the
/// hollow-vault k2 oracle setup placed on the real layout, as <see cref="NavRouteTests"/> drives it, but at real
/// health) must fight the Choir from its first ticks. BIBLE section 9: the Choir teaches the kick ledger; section 4:
/// the party is a timing game, so a damage bot that stands idle removes the Window and Combo play from the pull.
/// </summary>
public sealed class BotsEngageTests
{
    private const long Seed = 1;
    private const int LeaderId = 1;
    private const int MaxTicks = 4000;

    /// <summary>
    /// The engage bound: 20 ticks (1 s). A skill-80 bot reacts every 3 ticks (Skill.ReactionDelayTicks) and misses a
    /// decision 7% of the time, so 20 ticks hold six decisions; the Choir spawns inside the Cinder's 25 m range on the
    /// pull_start tick, so no walk is needed. A bot that has not cast by then is not engaging.
    /// </summary>
    private const int EngageBoundTicks = 20;

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test with xUnit's output sink.</summary>
    /// <param name="output">Output.</param>
    public BotsEngageTests(ITestOutputHelper output) => _output = output;

    private sealed record EngageRun(
        int PullStartTick,
        IReadOnlyList<int> ChoirIds,
        IReadOnlyList<int> DamageBotIds,
        List<(int Tick, CastCommand Cast)> Casts,
        List<SimEvent> Events,
        List<string> Trace);

    private static readonly Lazy<EngageRun> Choir = new(Drive);

    private static EngageRun Drive()
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
        var damageBots = party.Where(p => p.Id != LeaderId && p.Role == ActorRole.Damage).Select(p => p.Id).ToList();
        IReadOnlyDictionary<int, BotMemory> mem = memories;
        var prng = Skill.SeedPrng(Seed);
        var events = new List<SimEvent>();
        var casts = new List<(int, CastCommand)>();
        var trace = new List<string>();
        int? pullStart = null;
        IReadOnlyList<int> choir = Array.Empty<int>();

        while (run.State.Tick < MaxTicks && (pullStart is null || run.State.Tick < pullStart.Value + 200))
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
                if (Follow.WalkRoute(tick, leader, nav) is { } walk)
                {
                    commands.Add(walk);
                }
            }
            if (pullStart is not null)
            {
                foreach (var c in commands.Where(c => damageBots.Contains(c.ActorId)))
                {
                    if (c is CastCommand cast)
                    {
                        casts.Add((tick, cast));
                    }
                    if (tick - pullStart.Value < 200)
                    {
                        var self = run.State.Actors[c.ActorId];
                        trace.Add($"t{tick} a{c.ActorId} {c.Kind} {(c as CastCommand)?.AbilityId} target={(c as CastCommand)?.TargetId} selfTarget={self.TargetId} pos=({self.Pos.X},{self.Pos.Z})");
                    }
                }
            }

            var stepped = RunRoot.StepRun(run, commands);
            run = stepped.Run;
            events.AddRange(stepped.Events);
            if (pullStart is null && stepped.Events.FirstOrDefault(e => e.Type == "pull_start") is { } ps)
            {
                pullStart = ps.Tick;
                choir = ((JsArray)ps.Get("actorIds")!).Items.Select(v => (int)((JsInt)v).Value).ToList();
            }
        }
        Assert.NotNull(pullStart);
        return new EngageRun(pullStart!.Value, choir, damageBots, casts, events, trace);
    }

    [Fact(DisplayName = "Bots engage: every damage bot casts at a living Choir enemy within 20 ticks of pull_start")]
    public void Every_damage_bot_casts_at_a_choir_enemy_within_20_ticks_of_pull_start()
    {
        var r = Choir.Value;
        _output.WriteLine($"pull_start tick {r.PullStartTick}, choir {string.Join(",", r.ChoirIds)}, damage bots {string.Join(",", r.DamageBotIds)}");
        foreach (var line in r.Trace.Take(40))
        {
            _output.WriteLine(line);
        }
        Assert.NotEmpty(r.DamageBotIds);
        Assert.NotEmpty(r.ChoirIds);
        foreach (var bot in r.DamageBotIds)
        {
            var first = r.Casts.FirstOrDefault(c => c.Cast.ActorId == bot && c.Cast.TargetId is { } t && r.ChoirIds.Contains(t));
            var firstAny = r.Casts.FirstOrDefault(c => c.Cast.ActorId == bot);
            Assert.True(
                first.Cast is not null && first.Tick - r.PullStartTick <= EngageBoundTicks,
                $"damage bot {bot}: first cast at a Choir enemy at tick {(first.Cast is null ? "never" : first.Tick.ToString(System.Globalization.CultureInfo.InvariantCulture))} " +
                $"(pull_start {r.PullStartTick}, bound {EngageBoundTicks}); first cast of any kind: " +
                $"{(firstAny.Cast is null ? "none" : $"tick {firstAny.Tick} {firstAny.Cast.AbilityId} target={firstAny.Cast.TargetId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "null"}")}");
        }
    }

    [Fact(DisplayName = "Bots engage: every damage bot lands damage on a Choir enemy within 60 ticks of pull_start")]
    public void Every_damage_bot_damages_a_choir_enemy_within_60_ticks_of_pull_start()
    {
        var r = Choir.Value;
        foreach (var bot in r.DamageBotIds)
        {
            var hit = r.Events.FirstOrDefault(e => e.Type == "damage" && e.Tick >= r.PullStartTick &&
                ((JsInt)e.Get("sourceId")!).Value == bot && r.ChoirIds.Contains((int)((JsInt)e.Get("targetId")!).Value));
            _output.WriteLine($"damage bot {bot}: first Choir damage tick {(hit is null ? "never" : hit.Tick.ToString(System.Globalization.CultureInfo.InvariantCulture))}");
            Assert.True(hit is not null && hit.Tick - r.PullStartTick <= 60,
                $"damage bot {bot}: first damage on a Choir enemy at {(hit is null ? "never" : hit.Tick.ToString(System.Globalization.CultureInfo.InvariantCulture))} (pull_start {r.PullStartTick})");
        }
    }
}
