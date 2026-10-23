using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;
using Xunit.Abstractions;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// HudSceneTest run F, headless (design/BIBLE.md section 10: "Strangers are chill by default ... backfill instead of
/// abandonment"): the run SimDriver builds at key 2, seed 1 (<see cref="HostRunSetup.Build"/>, then
/// <see cref="BossRules.Arm"/>), seat 1 the autorun <see cref="ScriptedWarden"/> and seats 2 and 3 the host's own
/// <see cref="BotCommandSource"/>, stepped in SimDriver.StepOnce's order through <see cref="Backfill.Step"/>. Seat 2
/// abandons during the first pull, one tick after the Choir is down to a tenth of its health (<c>--abandon-seat
/// 2@pull0.hp10+50</c> under R-K4-4 (abandon one tick before the only ready kick stalled pull 0 on the AoE kit; was 2@pull0.hp10+1, finding f-0fd3e6fb on RULING R-415-D): the scenario is "a seat abandons during the first pull", so
/// the leave is pinned to the Choir's own progress, not to a run tick or an offset a pacing change moves into the Pair),
/// and one key-2 request joins the queue (<c>--joiner-key 2</c>). The joiner
/// must take the seat at the next pull start and then act: a <c>cast_complete</c> by seat 2 after the fill, within run
/// F's 3500 ticks.
/// </summary>
/// <remarks>
/// Merge-forward of #362 onto #415: #362 had moved the leave 1700 -> 1500 (D-PINNED-FIXTURES-362, the healer press
/// cleared the undisturbed Choir at 1580 so 1700 fell inside the Pair). The pull-relative form keeps that intent (the
/// leave stays inside the Choir) and <see cref="The_abandon_falls_inside_the_first_pull"/> checks it on this tree.
/// f-0fd3e6fb: 2@pull0+1350 still flipped whenever the Choir's fight length changed (the setup lesson, #364, #326);
/// 2@pull0.hp10+50 (R-K4-4, was 2@pull0.hp10+1) leaves late in the Choir whatever its length (RunFAbandonPacingTests). Measured on this tree (seed 1,
/// key 2, bot gear): the party reaches the Pair with the leave at 20% (tick 1343) and below, and wipes at 25% (1308, the
/// Warden dies at 1649); a leave at the Choir's first death (1031) wipes too (the Warden dies at 1454), which is why the
/// anchor is the Choir's health share and not its first death.
/// </remarks>
public sealed class RunFJoinerActsTests
{
    private const int Key = 2;
    private const long Seed = 1;
    private const int SeatId = 2;
    internal const string Abandon = "2@pull0.hp10+50"; // R-K4-4 pin, HudSceneTest $AbandonSeat (was 2@pull0.hp10+1 and 2@pull0+1350, f-0fd3e6fb, see remarks)
    private const int MaxTicks = 3500; // run F's --ticks (HudSceneTest $BackfillTicks)

    private sealed record Drive(int? AbandonTick, int? FillTick, List<SimEvent> Events, List<string> Log, Run Final);

    private static readonly Lazy<Drive> Driven = new(DriveRunF);

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test with xUnit's output sink (the post-fill trace is printed for the PR).</summary>
    /// <param name="output">The sink.</param>
    public RunFJoinerActsTests(ITestOutputHelper output) => _output = output;

    private static Drive DriveRunF()
    {
        var registry = ContentLoader.Load();
        var setup = HostRunSetup.Build(registry, "hollow-vault", Key, "warden", withBots: true);
        var start = setup.StartFor(null, null, registry, "warden");
        var options = (start.Options ?? new RunOptions()) with { RotationIndex = setup.RotationIndex };
        // As SimDriver.StartRun: the bot seats wear their default gear (HostRunSetup.WithBotLoadouts), so this replica paces
        // run F as the host does (without it the Pair started at 1645 here while the host's started at 2038).
        var party = setup.PartyFor(setup.Dungeon);
        options = setup.WithBotLoadouts(options, party) ?? options;
        var created = RunRoot.CreateRun(Seed, party, setup.Dungeon, setup.Content, options);
        var run = created with { Encounter = BossRules.Arm(created.Encounter, setup.Boss) };
        var warden = new ScriptedWarden();
        var bots = new BotCommandSource(setup.BotActorIds, KitRegistry.From(registry), Seed);
        var book = BackfillState.Empty;
        var queue = new List<QueueRequest>();
        var events = new List<SimEvent>();
        var log = new List<string>();
        int? fillTick = null;
        int? abandonTick = null;
        int? anchor = null;
        var plan = SeatAbandonPlan.Parse(Abandon);
        var nextSample = 0;

        while (run.State.Tick < MaxTicks && run.RunEnd.ResultTick is null)
        {
            var tick = run.State.Tick;
            if (tick == plan.ResolveTick(anchor))
            {
                // SimDriver.MaybeScriptedAbandon: the seat empties and the scripted joiner queues at the run's key.
                book = Backfill.Abandon(book, run, SeatId);
                var clause = run.Encounter.Rules.ActiveClauseId ?? registry.Clauses.Keys.OrderBy(k => k, StringComparer.Ordinal).FirstOrDefault() ?? "none";
                queue.Add(new QueueRequest(setup.Dungeon.Id, Key, clause));
                abandonTick = tick;
                log.Add($"{tick} SEAT abandon actor={SeatId} pull={plan.AnchorPull} {plan.AnchorFields(anchor)} offset={plan.Tick}");
            }
            var commands = new List<Command>();
            warden.Sample(tick, run, commands, book.Open.Select(s => s.ActorId).ToList());
            bots.Sample(tick, run, commands);
            if (fillTick is not null && tick >= nextSample)
            {
                var a = run.State.Actors[SeatId];
                log.Add($"{tick} seat{SeatId} alive={a.Alive} idle={a.Idle} hp={a.Health} pos=({a.Pos.X},{a.Pos.Z}) target={a.TargetId} cast={a.CastId} cmds=[{string.Join(",", commands.Where(c => c.ActorId == SeatId).Select(c => c.GetType().Name + (c is CastCommand cc ? ":" + cc.AbilityId + "->" + cc.TargetId : string.Empty)))}]");
                nextSample = tick + 50;
            }
            var stepped = Backfill.Step(book, run, commands, queue);
            book = stepped.Backfill;
            foreach (var i in stepped.ConsumedQueueIndices.Reverse())
            {
                queue.RemoveAt(i);
            }
            foreach (var fill in stepped.Fills)
            {
                log.Add($"{tick} SEAT refill actor={fill.ActorId} tick={fill.FillTick} key={fill.Joiner.KeyLevel}");
                if (fill.ActorId == SeatId)
                {
                    fillTick = fill.FillTick;
                    nextSample = fill.FillTick;
                }
            }
            // SimDriver.StepOnce: the anchor is the first step that ends with the plan's pull down to its health share,
            // kept once seen.
            anchor ??= plan.AnchorAt(tick, stepped.Step.Events, stepped.Step.Run.Dungeon, stepped.Step.Run.State.Actors);
            run = stepped.Step.Run;
            events.AddRange(stepped.Step.Events);
        }
        return new Drive(abandonTick, fillTick, events, log, run);
    }

    private static long ActorOf(SimEvent e) => e.Get("actorId") is JsInt id ? id.Value : -1;

    [Fact(DisplayName = "Run F: the refilled seat casts after its fill (a joiner's commands reach the run)")]
    public void The_refilled_seat_acts_after_the_fill()
    {
        var r = Driven.Value;
        foreach (var line in r.Log)
        {
            _output.WriteLine(line);
        }
        foreach (var e in r.Events.Where(e => e.Type is "pull_start" or "pull_clear" or "death" or "run_end" || (e.Type == "cast_complete" && ActorOf(e) == SeatId)))
        {
            _output.WriteLine($"{e.Tick} {e.Type} actor={ActorOf(e)} {(e.Get("abilityId") is JsString ab ? ab.Value : string.Empty)}");
        }

        Assert.True(r.FillTick is not null, $"seat {SeatId} was never refilled (no pull start after the abandon at {r.AbandonTick})");
        var acts = r.Events.Where(e => e.Type == "cast_complete" && ActorOf(e) == SeatId && e.Tick >= r.FillTick!.Value).ToList();
        Assert.True(acts.Count > 0, $"the refilled seat {SeatId} never completed a cast after its fill at {r.FillTick} (run ended {r.Final.RunEnd.Result?.ToJs() ?? "running"} at {r.Final.State.Tick})");
    }

    [Fact(DisplayName = "Run F: the seat abandons during the first pull (after its pull_start, before its pull_clear)")]
    public void The_abandon_falls_inside_the_first_pull()
    {
        var r = Driven.Value;
        var start = r.Events.First(e => e.Type == "pull_start" && e.Get("pullIndex") is JsInt { Value: 0 }).Tick;
        var clear = r.Events.FirstOrDefault(e => e.Type == "pull_clear")?.Tick;
        _output.WriteLine($"abandon={r.AbandonTick} pull0 start={start} clear={clear?.ToString() ?? "none"}");
        Assert.True(r.AbandonTick is not null, $"seat {SeatId} never abandoned ({Abandon})");
        Assert.True(r.AbandonTick > start && (clear is null || r.AbandonTick < clear),
            $"the abandon at {r.AbandonTick} is outside the first pull (pull_start {start}, pull_clear {clear})");
    }

    /// <summary>
    /// HudSceneTest run F check 23 ("the open seat stands idle"): no cast_complete by the abandoned seat between the
    /// abandon and the refill. keyrun #540 CI: with the filler rotation the leave at 2@pull0.hp10+1 fell while the
    /// Chanter was mid-way through Cadence (castTicks 20), and the cast begun before the leave completed after it
    /// ("EVENT {"abilityId":"chanter.cadence","actorId":2,"tick":1593,"type":"cast_complete"}"): an empty seat acted.
    /// </summary>
    [Fact(DisplayName = "Run F: the open seat stands idle between the abandon and the refill (HudSceneTest run F check 23)")]
    public void The_open_seat_stands_idle_until_the_refill()
    {
        var r = Driven.Value;
        Assert.True(r.AbandonTick is not null && r.FillTick is not null, $"run F never abandoned and refilled seat {SeatId} (abandon {r.AbandonTick}, fill {r.FillTick})");
        var acted = r.Events.Where(e => e.Type == "cast_complete" && ActorOf(e) == SeatId && e.Tick >= r.AbandonTick!.Value && e.Tick < r.FillTick!.Value).ToList();
        _output.WriteLine($"abandon={r.AbandonTick} fill={r.FillTick} seat{SeatId} casts between={acted.Count}");
        Assert.True(
            acted.Count == 0,
            $"the open seat {SeatId} acted between the abandon at {r.AbandonTick} and the refill at {r.FillTick}: "
            + string.Join(", ", acted.Select(e => $"{(e.Get("abilityId") as JsString)?.Value}@{e.Tick}")));
    }

    [Fact(DisplayName = "Run F: the party reaches pull 2 with no seat dead (HudSceneTest run F check 23)")]
    public void No_party_seat_dies_before_pull_2()
    {
        var r = Driven.Value;
        var pull2 = r.Events.FirstOrDefault(e => e.Type == "pull_start" && e.Get("pullIndex") is JsInt { Value: 1 })?.Tick;
        Assert.True(pull2 is not null, $"the party never reached pull 2 after the abandon at {r.AbandonTick} (run ended {r.Final.RunEnd.Result?.ToJs() ?? "running"} at {r.Final.State.Tick})");
        var dead = r.Events.FirstOrDefault(e => e.Type == "death" && ActorOf(e) is >= 1 and <= 3 && e.Tick <= pull2);
        Assert.True(dead is null, $"party seat {(dead is null ? -1 : ActorOf(dead))} died at {dead?.Tick} before pull 2 at {pull2} (abandon at {r.AbandonTick})");
    }
}
