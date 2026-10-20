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
            // A real death while the seat is open, so the run clock's added time moves between the abandon and the refill.
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
        Dictionary<int, int> PenaltyAt,
        Dictionary<int, Vec3> SeatPosAt,
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
        var penalty = new Dictionary<int, int>();
        var pos = new Dictionary<int, Vec3>();
        int? pullStart3 = null;
        while (run.State.Tick < EndTick)
        {
            var tick = run.State.Tick;
            penalty[tick] = run.Encounter.PenaltyTicks;
            pos[tick] = run.State.Actors[Seat].Pos;
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
        penalty[run.State.Tick] = run.Encounter.PenaltyTicks;
        pos[run.State.Tick] = run.State.Actors[Seat].Pos;
        return new Drive(run, backfill, fills, acted, penalty, pos, pullStart3, waiting);
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

        // The seat's actor stands still while open, and moves on the joiner's first command.
        for (var t = AbandonTick; t <= fillTick; t++)
        {
            Assert.Equal(d.SeatPosAt[AbandonTick], d.SeatPosAt[t]);
        }
        Assert.NotEqual(d.SeatPosAt[fillTick], d.SeatPosAt[fillTick + 1]);
    }

    [Fact]
    public void The_runs_added_time_is_unchanged_by_the_abandon_and_the_refill()
    {
        var d = DriveRun(AbandonTick, new[] { Key10 });
        var control = DriveRun(null, Array.Empty<QueueRequest>());
        var fill = Assert.Single(d.Fills);

        // Every tick's added time equals the no-abandon control run's, through the abandon, the open seat and the refill.
        Assert.Equal(control.PenaltyAt.Keys.OrderBy(k => k), d.PenaltyAt.Keys.OrderBy(k => k));
        foreach (var (tick, penalty) in control.PenaltyAt)
        {
            Assert.Equal(penalty, d.PenaltyAt[tick]);
        }
        Assert.Equal(control.Run.Encounter.PenaltyTicks, d.Run.Encounter.PenaltyTicks);

        // STATE.md D46: the healer's death (tick 80, after the abandon at tick 60) adds its cost to the run clock, so the
        // seat opens with no added time and the joiner arrives with the run's current added time, never reset.
        Assert.False(d.Run.State.Actors[Healer].Alive);
        Assert.Equal(0, d.PenaltyAt[AbandonTick]);
        Assert.True(d.Run.Encounter.PenaltyTicks > 0);
        Assert.Equal(Timer.DeathCostTicks(d.Run.Encounter.Rules), d.PenaltyAt[fill.FillTick]);
        Assert.Equal(d.PenaltyAt[fill.FillTick], fill.PenaltyTicks);
        Assert.Equal(d.Run.Encounter.PenaltyTicks, fill.PenaltyTicks);
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
            Projection.Line(a.Run.State, a.Run.Encounter.PullIndex, a.Run.Encounter.PenaltyTicks),
            Projection.Line(b.Run.State, b.Run.Encounter.PullIndex, b.Run.Encounter.PenaltyTicks));
    }

    [Fact]
    public void Abandon_records_the_seat_without_touching_the_run()
    {
        var run = BuildRun();
        var before = Projection.Line(run.State, run.Encounter.PullIndex, run.Encounter.PenaltyTicks);
        var s = Backfill.Abandon(BackfillState.Empty, run, Seat);

        var open = Assert.Single(s.Open);
        Assert.Equal(new SeatAbandon(Seat, 0, 0, run.Encounter.PenaltyTicks), open);
        Assert.Equal(before, Projection.Line(run.State, run.Encounter.PullIndex, run.Encounter.PenaltyTicks));
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
