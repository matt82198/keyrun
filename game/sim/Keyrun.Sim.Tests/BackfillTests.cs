using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// ENGINE-PLAN 3G backfill (BIBLE s10 "a slot that empties mid-run is offered to the queue at the same key; a joiner
/// arrives at the next pull start with the run's Slack"; BIBLE s2 "The queue matches on key level only").
/// The fixture is a four-pull key-10 run whose tank clears pull 0 at tick 5, pull 1 at tick 40 and pull 2 at tick 100;
/// seat 3 (the Cinder) moves every tick, abandons at tick 60 (pull 2), and its seat is offered at the pull 3 start.
/// </summary>
public sealed class BackfillTests
{
    private const long Seed = 30007;
    private const int RunKey = 10;
    private const int Tank = 1;
    private const int Healer = 2;
    private const int Seat = 3;
    private const int AbandonTick = 60;
    private const int EndTick = 160;

    private static readonly int[] KillTicks = { 5, 40, 100 };

    private const int HealerDeathTick = 80;

    private static readonly QueueRequest Key10 = new("backfill-vault", RunKey, "echo");
    private static readonly QueueRequest Key2 = new("backfill-vault", 2, "tide");

    private static Run BuildRun()
    {
        var abilities = new Dictionary<string, AbilityDef>(StringComparer.Ordinal)
        {
            ["nuke"] = RunTestKit.Ability("nuke", new EffectOp[] { new DamageOp(1000, "physical") }),
        };
        var content = RunTestKit.Content(abilities, lockoutTicks: 0) with { KeyLevel = RunKey };
        var pulls = Enumerable.Range(0, 4)
            .Select(i => new PullDef("p" + i, new[] { RunTestKit.Spawn(100 + i, "dummy", 3000, 0, 100) }))
            .ToList();
        return RunRoot.CreateRun(
            Seed,
            new[]
            {
                RunTestKit.Member(Tank, "warden", ActorRole.Tank, 0, 0, 500, 100),
                RunTestKit.Member(Healer, "chanter", ActorRole.Healer, -2100, 0, 500, 100),
                RunTestKit.Member(Seat, "cinder", ActorRole.Damage, -700, 0, 500, 100),
            },
            new DungeonDef("backfill-vault", pulls),
            content);
    }

    /// <summary>This tick's scripted commands: the tank's nukes, and seat 3 walking +x/-x every tick (always moving).</summary>
    private static List<Command> CommandsAt(int tick)
    {
        var list = new List<Command>();
        var kill = Array.IndexOf(KillTicks, tick);
        if (kill >= 0)
        {
            list.Add(new CastCommand(tick, Tank, "nuke", 100 + kill));
        }
        if (tick == HealerDeathTick)
        {
            // A real death while the seat is open, so the run's Slack moves between the abandon and the refill.
            list.Add(new CastCommand(tick, Tank, "nuke", Healer));
        }
        list.Add(new MoveCommand(tick, Seat, new Dir2(tick % 2 == 0 ? 1000 : -1000, 0), 256));
        return list;
    }

    private sealed record Drive(
        Run Run,
        BackfillState Backfill,
        List<SeatFill> Fills,
        List<int> SeatActedTicks,
        Dictionary<int, int> SlackAt,
        Dictionary<int, Vec3> SeatPosAt,
        Dictionary<int, Vec3> TankPosAt,
        int? PullStart3Tick,
        List<QueueRequest> QueueLeft);

    /// <summary>Drives the fixture through <see cref="Backfill.Step"/> to <see cref="EndTick"/>.</summary>
    private static Drive DriveRun(int? abandonTick, IReadOnlyList<QueueRequest> queue)
    {
        var run = BuildRun();
        var backfill = BackfillState.Empty;
        var waiting = queue.ToList();
        var fills = new List<SeatFill>();
        var acted = new List<int>();
        var slack = new Dictionary<int, int>();
        var pos = new Dictionary<int, Vec3>();
        var tankPos = new Dictionary<int, Vec3>();
        int? pullStart3 = null;
        while (run.State.Tick < EndTick)
        {
            var tick = run.State.Tick;
            slack[tick] = run.Encounter.SlackTicks;
            pos[tick] = run.State.Actors[Seat].Pos;
            tankPos[tick] = run.State.Actors[Tank].Pos;
            if (tick == abandonTick)
            {
                Assert.Equal(2, run.Encounter.PullIndex);
                backfill = Backfill.Abandon(backfill, run, Seat);
            }
            var step = Backfill.Step(backfill, run, CommandsAt(tick), waiting);
            if (step.Applied.Any(c => c.ActorId == Seat))
            {
                acted.Add(tick);
            }
            foreach (var index in step.ConsumedQueueIndices.OrderByDescending(i => i))
            {
                waiting.RemoveAt(index);
            }
            fills.AddRange(step.Fills);
            if (step.Step.Events.Any(e => e.Type == "pull_start" && e.Get("pullIndex") is JsInt { Value: 3 }))
            {
                pullStart3 = step.Step.Run.State.Tick;
            }
            backfill = step.Backfill;
            run = step.Step.Run;
        }
        slack[run.State.Tick] = run.Encounter.SlackTicks;
        pos[run.State.Tick] = run.State.Actors[Seat].Pos;
        tankPos[run.State.Tick] = run.State.Actors[Tank].Pos;
        return new Drive(run, backfill, fills, acted, slack, pos, tankPos, pullStart3, waiting);
    }

    [Fact]
    public void A_scripted_abandon_at_pull_2_is_refilled_at_the_next_pull_start()
    {
        var d = DriveRun(AbandonTick, new[] { Key10 });

        Assert.NotNull(d.PullStart3Tick);
        var fill = Assert.Single(d.Fills);
        Assert.Equal(Seat, fill.ActorId);
        Assert.Same(Key10, fill.Joiner);
        Assert.Equal(AbandonTick, fill.AbandonTick);
        Assert.Equal(3, fill.PullIndex);
        Assert.Equal(d.PullStart3Tick, fill.FillTick);
        Assert.Empty(d.Backfill.Open);
        Assert.Equal(fill, Assert.Single(d.Backfill.Filled));
        Assert.Empty(d.QueueLeft);
        Assert.True(d.Run.State.Actors[Seat].Alive);
    }

    [Fact]
    public void The_joiner_first_acts_at_the_next_pull_start_and_the_seat_is_idle_until_then()
    {
        var d = DriveRun(AbandonTick, new[] { Key10 });
        var fillTick = d.PullStart3Tick!.Value;
        Assert.True(fillTick > AbandonTick);

        // The leaver acted up to the abandon; nobody acts in the seat from the abandon until the pull start.
        var after = d.SeatActedTicks.Where(t => t >= AbandonTick).ToList();
        Assert.Equal(fillTick, after.First());
        Assert.Equal(Enumerable.Range(0, AbandonTick), d.SeatActedTicks.Where(t => t < AbandonTick));
        Assert.Equal(Enumerable.Range(fillTick, EndTick - fillTick), after);

        // The seat's actor stands still while open; at the pull start the joiner arrives with the party (BIBLE s9.1 and
        // s10, "a joiner arrives at the next pull start ... inside the party's taken branch": on the staying member nearest
        // the leaver, here the tank, the healer having died at 80); then it moves on the joiner's first command.
        for (var t = AbandonTick; t < fillTick; t++)
        {
            Assert.Equal(d.SeatPosAt[AbandonTick], d.SeatPosAt[t]);
        }
        Assert.False(d.Run.State.Actors[Healer].Alive);
        Assert.Equal(d.TankPosAt[fillTick], d.SeatPosAt[fillTick]);
        Assert.NotEqual(d.SeatPosAt[AbandonTick], d.SeatPosAt[fillTick]);
        Assert.NotEqual(d.SeatPosAt[fillTick], d.SeatPosAt[fillTick + 1]);
    }

    [Fact]
    public void The_runs_Slack_is_unchanged_by_the_abandon_and_the_refill()
    {
        var d = DriveRun(AbandonTick, new[] { Key10 });
        var control = DriveRun(null, Array.Empty<QueueRequest>());
        var fill = Assert.Single(d.Fills);

        // Every tick's Slack equals the no-abandon control run's, through the abandon, the open seat and the refill.
        Assert.Equal(control.SlackAt.Keys.OrderBy(k => k), d.SlackAt.Keys.OrderBy(k => k));
        foreach (var (tick, slack) in control.SlackAt)
        {
            Assert.Equal(slack, d.SlackAt[tick]);
        }
        Assert.Equal(control.Run.Encounter.SlackTicks, d.Run.Encounter.SlackTicks);

        // The joiner arrives with the run's current Slack. STATE.md D46: the healer's death (tick 80) adds time to the
        // run clock and no longer drains the bank, so the bank is the abandon-time bank and the death time is kept.
        Assert.False(d.Run.State.Actors[Healer].Alive);
        Assert.Equal(d.SlackAt[AbandonTick], d.SlackAt[fill.FillTick]);
        Assert.True(d.Run.Encounter.PenaltyTicks > 0);
        Assert.Equal(control.Run.Encounter.PenaltyTicks, d.Run.Encounter.PenaltyTicks);
        Assert.Equal(d.SlackAt[fill.FillTick], fill.SlackTicks);
        Assert.Equal(d.Run.Encounter.SlackTicks, fill.SlackTicks);
    }

    [Fact]
    public void A_joiner_queued_at_key_2_is_refused_a_key_10_run()
    {
        Assert.False(Backfill.Admits(RunKey, Key2));
        Assert.True(Backfill.Admits(RunKey, Key10));
        Assert.Equal(-1, Backfill.PickJoiner(RunKey, new[] { Key2 }));
        Assert.Equal(1, Backfill.PickJoiner(RunKey, new[] { Key2, Key10 }));

        var refused = DriveRun(AbandonTick, new[] { Key2 });
        Assert.Empty(refused.Fills);
        Assert.Equal(Seat, Assert.Single(refused.Backfill.Open).ActorId);
        Assert.Equal(new[] { Key2 }, refused.QueueLeft);
        Assert.DoesNotContain(refused.SeatActedTicks, t => t >= AbandonTick);

        var skipped = DriveRun(AbandonTick, new[] { Key2, Key10 });
        Assert.Same(Key10, Assert.Single(skipped.Fills).Joiner);
        Assert.Equal(new[] { Key2 }, skipped.QueueLeft);
    }

    [Fact]
    public void The_key_band_matches_on_the_exact_key_inside_two_to_twenty()
    {
        for (var key = ScreenState.MinKey; key <= ScreenState.MaxKey; key++)
        {
            Assert.True(Backfill.Admits(key, new QueueRequest("any-dungeon", key, "echo")));
            Assert.False(Backfill.Admits(key, new QueueRequest("any-dungeon", key + 1, "echo")));
            Assert.False(Backfill.Admits(key, new QueueRequest("any-dungeon", key - 1, "echo")));
        }
        Assert.False(Backfill.Admits(1, new QueueRequest("any-dungeon", 1, "echo")));
        Assert.False(Backfill.Admits(21, new QueueRequest("any-dungeon", 21, "echo")));
    }

    [Fact]
    public void Same_seed_and_same_abandon_tick_give_the_same_refill_twice()
    {
        var a = DriveRun(AbandonTick, new[] { Key2, Key10 });
        var b = DriveRun(AbandonTick, new[] { Key2, Key10 });

        Assert.Equal(Assert.Single(a.Fills), Assert.Single(b.Fills));
        Assert.Equal(a.SeatActedTicks, b.SeatActedTicks);
        Assert.Equal(
            Projection.Line(a.Run.State, a.Run.Encounter.PullIndex, a.Run.Encounter.SlackTicks),
            Projection.Line(b.Run.State, b.Run.Encounter.PullIndex, b.Run.Encounter.SlackTicks));
    }

    [Fact]
    public void Abandon_records_the_seat_without_touching_the_run()
    {
        var run = BuildRun();
        var before = Projection.Line(run.State, run.Encounter.PullIndex, run.Encounter.SlackTicks);
        var s = Backfill.Abandon(BackfillState.Empty, run, Seat);

        var open = Assert.Single(s.Open);
        Assert.Equal(new SeatAbandon(Seat, 0, 0, run.Encounter.SlackTicks), open);
        Assert.Equal(before, Projection.Line(run.State, run.Encounter.PullIndex, run.Encounter.SlackTicks));
        Assert.True(run.State.Actors[Seat].Alive);
    }

    [Fact]
    public void Abandon_refuses_an_enemy_an_unknown_actor_and_an_already_open_seat()
    {
        var run = BuildRun();
        var s = Backfill.Abandon(BackfillState.Empty, run, Seat);
        Assert.Throws<ArgumentException>(() => Backfill.Abandon(BackfillState.Empty, run, 100));
        Assert.Throws<ArgumentException>(() => Backfill.Abandon(BackfillState.Empty, run, 999));
        Assert.Throws<ArgumentException>(() => Backfill.Abandon(s, run, Seat));
    }

    [Fact]
    public void Open_seats_drop_their_commands_and_other_seats_pass_untouched()
    {
        var run = BuildRun();
        var s = Backfill.Abandon(BackfillState.Empty, run, Seat);
        var commands = new Command[] { new CastCommand(0, Tank, "nuke", 100), new MoveCommand(0, Seat, new Dir2(1000, 0), 0) };

        var kept = Backfill.SeatCommands(s, commands);

        Assert.Equal(new[] { commands[0] }, kept);
        Assert.Same(commands, Backfill.SeatCommands(BackfillState.Empty, commands));
    }

    // f-20d936ba part 1 (BIBLE.md:361 and :501-503, "the next pull start (a teaching pull or a pack engage)"): a layout
    // run with one path pack on the route and its only pull far past it. The tank walks east into the pack's ring; the
    // healer and the seat stand in the hall. The pack's engage is a pack_engage event with no pull_start.
    private const int PackMember = 301;

    private static Run BuildPackRun()
    {
        var nuke = RunTestKit.Ability("nuke", new EffectOp[] { new DamageOp(1, "physical") });
        var layout = new NavLayout(
            new[]
            {
                new NavBox("hall", new Bounds(-5000, 30000, -5000, 5000), true, null),
                new NavBox("corridor", new Bounds(29000, 36000, -1000, 1000), false, null),
                new NavBox("end", new Bounds(35000, 50000, -5000, 5000), true, null),
            },
            new[] { new NavTrigger("p1", new Bounds(37000, 50000, -5000, 5000)) },
            new[] { new Vec3(29500, 0, 0), new Vec3(35500, 0, 0), new Vec3(45000, 0, 0) });
        var pack = new PackGroup("watch", new[] { RunTestKit.Spawn(PackMember, "dummy", 15000, 0, 10) }, new Vec3(15000, 0, 0), 3000);
        var content = RunTestKit.Content(new Dictionary<string, AbilityDef>(StringComparer.Ordinal) { ["nuke"] = nuke }, bounds: layout.Extent) with
        {
            KeyLevel = RunKey,
            Layout = layout,
            Packs = new[] { pack },
        };
        return RunRoot.CreateRun(
            Seed,
            new[]
            {
                RunTestKit.Member(Tank, "warden", ActorRole.Tank, 0, 0, 400, 100),
                RunTestKit.Member(Healer, "chanter", ActorRole.Healer, -2000, 0, 250, 100),
                RunTestKit.Member(Seat, "cinder", ActorRole.Damage, -700, 1000, 250, 100),
            },
            new DungeonDef("backfill-packs", new[] { new PullDef("p1", new[] { RunTestKit.Spawn(101, "dummy", 48000, 0, 10) }) }),
            content);
    }

    private sealed record PackDrive(Run Run, BackfillState Backfill, List<SeatFill> Fills, int? EngageStepTick, int? EngageEventTick, Vec3 SeatAtAbandon, Dictionary<int, Vec3> HealerPosAt, Dictionary<int, Vec3> SeatPosAt);

    /// <summary>Walks the tank east every tick until the pack engages, then on for <paramref name="ticksAfter"/> idle ticks.</summary>
    /// <param name="abandon">Abandons the seat before the step whose pre-step tick satisfies it (relative to the engage
    /// step: null = never; the delegate sees the pre-step tick and whether the engage has already happened).</param>
    /// <param name="inject">When set, replaces the book before each step (sees the pre-step run).</param>
    private static PackDrive DrivePackRun(Func<int, bool, bool> abandon, int ticksAfter = 60, Func<Run, BackfillState, BackfillState>? inject = null)
    {
        var run = BuildPackRun();
        var backfill = BackfillState.Empty;
        var waiting = new List<QueueRequest> { Key10 };
        var fills = new List<SeatFill>();
        var healerPos = new Dictionary<int, Vec3>();
        var seatPos = new Dictionary<int, Vec3>();
        int? engageStep = null;
        int? engageEvent = null;
        var seatAtAbandon = run.State.Actors[Seat].Pos;
        var abandoned = false;
        for (var i = 0; i < 300 + ticksAfter; i++)
        {
            var tick = run.State.Tick;
            if (engageStep is not null && tick >= engageStep + ticksAfter)
            {
                break;
            }
            if (!abandoned && abandon(tick, engageStep is not null))
            {
                backfill = Backfill.Abandon(backfill, run, Seat);
                seatAtAbandon = run.State.Actors[Seat].Pos;
                abandoned = true;
            }
            if (inject is not null)
            {
                backfill = inject(run, backfill);
            }
            var commands = new List<Command>();
            if (engageStep is null)
            {
                commands.Add(new MoveCommand(tick, Tank, new Dir2(1000, 0), 0));
            }
            var step = Backfill.Step(backfill, run, commands, waiting);
            foreach (var index in step.ConsumedQueueIndices.OrderByDescending(x => x))
            {
                waiting.RemoveAt(index);
            }
            fills.AddRange(step.Fills);
            run = step.Step.Run;
            backfill = step.Backfill;
            healerPos[run.State.Tick] = run.State.Actors[Healer].Pos;
            seatPos[run.State.Tick] = run.State.Actors[Seat].Pos;
            if (step.Step.Events.FirstOrDefault(e => e.Type == "pack_engage") is { } engage)
            {
                Assert.DoesNotContain(step.Step.Events, e => e.Type == "pull_start");
                engageStep = run.State.Tick;
                engageEvent = engage.Tick;
            }
        }
        return new PackDrive(run, backfill, fills, engageStep, engageEvent, seatAtAbandon, healerPos, seatPos);
    }

    [Fact(DisplayName = "Backfill (R-BACKFILL-1, BIBLE s10 \"a joiner arrives at the next pull start\"): a seat abandoned before a pack_engage stays empty through the engage")]
    public void A_seat_open_before_a_pack_engage_is_not_refilled_at_the_engage()
    {
        var d = DrivePackRun((tick, engaged) => tick == 5 && !engaged);

        Assert.NotNull(d.EngageStepTick);
        Assert.Equal(0, d.Run.Encounter.PullIndex);
        Assert.Empty(d.Fills);
        Assert.Equal(Seat, Assert.Single(d.Backfill.Open).ActorId);
    }

    [Fact(DisplayName = "Backfill (R-BACKFILL-1): a seat open before a pack_engage is not refilled at the engage and stays where it stood")]
    public void A_seat_open_before_a_pack_engage_stays_idle_where_it_stood()
    {
        // R-BACKFILL-1: the engage is not a pull start, so the seat is not refilled there (it was: the joiner arrived on the engage).
        var d = DrivePackRun((tick, engaged) => tick == 5 && !engaged);

        Assert.NotNull(d.EngageStepTick);
        Assert.Equal(0, d.Run.Encounter.PullIndex);
        Assert.Empty(d.Fills);
        Assert.Equal(Seat, Assert.Single(d.Backfill.Open).ActorId);
        Assert.Equal(d.SeatAtAbandon, d.SeatPosAt[d.EngageStepTick!.Value]);
    }

    [Fact]
    public void A_seat_opened_before_or_after_the_pack_engage_is_not_filled_on_it()
    {
        var probe = DrivePackRun((_, _) => false);
        Assert.NotNull(probe.EngageEventTick);
        var eventTick = probe.EngageEventTick!.Value;
        var preStep = probe.EngageStepTick!.Value - 1;

        // The engage event carries the post-step tick, so a seat Abandon records before a step is always earlier. The
        // book is handed a seat whose abandon tick is the engage event's own tick, open going into the engage step: the
        // tick rule keeps it open through that engage, and with no later start on the route it stays open.
        PackDrive OpenAt(int abandonTick) => DrivePackRun(
            (_, _) => false,
            inject: (run, book) => run.State.Tick == preStep
                ? new BackfillState(new[] { new SeatAbandon(Seat, abandonTick, run.Encounter.PullIndex, run.Encounter.SlackTicks) }, Array.Empty<SeatFill>())
                : book);

        var atEngage = OpenAt(eventTick);
        Assert.Equal(probe.EngageStepTick, atEngage.EngageStepTick);
        Assert.Empty(atEngage.Fills);
        Assert.Equal(Seat, Assert.Single(atEngage.Backfill.Open).ActorId);

        // One tick earlier is before the engage. R-BACKFILL-1: the engage is not a pull start, so that seat stays open too
        // (it was refilled on the engage before the ruling).
        var before = OpenAt(eventTick - 1);
        Assert.Empty(before.Fills);
        Assert.Equal(Seat, Assert.Single(before.Backfill.Open).ActorId);
    }

    [Fact]
    public void No_seat_is_filled_once_the_run_has_ended()
    {
        var run = BuildRun();
        var s = Backfill.Abandon(BackfillState.Empty, run, Seat);
        var step = Backfill.Step(s, run, Array.Empty<Command>(), new[] { Key10 }, new StepRunOpts(AbandonRequested: true));

        Assert.Equal(RunResult.Abandoned, step.Step.Run.RunEnd.Result);
        Assert.Empty(step.Fills);
        Assert.Empty(step.ConsumedQueueIndices);
        Assert.Single(step.Backfill.Open);
    }
}
