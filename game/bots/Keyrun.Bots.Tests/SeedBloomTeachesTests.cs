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
/// SEED-BLOOM-TEACHES (finding f-add02b73; design/BIBLE.md section 9.6 item 3: the Seed "on death drops a Window on the
/// floor; a Combo landed in it grants bonus loot at run end. Teaches the boss's core rule with no stakes", and section 4:
/// a Payoff landing inside the ring closes it and fires a Combo). The same run shape as <see cref="SeedPullTeachesTests"/>:
/// the hollow-vault k2 oracle setup placed into game/content/data/layout.json, the host's three seats (Warden 1,
/// Chanter 2, Cinder 3) on the real bot roles, and seat 1 driven like the autorun Warden (the route while no enemy
/// lives). Before the fix the Seed's Death Bloom was surfaced to bots as a hostile floor patch (its opener is an enemy),
/// so they fled it and then followed the leader out, and the Window expired with no Combo (HUD run-end bonus count 0).
/// The pull must teach the rule: a Combo lands in the seedbloom Window before it ends, and the run's rewards carry the
/// Seed bonus.
/// </summary>
public sealed class SeedBloomTeachesTests
{
    private const long Seed = 1;
    private const int LeaderId = 1;
    private const int MaxTicks = 8000;

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test with xUnit's output sink (the bloom's timeline is printed for the PR).</summary>
    /// <param name="output">The sink.</param>
    public SeedBloomTeachesTests(ITestOutputHelper output) => _output = output;

    private sealed record Driven(List<SimEvent> Events, int SeedId, RunRewards Rewards);

    private static readonly Lazy<Driven> Run = new(Drive);

    private static Driven Drive()
    {
        var registry = ContentLoader.Load();
        var layoutDef = registry.Layout;
        var nav = NavLayouts.ToSim(layoutDef);
        var setup = RunSetup.Load(Path.Combine(RolesOracleTraceTests.OracleDir(), "hollow-vault-k2.setup.json"), Seed);
        SimDungeon dungeon = NavLayouts.PlaceDungeon(layoutDef, setup.Dungeon);
        var party = NavLayouts.PlaceParty(layoutDef, setup.Party.Take(3).ToList(), dungeon.Pulls[0].Id);
        var content = setup.Content with { Layout = nav, Bounds = nav.Extent };
        var run = RunRoot.CreateRun(Seed, party, dungeon, content, setup.Options);
        var seedId = dungeon.Pulls.Single(p => p.Id == "seed").Spawns.Single().ActorId;

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

        while (run.State.Tick < MaxTicks && run.RunEnd.ResultTick is null)
        {
            // Stop once the Seed's Window has ended: the bonus is decided by then.
            if (run.Rewards.SeedWindowEndsTick is { } ends && run.State.Tick > ends)
            {
                break;
            }
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
                    if (Follow.LeadRoute(tick, leader, run) is { } walk)
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
        }
        return new Driven(events, seedId, run.Rewards);
    }

    private static string? S(SimEvent e, string key) => e.Get(key) is JsString s ? s.Value : null;

    [Fact(DisplayName = "Seed bloom: a Combo lands in the Seed's Death Bloom Window before it ends, and the run carries the Seed bonus")]
    public void A_combo_lands_in_the_seedbloom_window_and_the_bonus_is_awarded()
    {
        var r = Run.Value;
        var death = r.Events.SingleOrDefault(e => e.Type == "death" && e.Get("actorId") is JsInt a && a.Value == r.SeedId);
        Assert.True(death is not null, "the Seed never died");
        var opened = r.Events.SingleOrDefault(e => e.Type == "window_opened" && S(e, "windowId") == "seedbloom");
        Assert.True(opened is not null, "the Seed's Death Bloom Window never opened");
        _output.WriteLine($"seed death {death!.Tick}, seedbloom opened {opened!.Tick}, ends {r.Rewards.SeedWindowEndsTick}");
        foreach (var e in r.Events.Where(e => e.Tick >= opened.Tick && (S(e, "windowId") == "seedbloom" || e.Type == "cast_complete")))
        {
            _output.WriteLine($"tick {e.Tick} {e.Type} actor {e.Get("actorId")} ability {S(e, "abilityId")} payoff {e.Get("payoffActorId")} reason {S(e, "reason")}");
        }
        var combo = r.Events.FirstOrDefault(e => e.Type == "combo" && S(e, "windowId") == "seedbloom");
        Assert.True(combo is not null, $"no Combo landed in the seedbloom Window (opened tick {opened.Tick}, ends {r.Rewards.SeedWindowEndsTick})");
        var bonus = Assert.Single(r.Rewards.BonusLoot);
        Assert.Equal("seed", bonus.Source);
        Assert.Equal("seedbloom", bonus.WindowId);
        Assert.True(bonus.Tick <= r.Rewards.SeedWindowEndsTick, $"the bonus Combo at tick {bonus.Tick} fell after the Window's end {r.Rewards.SeedWindowEndsTick}");
    }
}
