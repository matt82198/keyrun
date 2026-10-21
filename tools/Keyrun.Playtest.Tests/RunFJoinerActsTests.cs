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
/// <see cref="BotCommandSource"/>, stepped in SimDriver.StepOnce's order through <see cref="Backfill.Step"/>. Seat 2 is
/// abandoned at tick 1500 (<c>--abandon-seat 2@1500</c>), late in the Choir fight, and one key-2 request joins the queue (<c>--joiner-key 2</c>).
/// The joiner must take the seat at the next pull start and then act: a <c>cast_complete</c> by seat 2 after the fill,
/// within run F's 3500 ticks.
/// </summary>
public sealed class RunFJoinerActsTests
{
    private const int Key = 2;
    private const long Seed = 1;
    private const int SeatId = 2;
    // HudSceneTest $AbandonSeat '2@1500'. Was 1700 (D-PINNED-FIXTURES-362): with the healer press the undisturbed Choir
    // clears at 1580 and the Pair starts at 1693, so 1700 fell inside pull 2; at 1500 the Choir clears at 1607, the Pair
    // starts at 1720 and the seat refills there (measured on seed 1 key 2).
    private const int AbandonTick = 1500;
    private const int MaxTicks = 3500; // run F's --ticks (HudSceneTest $BackfillTicks)

    private sealed record Drive(int? FillTick, List<SimEvent> Events, List<string> Log, Run Final);

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
        var created = RunRoot.CreateRun(Seed, setup.PartyFor(setup.Dungeon), setup.Dungeon, setup.Content, options);
        var run = created with { Encounter = BossRules.Arm(created.Encounter, setup.Boss) };
        var warden = new ScriptedWarden();
        var bots = new BotCommandSource(setup.BotActorIds, KitRegistry.From(registry), Seed);
        var book = BackfillState.Empty;
        var queue = new List<QueueRequest>();
        var events = new List<SimEvent>();
        var log = new List<string>();
        int? fillTick = null;
        var nextSample = 0;

        while (run.State.Tick < MaxTicks && run.RunEnd.ResultTick is null)
        {
            var tick = run.State.Tick;
            if (tick == AbandonTick)
            {
                // SimDriver.MaybeScriptedAbandon: the seat empties and the scripted joiner queues at the run's key.
                book = Backfill.Abandon(book, run, SeatId);
                var clause = run.Encounter.Rules.ActiveClauseId ?? registry.Clauses.Keys.OrderBy(k => k, StringComparer.Ordinal).FirstOrDefault() ?? "none";
                queue.Add(new QueueRequest(setup.Dungeon.Id, Key, clause));
                log.Add($"{tick} SEAT abandon actor={SeatId}");
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
            run = stepped.Step.Run;
            events.AddRange(stepped.Step.Events);
        }
        return new Drive(fillTick, events, log, run);
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

        Assert.True(r.FillTick is not null, $"seat {SeatId} was never refilled (no pull start after the abandon at {AbandonTick})");
        var acts = r.Events.Where(e => e.Type == "cast_complete" && ActorOf(e) == SeatId && e.Tick >= r.FillTick!.Value).ToList();
        Assert.True(acts.Count > 0, $"the refilled seat {SeatId} never completed a cast after its fill at {r.FillTick} (run ended {r.Final.RunEnd.Result?.ToJs() ?? "running"} at {r.Final.State.Tick})");
    }
}
