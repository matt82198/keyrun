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
/// BACKFILL-GATE (STATE.md D27; design/BIBLE.md sections 4 and 10). <see cref="PartyTogetherTests"/>' run C shape on the
/// real layout, with HudSceneTest run F's backfill: seat 2 (the Chanter) is abandoned inside the Choir fight, so it
/// stands idle in choir-room while the Warden and the Cinder clear the Choir and walk on; the pair pull starts with them
/// inside pair-hall, its gate shuts (<c>gate_closed</c>), and <see cref="Backfill.Step"/> refills seat 2 on that tick.
/// The joiner's bot must wait outside the shut gate (no move pressing into the <see cref="NavLayout.GateClamp"/>, clear
/// of the room wall) until <c>gate_open</c>, then walk into pair-hall within <see cref="PartyTogetherTests.WithinTicks"/>.
/// </summary>
public sealed class BackfillGateTests
{
    /// <summary>The joiner's hold must stand at least this far outside the shut room's wall, mm (not against the gate).</summary>
    public const long ClearOfGateMm = 1500;

    /// <summary>The joiner must be holding still (no move or dash command) for this many ticks before <c>gate_open</c>.</summary>
    public const int HoldTicks = 60;

    private const long Seed = 1;
    private const int LeaderId = 1;
    private const int JoinerId = 2;
    private const int MaxTicks = 12000;
    private const int HealthCap = 1500;
    private const string GatedRoom = "pair-hall";

    private sealed record Tick(int At, Vec3 Joiner, bool JoinerMoved);

    private sealed record Drive(List<Tick> Ticks, List<SimEvent> Events, NavLayout Nav, int? FillTick);

    private static readonly Lazy<Drive> Driven = new(DriveRun);

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test with xUnit's output sink (the gate and room_enter ticks are printed for the PR).</summary>
    /// <param name="output">The sink.</param>
    public BackfillGateTests(ITestOutputHelper output) => _output = output;

    private static Drive DriveRun()
    {
        var registry = ContentLoader.Load();
        var layoutDef = registry.Layout;
        var nav = NavLayouts.ToSim(layoutDef);
        var setup = RunSetup.Load(Path.Combine(RolesOracleTraceTests.OracleDir(), "hollow-vault-k2.setup.json"), Seed);
        var capped = setup.Dungeon with
        {
            Pulls = setup.Dungeon.Pulls.Select(p => p with { Spawns = p.Spawns.Select(s => s with { Health = Math.Min(s.Health, HealthCap) }).ToList() }).ToList(),
        };
        SimDungeon dungeon = NavLayouts.PlaceDungeon(layoutDef, capped);
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
        var ticks = new List<Tick>();
        var book = BackfillState.Empty;
        var queue = new List<QueueRequest> { new(dungeon.Id, run.Encounter.KeyLevel, "none") };
        var abandoned = false;
        int? fillTick = null;
        int? stopAt = null;

        while (run.State.Tick < MaxTicks && run.RunEnd.ResultTick is null && (stopAt is null || run.State.Tick < stopAt))
        {
            var tick = run.State.Tick;
            if (!abandoned && run.Encounter.PullIndex == 0 && run.State.Actors.Values.Count(a => a.Alive && a.Kind == ActorKind.Enemy && !a.Idle) == 1)
            {
                // Seat 2 leaves as the Choir's last enemy is fought (HudSceneTest run F abandons late in pull 1 too):
                // earlier, the Warden dies without his healer and the leader becomes the idle open seat.
                book = Backfill.Abandon(book, run, JoinerId);
                abandoned = true;
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
                    // ScriptedWarden's travel: the route, holding for the party, not the tank role.
                    commands.RemoveAll(c => c.ActorId == LeaderId);
                    if (Follow.LeadRoute(tick, leader, run, book.Open.Select(s => s.ActorId).ToList()) is { } walk)
                    {
                        commands.Add(walk);
                    }
                }
                else
                {
                    // ScriptedWarden outside the Choir: he fights where he stands (no approach), casts only.
                    commands.RemoveAll(c => c.ActorId == LeaderId && c is MoveCommand or DashCommand);
                }
            }

            var stepped = Backfill.Step(book, run, commands, queue);
            book = stepped.Backfill;
            foreach (var i in stepped.ConsumedQueueIndices.Reverse())
            {
                queue.RemoveAt(i);
            }
            if (stepped.Fills.Any(f => f.ActorId == JoinerId))
            {
                fillTick = stepped.Step.Run.State.Tick;
            }
            run = stepped.Step.Run;
            events.AddRange(stepped.Step.Events);
            if (fillTick is not null)
            {
                var moved = stepped.Applied.Any(c => c.ActorId == JoinerId && c is MoveCommand or DashCommand);
                ticks.Add(new Tick(run.State.Tick, run.State.Actors[JoinerId].Pos, moved));
            }
            foreach (var e in stepped.Step.Events)
            {
                if (stopAt is null && e.Type == "gate_open" && ((JsString)e.Get("room")!).Value == GatedRoom)
                {
                    stopAt = e.Tick + (2 * PartyTogetherTests.WithinTicks);
                }
            }
        }
        return new Drive(ticks, events, nav, fillTick);
    }

    private static int? GateTick(Drive r, string type) =>
        r.Events.FirstOrDefault(e => e.Type == type && ((JsString)e.Get("room")!).Value == GatedRoom)?.Tick;

    [Fact(DisplayName = "Backfill gate: a joiner refilled while its party is locked in pair-hall waits outside the shut gate, then walks in after gate_open")]
    public void Backfill_joiner_waits_outside_the_shut_gate_until_gate_open()
    {
        var r = Driven.Value;
        foreach (var e in r.Events.Where(e => e.Type is "room_enter" or "pull_start" or "pull_clear" or "gate_closed" or "gate_open" or "run_end" or "death"))
        {
            _output.WriteLine($"{e.Tick} {e.Type} {(e.Type == "room_enter" ? ((JsInt)e.Get("actorId")!).Value + " " + ((JsString)e.Get("room")!).Value : e.Type is "gate_closed" or "gate_open" ? ((JsString)e.Get("room")!).Value : e.Type is "run_end" or "death" ? string.Empty : ((JsString)e.Get("pullId")!).Value)}");
        }
        var closed = GateTick(r, "gate_closed");
        var opened = GateTick(r, "gate_open");
        _output.WriteLine($"fill {r.FillTick} gate_closed {closed} gate_open {opened}");

        // Non-vacuity: the seat is refilled on the tick pair-hall's gate shuts, and the gate stays shut long enough to hold.
        Assert.NotNull(r.FillTick);
        Assert.NotNull(closed);
        Assert.NotNull(opened);
        Assert.Equal(closed, r.FillTick);
        Assert.True(opened!.Value - closed!.Value > 2 * HoldTicks, $"gate shut {closed}..{opened}: too short to observe a hold");

        var room = r.Nav.Boxes.Single(b => b.Id == GatedRoom).Box;
        var shut = r.Ticks.Where(t => t.At >= closed.Value && t.At < opened.Value).ToList();
        Assert.All(shut, t => Assert.False(Movement.Contains(room, t.Joiner), $"tick {t.At}: joiner inside {GatedRoom} while its gate is shut"));

        var last = shut.Where(t => t.At >= opened.Value - HoldTicks).ToList();
        Assert.NotEmpty(last);
        var failures = new List<string>();
        foreach (var t in last)
        {
            var outside = room.MinX - t.Joiner.X;
            if (t.JoinerMoved || outside < ClearOfGateMm)
            {
                failures.Add($"tick {t.At}: joiner at ({t.Joiner.X},{t.Joiner.Z}) {outside} mm outside {GatedRoom}{(t.JoinerMoved ? ", moving into the shut gate" : string.Empty)}");
            }
        }
        Assert.True(failures.Count == 0, $"{failures.Count} of the last {HoldTicks} shut ticks press the gate; first: {failures.FirstOrDefault()}");

        var enter = r.Events.FirstOrDefault(e => e.Type == "room_enter" && ((JsInt)e.Get("actorId")!).Value == JoinerId && ((JsString)e.Get("room")!).Value == GatedRoom && e.Tick >= opened.Value);
        Assert.True(enter is not null, $"the joiner never entered {GatedRoom} after gate_open {opened}");
        _output.WriteLine($"joiner room_enter {GatedRoom} {enter!.Tick}");
        Assert.True(enter.Tick - opened.Value <= PartyTogetherTests.WithinTicks, $"joiner entered {GatedRoom} at {enter.Tick}, {enter.Tick - opened.Value} ticks after gate_open {opened} (budget {PartyTogetherTests.WithinTicks})");
    }
}
