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
/// SEED-PULL-TEACHES (Matt demo verdict KF3, 2026-10-19: "the Seed elite spawns on entry and the bots cheese it";
/// design/BIBLE.md section 9 item 3: the Seed "teaches the boss's core rule with no stakes", and section 3: a mechanic
/// is read before it lands). Run C's shape at full health: the hollow-vault k2 oracle setup placed into the rooms of
/// game/content/data/layout.json, the host's three seats (Warden 1, Chanter 2, Cinder 3) at the Entrance, the real bot
/// roles on every seat, and seat 1 driven like the autorun Warden (the route, holding for the party, while no enemy
/// lives; in a fight it keeps its casts and drops its moves). The Seed spawns on the tick the last seat enters the
/// seed-chamber (STATE.md D27). From there the Seed must open with a readable setup: none of its casts may start before
/// the party was shown that cast's own telegraph, and its telegraphed slam must land at least once before it can die.
/// </summary>
public sealed class SeedPullTeachesTests
{
    private const long Seed = 1;
    private const int LeaderId = 1;
    private const int MaxTicks = 8000;

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test with xUnit's output sink (the Seed's timeline is printed for the PR).</summary>
    /// <param name="output">The sink.</param>
    public SeedPullTeachesTests(ITestOutputHelper output) => _output = output;

    private sealed record SeedCast(int StartTick, string AbilityId);

    private sealed record Driven(List<SimEvent> Events, int SeedId, int PullStartTick, List<SeedCast> Casts);

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
        var seedPull = dungeon.Pulls.Single(p => p.Id == "seed");
        var seedId = seedPull.Spawns.Single().ActorId;

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
        var casts = new List<SeedCast>();
        var pullStart = -1;

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
            if (pullStart < 0 && stepped.Events.FirstOrDefault(e => e.Type == "pull_start" && ((JsString)e.Get("pullId")!).Value == "seed") is { } start)
            {
                pullStart = start.Tick;
            }
            if (run.State.Actors.TryGetValue(seedId, out var s) && s.Alive && s.CastId is { } castId && s.CastStartTick is { } castStart &&
                !casts.Any(c => c.StartTick == castStart && c.AbilityId == castId))
            {
                casts.Add(new SeedCast(castStart, castId));
            }
        }
        return new Driven(events, seedId, pullStart, casts);
    }

    private static int I(SimEvent e, string key) => (int)((JsInt)e.Get(key)!).Value;

    private static string S(SimEvent e, string key) => ((JsString)e.Get(key)!).Value;

    private List<SimEvent> SeedTelegraphs(Driven r)
    {
        var tgs = r.Events.Where(e => e.Type == "telegraph" && e.Get("actorId") is JsInt a && a.Value == r.SeedId).ToList();
        foreach (var t in tgs)
        {
            _output.WriteLine($"telegraph tick {t.Tick} cast {S(t, "cast")} resolves {I(t, "resolvesTick")}");
        }
        foreach (var c in r.Casts)
        {
            _output.WriteLine($"cast start tick {c.StartTick} {c.AbilityId}");
        }
        return tgs;
    }

    private static int DeathTick(Driven r) =>
        r.Events.Single(e => e.Type == "death" && e.Get("actorId") is JsInt a && a.Value == r.SeedId).Tick;

    [Fact(DisplayName = "Seed pull: every Seed cast starts only after the party was shown that cast's own telegraph, inside the pull")]
    public void Every_seed_cast_is_announced_by_its_telegraph_after_pull_start()
    {
        var r = Run.Value;
        Assert.True(r.PullStartTick > 0, "the Seed pull never started");
        _output.WriteLine($"seed pull_start {r.PullStartTick}, seed death {DeathTick(r)}");
        var tgs = SeedTelegraphs(r);
        Assert.NotEmpty(r.Casts);
        var unannounced = r.Casts
            .Where(c => !tgs.Any(t => S(t, "cast") == c.AbilityId && I(t, "resolvesTick") == c.StartTick && t.Tick >= r.PullStartTick))
            .Select(c => $"{c.AbilityId} started at tick {c.StartTick} ({c.StartTick - r.PullStartTick} ticks after pull_start {r.PullStartTick}) with no telegraph of its own")
            .ToList();
        Assert.True(unannounced.Count == 0, string.Join("; ", unannounced));
    }

    [Fact(DisplayName = "Seed pull: the Seed's telegraphed slam lands at least once before the Seed dies")]
    public void A_telegraphed_slam_resolves_before_the_seed_dies()
    {
        var r = Run.Value;
        var death = DeathTick(r);
        var tgs = SeedTelegraphs(r);
        var slams = r.Events.Where(e => e.Type == "cast_complete" && I(e, "actorId") == r.SeedId && S(e, "abilityId") == "seed.slam" && e.Tick < death).ToList();
        var shown = slams.Where(c => tgs.Any(t => S(t, "cast") == "seed.slam" && t.Tick >= r.PullStartTick && I(t, "resolvesTick") < c.Tick)).ToList();
        Assert.True(shown.Count >= 1, $"no telegraphed seed.slam completed between pull_start {r.PullStartTick} and the Seed's death at {death}");
    }

    /// <summary>
    /// SEED-LOCK-TEACHES (KF3 "the bots cheese it", f-70c82aca; BIBLE s9: the Seed "teaches the boss's core rule", and
    /// the Lock is a Window the boss opens on itself that only a Combo closes). Before the fix the Seed's only Window was
    /// its onDeath floor bloom, so the party burst it with no rule in play and the rule showed up after the fight, closed
    /// by one lone Payoff. The Seed must open its own Window on itself while it lives, and the party must close it with
    /// a Combo before the Seed dies.
    /// </summary>
    [Fact(DisplayName = "Seed pull: the Seed opens a Window on itself while it lives and the party closes it with a Combo before it dies")]
    public void The_party_closes_the_seeds_own_window_with_a_combo_while_it_lives()
    {
        var r = Run.Value;
        var death = DeathTick(r);
        bool OnSeed(SimEvent e, string key) => e.Get(key) is JsInt a && a.Value == r.SeedId;
        var opened = r.Events.Where(e => e.Type == "window_opened" && OnSeed(e, "setupActorId") && OnSeed(e, "targetId") &&
            e.Tick >= r.PullStartTick && e.Tick < death).ToList();
        var closed = r.Events.Where(e => e.Type == "combo" && OnSeed(e, "setupActorId") && OnSeed(e, "targetId") &&
            e.Tick >= r.PullStartTick && e.Tick < death).ToList();
        foreach (var e in opened.Concat(closed).OrderBy(e => e.Tick))
        {
            _output.WriteLine($"tick {e.Tick} {e.Type} window {S(e, "windowId")} payoff {e.Get("payoffActorId")} combo {e.Get("comboId")}");
        }
        Assert.True(opened.Count >= 1, $"the Seed opened no Window on itself between pull_start {r.PullStartTick} and its death at {death}");
        Assert.True(closed.Count >= 1, $"no Combo closed a Window the Seed opened on itself before it died at {death} ({opened.Count} opened)");
    }
}
