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
/// CROSS-CLASS-KICK-ORDER (finding f-9a4e663c; design/BIBLE.md section 3 "Interrupts resolve by intended tick with a
/// fixed tiebreak, never arrival order", section 4 synergy as a timing game, section 9 The Choir teaches the kick ledger):
/// in a mixed party the kick ledger is the party's, not each class's. One must-kick cast gets one kick, and consecutive
/// casts rotate across seats in a fixed order (least recently kicked first, ties to the lowest seat id), skipping a seat
/// whose interrupt cannot reach the caster. Layout runs only; the oracle runner keeps the frozen per-class TS ledger.
/// </summary>
public sealed class KickOrderTests
{
    private const string HealCast = "choir.healCast";
    private const int ChoirId = 100;
    private const int CastTicks = 80;

    /// <summary>The Choir's 80-tick heal opens for a kick 40 ticks in on a layout run (KickTimingTests).</summary>
    private const int KickOpensAfter = 40;

    /// <summary>Every class's interrupt cooldown in content/data/abilities (24 s at 20 Hz).</summary>
    private const int InterruptCooldownTicks = 480;

    private const long Seed = 1;
    private const int LeaderId = 1;
    private const int MaxTicks = 4000;

    private static readonly Lazy<NavLayout> Nav = new(() => NavLayouts.ToSim(ContentLoader.Load().Layout));

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test with xUnit's output sink (the per-cast kickers are printed for the PR).</summary>
    /// <param name="output">The sink.</param>
    public KickOrderTests(ITestOutputHelper output) => _output = output;

    private static ActorState Choir(int start) => RolesTestKit.Actor(
        id: ChoirId, kind: ActorKind.Enemy, classId: "choir", role: ActorRole.Healer, x: 5000,
        health: 1000, maxHealth: 1000, castId: HealCast, castStartTick: start, castEndsTick: start + CastTicks);

    private static string InterruptOf(string classId)
    {
        var view = RolesTestKit.ViewOf(0, RolesTestKit.RealKits, null);
        return RoleShared.KitOf(view, classId).InterruptAbilityId!;
    }

    private static ActorState Seat(int id, string classId, ActorRole role, int interruptReady = 0, long x = 4000) => RolesTestKit.Actor(
        id: id, classId: classId, role: role, x: x, health: 1000, maxHealth: 1000,
        cooldowns: new[] { (InterruptOf(classId), interruptReady) });

    /// <summary>Every seat that issues a kick at <paramref name="tick"/>, ascending by id.</summary>
    private static List<int> Kickers(int tick, NavLayout? layout, params ActorState[] actors)
    {
        var view = RolesTestKit.ViewOf(tick, RolesTestKit.RealKits, null, actors) with { Layout = layout };
        var kickers = new List<int>();
        foreach (var self in view.State.Actors.Values.Where(a => a.Kind == ActorKind.Player))
        {
            if (RoleShared.LedgerKickCommand(view, self, RoleShared.KitOf(view, self.ClassId)) is { } kick)
            {
                Assert.Equal(ChoirId, kick.TargetId);
                Assert.Equal(InterruptOf(self.ClassId), kick.AbilityId);
                kickers.Add(self.Id);
            }
        }
        return kickers;
    }

    [Fact(DisplayName = "Kick order: a mixed party (Warden 1, Chanter 2, Cinder 3) kicks one Choir cast once, seat 1 first")]
    public void Mixed_party_kicks_one_cast_once()
    {
        var open = 100 + KickOpensAfter;
        var kickers = Kickers(open, Nav.Value,
            Choir(100), Seat(1, "warden", ActorRole.Tank), Seat(2, "chanter", ActorRole.Healer), Seat(3, "cinder", ActorRole.Damage));
        Assert.Equal(new[] { 1 }, kickers);
    }

    [Theory(DisplayName = "Kick order: consecutive Choir casts are kicked by different seats, rotating 1, 2, 3, 1, 2, 3")]
    [InlineData(392)]
    [InlineData(600)]
    public void Consecutive_casts_rotate_across_seats(int cadence)
    {
        var classes = new SortedDictionary<int, (string ClassId, ActorRole Role)>
        {
            [1] = ("warden", ActorRole.Tank),
            [2] = ("chanter", ActorRole.Healer),
            [3] = ("cinder", ActorRole.Damage),
        };
        var ready = new SortedDictionary<int, int> { [1] = 0, [2] = 0, [3] = 0 };
        var order = new List<int>();
        for (var i = 0; i < 6; i++)
        {
            var start = 100 + (i * cadence);
            var open = start + KickOpensAfter;
            var actors = new List<ActorState> { Choir(start) };
            actors.AddRange(classes.Select(kv => Seat(kv.Key, kv.Value.ClassId, kv.Value.Role, ready[kv.Key])));
            var kickers = Kickers(open, Nav.Value, actors.ToArray());
            Assert.True(kickers.Count == 1, $"cast {i} at {start}: {kickers.Count} seats kicked ({string.Join(",", kickers)}), want exactly one");
            order.Add(kickers[0]);
            ready[kickers[0]] = open + InterruptCooldownTicks;
        }
        Assert.Equal(new[] { 1, 2, 3, 1, 2, 3 }, order);
    }

    [Fact(DisplayName = "Kick order: a seat whose interrupt cannot reach the caster is skipped (Warden's 3 m Shield Bash at 5 m)")]
    public void Out_of_range_seat_is_skipped()
    {
        var open = 100 + KickOpensAfter;
        var kickers = Kickers(open, Nav.Value,
            Choir(100), Seat(1, "warden", ActorRole.Tank, x: 0), Seat(2, "chanter", ActorRole.Healer), Seat(3, "cinder", ActorRole.Damage));
        Assert.Equal(new[] { 2 }, kickers);
    }

    [Fact(DisplayName = "Kick order: the tiebreak is the seat id, not the class (Cinder 1, Chanter 2, Warden 3 kicks with seat 1)")]
    public void Tiebreak_is_the_seat_id()
    {
        var open = 100 + KickOpensAfter;
        var kickers = Kickers(open, Nav.Value,
            Choir(100), Seat(1, "cinder", ActorRole.Damage), Seat(2, "chanter", ActorRole.Healer), Seat(3, "warden", ActorRole.Tank));
        Assert.Equal(new[] { 1 }, kickers);
    }

    /// <summary>
    /// One Choir heal cast as the run saw it. <paramref name="LandedKicks"/> counts the "interrupt" events on it (a kick
    /// counts when it lands, R-BOTS-1); <paramref name="KickCommands"/> counts the presses aimed at it, printed only: a
    /// seat re-pressing inside the spell-queue window (main #370) is ordinary input, not a second kick.
    /// <paramref name="CasterDied"/> marks a cast that ended in cast_stopped on its caster's death tick (R-DEADCAST-1):
    /// it never resolves, so no kick is owed (R-BOTS-2).
    /// </summary>
    private sealed record CastSeen(int Start, int? KickTick, int? KickerId, int LandedKicks, int KickCommands, int MaxKicksOneTick, bool Completed, bool CasterDied);

    [Fact(DisplayName = "Kick order: run C's Choir pull, every heal cast gets one kick and consecutive casts are kicked by different seats")]
    public void Choir_pull_kicks_rotate_with_no_double_kick()
    {
        var casts = DriveChoirPull();
        foreach (var c in casts)
        {
            _output.WriteLine($"healCast start={c.Start} kick={c.KickTick?.ToString() ?? "none"} kicker={c.KickerId?.ToString() ?? "none"} landed={c.LandedKicks} kickCommands={c.KickCommands} maxOneTick={c.MaxKicksOneTick} completed={c.Completed} casterDied={c.CasterDied}");
        }
        var owed = casts.Where(c => !c.CasterDied).ToList();
        Assert.True(owed.Count >= 3, $"only {owed.Count} Choir heal casts owed a kick ({casts.Count} seen); the rotation check would be vacuous");
        var failures = new List<string>();
        foreach (var c in owed)
        {
            if (c.KickerId is null)
            {
                failures.Add($"cast {c.Start} was never kicked (completed={c.Completed})");
            }
            if (c.LandedKicks != 1)
            {
                failures.Add($"cast {c.Start} took {c.LandedKicks} landed kicks ({c.KickCommands} presses), want exactly one");
            }
        }
        for (var i = 1; i < owed.Count; i++)
        {
            if (owed[i].KickerId is { } k && k == owed[i - 1].KickerId)
            {
                failures.Add($"casts {owed[i - 1].Start} and {owed[i].Start} were both kicked by seat {k}");
            }
        }
        Assert.True(failures.Count == 0, string.Join("; ", failures));
    }

    /// <summary>
    /// Run C's shape (PartyTogetherTests, KickTimingTests): the hollow-vault k2 oracle setup placed into the real layout,
    /// three seats (Warden 1, Chanter 2, Cinder 3), all real bot roles at skill 80, the leader walking the route while no
    /// enemy lives and holding in a fight. Counts every interrupt command aimed at a casting Choir. Stops at pull_clear.
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
                    open[choir.Id] = new CastSeen(choir.CastStartTick!.Value, null, null, 0, 0, 0, false, false);
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
            foreach (var target in open.Keys.ToList())
            {
                var kicks = commands.OfType<CastCommand>().Count(c =>
                    c.TargetId == target
                    && run.State.Actors.TryGetValue(c.ActorId, out var p)
                    && string.Equals(RoleShared.KitOf(view, p.ClassId).InterruptAbilityId, c.AbilityId, StringComparison.Ordinal));
                if (kicks > 0)
                {
                    var seen = open[target];
                    open[target] = seen with { KickCommands = seen.KickCommands + kicks, MaxKicksOneTick = Math.Max(seen.MaxKicksOneTick, kicks) };
                }
            }

            var stepped = RunRoot.StepRun(run, commands);
            run = stepped.Run;
            // A cast stays open until its caster no longer holds that same cast after the step; every interrupt that
            // lands on it while open is tallied, so a second landed kick on one cast (by any seat) counts as two.
            var diedThisStep = stepped.Events.Where(e => e.Type == "death").Select(e => (int)((JsInt)e.Get("actorId")!).Value).ToHashSet();
            foreach (var e in stepped.Events)
            {
                if (e.Type == "interrupt" && ((JsString)e.Get("abilityId")!).Value == HealCast)
                {
                    var target = (int)((JsInt)e.Get("targetId")!).Value;
                    var source = (int)((JsInt)e.Get("sourceId")!).Value;
                    if (open.TryGetValue(target, out var c))
                    {
                        open[target] = c with { KickTick = c.KickTick ?? e.Tick, KickerId = c.KickerId ?? source, LandedKicks = c.LandedKicks + 1 };
                    }
                }
                else if (e.Type == "cast_complete" && ((JsString)e.Get("abilityId")!).Value == HealCast)
                {
                    var actor = (int)((JsInt)e.Get("actorId")!).Value;
                    if (open.TryGetValue(actor, out var c))
                    {
                        open[actor] = c with { Completed = true };
                    }
                }
                else if (e.Type == "cast_stopped" && ((JsString)e.Get("abilityId")!).Value == HealCast)
                {
                    // R-BOTS-2: a cast stopped on its caster's death tick owes no kick. Any other stop stays owed.
                    var actor = (int)((JsInt)e.Get("actorId")!).Value;
                    if (diedThisStep.Contains(actor) && open.TryGetValue(actor, out var c))
                    {
                        open[actor] = c with { CasterDied = true };
                    }
                }
            }
            foreach (var (casterId, c) in open.ToList())
            {
                var stillCasting = run.State.Actors.TryGetValue(casterId, out var caster)
                    && caster.Alive && caster.CastId == HealCast && caster.CastStartTick == c.Start;
                if (!stillCasting)
                {
                    open.Remove(casterId);
                    casts.Add(c);
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

/// <summary>
/// KICK-ORDER rule (design/BIBLE.md section 3 "A suggested kick order", section 10 kick-ready readout): the pure order
/// the party's kicks come off cooldown, one place per kick slot. Ready seats first, then soonest ready, then seat id.
/// </summary>
public sealed class KickOrderRuleTests
{
    [Fact(DisplayName = "Kick order rule: a seat already off cooldown kicks before one still cooling, whatever its seat id")]
    public void Ready_seat_comes_before_cooling_seat()
    {
        var order = KickOrder.Compute(new[] { new KickSeat(1, 500), new KickSeat(2, 100) }, 200);
        Assert.Equal(new[] { 2, 1 }, order.Select(s => s.SeatId).ToArray());
        Assert.Equal(new[] { true, false }, order.Select(s => s.Ready).ToArray());
    }

    [Fact(DisplayName = "Kick order rule: among ready seats the soonest ready (earliest ready tick) kicks first")]
    public void Ready_seats_soonest_ready_first()
    {
        var order = KickOrder.Compute(new[] { new KickSeat(1, 150), new KickSeat(2, 90), new KickSeat(3, 120) }, 200);
        Assert.Equal(new[] { 2, 3, 1 }, order.Select(s => s.SeatId).ToArray());
    }

    [Fact(DisplayName = "Kick order rule: among cooling seats the soonest ready kicks first")]
    public void Cooling_seats_soonest_ready_first()
    {
        var order = KickOrder.Compute(new[] { new KickSeat(1, 400), new KickSeat(2, 300), new KickSeat(3, 250) }, 200);
        Assert.Equal(new[] { 3, 2, 1 }, order.Select(s => s.SeatId).ToArray());
        Assert.All(order, s => Assert.False(s.Ready));
    }

    [Fact(DisplayName = "Kick order rule: equal ready ticks break to the lowest seat id, whatever the input order")]
    public void Tie_breaks_to_seat_id()
    {
        var forward = KickOrder.Compute(new[] { new KickSeat(1, 0), new KickSeat(2, 0), new KickSeat(3, 0) }, 0);
        var reversed = KickOrder.Compute(new[] { new KickSeat(3, 0), new KickSeat(2, 0), new KickSeat(1, 0) }, 0);
        Assert.Equal(new[] { 1, 2, 3 }, forward.Select(s => s.SeatId).ToArray());
        Assert.Equal(forward, reversed);
    }

    [Fact(DisplayName = "Kick order rule: a kick ready exactly at the tick is ready now")]
    public void Ready_at_exact_tick_counts_as_ready()
    {
        var order = KickOrder.Compute(new[] { new KickSeat(1, 201), new KickSeat(2, 200) }, 200);
        Assert.Equal(new[] { 2, 1 }, order.Select(s => s.SeatId).ToArray());
        Assert.Equal(new[] { true, false }, order.Select(s => s.Ready).ToArray());
    }

    [Fact(DisplayName = "Kick order rule: places are 1..N in order, one per seat, and no seats gives no places")]
    public void Places_are_one_to_n()
    {
        var order = KickOrder.Compute(new[] { new KickSeat(4, 0), new KickSeat(2, 900), new KickSeat(9, 50) }, 100);
        Assert.Equal(new[] { 1, 2, 3 }, order.Select(s => s.Order).ToArray());
        Assert.Equal(new[] { 4, 9, 2 }, order.Select(s => s.SeatId).ToArray());
        Assert.Empty(KickOrder.Compute(Array.Empty<KickSeat>(), 100));
    }

    [Fact(DisplayName = "Kick order rule: casters take the kick places in the order given, and a caster past the last place gets no number")]
    public void Casters_take_places_in_order()
    {
        var order = KickOrder.Compute(new[] { new KickSeat(1, 0), new KickSeat(2, 0) }, 0);
        var numbers = KickOrder.NumberCasters(new[] { 10, 11, 12 }, order);
        Assert.Equal(1, numbers[10]);
        Assert.Equal(2, numbers[11]);
        Assert.False(numbers.ContainsKey(12));
    }
}
