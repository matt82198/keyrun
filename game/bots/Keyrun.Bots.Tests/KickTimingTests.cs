using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Keyrun.Bots.Roles;
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
/// BOT-KICK-TIMING (Matt 2026-10-20 "lean into m+ design heavily while in the dungeon"; design/BIBLE.md section 9 The
/// Choir's must-kick 4-second heal, section 3 interrupts by intended tick, section 4 synergy as a timing game): on a
/// layout run a bot kicks the Choir's heal like a good M+ player, after a human reaction window and into the back half
/// of the cast, so the cast bar is on screen, and the kick still lands before the cast resolves. The kicker is the one
/// the kick ledger (<see cref="Ledger.ResolveInterrupt"/>) assigns. The oracle runner (no layout) is unchanged.
/// </summary>
public sealed class KickTimingTests
{
    /// <summary>
    /// The Choir's 80-tick (4 s) heal is kicked no earlier than 40 ticks (2 s) in: half the cast, so the bar is seen
    /// filling, and well past a 0.3-0.8 s human reaction. Stated here independently of the production constants.
    /// </summary>
    private const int ChoirKickNotBeforeTicks = 40;

    private const string HealCast = "choir.healCast";
    private const int ChoirId = 100;
    private const int CastStart = 100;
    private const int CastEnds = 180;
    private const long Seed = 1;
    private const int LeaderId = 1;
    private const int MaxTicks = 4000;

    private static readonly Lazy<NavLayout> Nav = new(() => NavLayouts.ToSim(ContentLoader.Load().Layout));

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test with xUnit's output sink (the cast and kick ticks are printed for the PR).</summary>
    /// <param name="output">The sink.</param>
    public KickTimingTests(ITestOutputHelper output) => _output = output;

    private static ActorState Choir(int start = CastStart, int ends = CastEnds) => RolesTestKit.Actor(
        id: ChoirId, kind: ActorKind.Enemy, classId: "choir", role: ActorRole.Healer, x: 5000,
        health: 1000, maxHealth: 1000, castId: HealCast, castStartTick: start, castEndsTick: ends);

    private static ActorState Chanter(int id, int rebukeReady = 0) => RolesTestKit.Actor(
        id: id, classId: "chanter", role: ActorRole.Healer, health: 1000, maxHealth: 1000,
        cooldowns: new[] { ("chanter.rebuke", rebukeReady) });

    private static CastCommand? KickAt(int tick, int selfId, NavLayout? layout, params ActorState[] actors)
    {
        var view = RolesTestKit.ViewOf(tick, RolesTestKit.RealKits, null, actors) with { Layout = layout };
        var self = view.State.Actors[selfId];
        return RoleShared.LedgerKickCommand(view, self, RoleShared.KitOf(view, self.ClassId));
    }

    [Fact(DisplayName = "Kick timing: in the dungeon the ledger's kicker holds the Choir heal kick until 40 ticks into the 80-tick cast, then kicks")]
    public void Ledger_kicker_waits_for_the_back_half_of_the_choir_cast()
    {
        for (var tick = CastStart; tick < CastStart + ChoirKickNotBeforeTicks; tick++)
        {
            Assert.True(KickAt(tick, 2, Nav.Value, Choir(), Chanter(2), Chanter(5)) is null, $"bot 2 kicked at tick {tick}, {tick - CastStart} ticks into the cast");
        }
        var kick = KickAt(CastStart + ChoirKickNotBeforeTicks, 2, Nav.Value, Choir(), Chanter(2), Chanter(5));
        Assert.NotNull(kick);
        Assert.Equal("chanter.rebuke", kick!.AbilityId);
        Assert.Equal(ChoirId, kick.TargetId);
        Assert.Equal(CastStart + ChoirKickNotBeforeTicks, kick.Tick);
        Assert.True(kick.Tick < CastEnds, "the kick must land before the cast resolves");
    }

    [Fact(DisplayName = "Kick timing: the bot that kicks is the one the ledger assigns (lowest ready id), the other holds")]
    public void Only_the_ledger_assigned_bot_kicks()
    {
        var open = CastStart + ChoirKickNotBeforeTicks;
        Assert.Null(KickAt(open, 5, Nav.Value, Choir(), Chanter(2), Chanter(5)));
        // Bot 2's kick on cooldown: the ledger hands the kick to bot 5.
        var handed = KickAt(open, 5, Nav.Value, Choir(), Chanter(2, rebukeReady: 999), Chanter(5));
        Assert.NotNull(handed);
        Assert.Equal(5, handed!.ActorId);
        Assert.Null(KickAt(open, 2, Nav.Value, Choir(), Chanter(2, rebukeReady: 999), Chanter(5)));
    }

    [Fact(DisplayName = "Kick timing: a short interruptible cast still waits a reaction, and the oracle runner (no layout) kicks at once as before")]
    public void Short_casts_wait_a_reaction_and_the_oracle_runner_is_unchanged()
    {
        // A 20-tick cast: the land margin leaves no back half, so the bot kicks after the 12-tick (0.6 s) reaction.
        Assert.Null(KickAt(CastStart + 11, 2, Nav.Value, Choir(CastStart, CastStart + 20), Chanter(2)));
        Assert.NotNull(KickAt(CastStart + 12, 2, Nav.Value, Choir(CastStart, CastStart + 20), Chanter(2)));
        // No layout = the oracle runner (frozen TS parity): the ledger kicks on the first ready tick.
        Assert.NotNull(KickAt(CastStart, 2, null, Choir(), Chanter(2)));
        Assert.NotNull(KickAt(CastStart + 1, 2, null, Choir(), Chanter(2)));
    }

    private sealed record CastSeen(int Start, int Ends, int? KickTick, int? KickerId, int? LedgerKickerId, bool Completed);

    [Fact(DisplayName = "Kick timing: run C's Choir pull on the real layout, every heal cast is kicked 40+ ticks in, before it resolves, by the ledger's kicker")]
    public void Choir_pull_every_heal_is_kicked_late_but_in_time()
    {
        var casts = DriveChoirPull();
        foreach (var c in casts)
        {
            _output.WriteLine($"healCast start={c.Start} ends={c.Ends} kick={c.KickTick?.ToString() ?? "none"} kicker={c.KickerId?.ToString() ?? "none"} ledger={c.LedgerKickerId?.ToString() ?? "none"} completed={c.Completed}");
        }
        Assert.True(casts.Count >= 2, $"only {casts.Count} Choir heal casts were seen; the check would be vacuous");
        var failures = new List<string>();
        foreach (var c in casts)
        {
            if (c.KickTick is not { } kick)
            {
                failures.Add($"cast {c.Start}..{c.Ends} was never kicked (completed={c.Completed})");
                continue;
            }
            if (kick - c.Start < ChoirKickNotBeforeTicks)
            {
                failures.Add($"cast {c.Start}..{c.Ends} kicked at {kick}, only {kick - c.Start} ticks in (want >= {ChoirKickNotBeforeTicks})");
            }
            if (kick >= c.Ends)
            {
                failures.Add($"cast {c.Start}..{c.Ends} kicked at {kick}, after it resolved");
            }
            if (c.KickerId != c.LedgerKickerId)
            {
                failures.Add($"cast {c.Start}..{c.Ends} kicked by {c.KickerId}, the ledger assigned {c.LedgerKickerId}");
            }
        }
        Assert.True(failures.Count == 0, string.Join("; ", failures));
    }

    /// <summary>
    /// Run C's shape (PartyTogetherTests): the hollow-vault k2 oracle setup placed into the real layout, three seats
    /// (Warden 1, Chanter 2, Cinder 3), all real bot roles at skill 80 (the host's dial), the leader walking the route
    /// while no enemy lives and holding where he stands in a fight. Stops at the Choir's pull_clear.
    /// </summary>
    private static List<CastSeen> DriveChoirPull()
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
        var casts = new List<CastSeen>();
        var open = new Dictionary<int, CastSeen>();

        while (run.State.Tick < MaxTicks && run.RunEnd.ResultTick is null)
        {
            var tick = run.State.Tick;
            foreach (var choir in run.State.Actors.Values.Where(a => a.Kind == ActorKind.Enemy && a.Alive && a.CastId == HealCast))
            {
                if (!open.ContainsKey(choir.Id))
                {
                    open[choir.Id] = new CastSeen(choir.CastStartTick!.Value, choir.CastEndsTick!.Value, null, null, null, false);
                }
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
            // The ledger's assignment on this tick's pre-step state, per kicker's interrupt id.
            var ledger = new Dictionary<int, int?>();
            foreach (var p in run.State.Actors.Values.Where(a => a.Kind == ActorKind.Player && a.Alive))
            {
                var interruptId = RoleShared.KitOf(view, p.ClassId).InterruptAbilityId;
                if (string.IsNullOrEmpty(interruptId))
                {
                    continue;
                }
                var eligible = run.State.Actors.Values
                    .Where(a => a.Kind == ActorKind.Player && a.Alive)
                    .Where(a => string.Equals(RoleShared.KitOf(view, a.ClassId).InterruptAbilityId, interruptId, StringComparison.Ordinal))
                    .Select(a => a.Id)
                    .ToList();
                ledger[p.Id] = Ledger.ResolveInterrupt(run.State.Actors, interruptId, eligible, tick, view.Kits.PriorityInterruptAbilityIds())?.KickerId;
            }

            var stepped = RunRoot.StepRun(run, commands);
            run = stepped.Run;
            foreach (var e in stepped.Events)
            {
                if (e.Type == "interrupt" && ((JsString)e.Get("abilityId")!).Value == HealCast)
                {
                    var target = (int)((JsInt)e.Get("targetId")!).Value;
                    var source = (int)((JsInt)e.Get("sourceId")!).Value;
                    if (open.Remove(target, out var c))
                    {
                        casts.Add(c with { KickTick = e.Tick, KickerId = source, LedgerKickerId = ledger.TryGetValue(source, out var k) ? k : null });
                    }
                }
                else if (e.Type == "cast_complete" && ((JsString)e.Get("abilityId")!).Value == HealCast)
                {
                    var actor = (int)((JsInt)e.Get("actorId")!).Value;
                    if (open.Remove(actor, out var c))
                    {
                        casts.Add(c with { Completed = true });
                    }
                }
            }
            if (stepped.Events.Any(e => e.Type == "pull_clear"))
            {
                break;
            }
        }
        casts.AddRange(open.Values);
        return casts.OrderBy(c => c.Start).ToList();
    }
}
