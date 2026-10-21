using System.Collections.Generic;
using System.Linq;
using Keyrun.Host;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// Finding f-e24b4fa4 (RULING R-229), BIBLE s10 "ready check on pop": the screen's Queue press pops a ready check and the
/// queued run starts only once every seat has answered ready. Every case drives the REAL host gate
/// (game/src/Host/QueuedRunGate.cs, linked into this project) exactly as SimDriver's <c>Screen.Queued</c> handler does
/// (<see cref="QueuedRunGate.Queued"/> with the human seat and the bot seats), and observes what the host acts on: the
/// <see cref="QueuedRunGate.Entered"/> event SimDriver starts the run from (StartQueuedRun) and the
/// <see cref="QueuedRunGate.Requeued"/> event SimDriver drops the request and reopens the screen from.
/// The window is main's QueuePop window (<see cref="ReadyCheck.TimeoutTicks"/>, 10 s); nothing here restates it.
/// </summary>
public sealed class QueuedRunGateTests
{
    private const int Human = 1;
    private static readonly int[] Bots = { 2, 3 };
    private static readonly QueueRequest Request = new("hollow-vault", 3, "echo");

    private sealed class Host
    {
        public Host()
        {
            Gate = new QueuedRunGate(Lines.Add);
            Gate.Entered += Started.Add;
            Gate.Requeued += (request, check) => Reopened.Add((request, check));
        }

        public QueuedRunGate Gate { get; }

        public List<QueueRequest> Started { get; } = new();

        public List<(QueueRequest Request, ReadyCheck Check)> Reopened { get; } = new();

        public List<string> Lines { get; } = new();
    }

    [Fact]
    public void A_queue_press_starts_no_run_until_the_human_answers_ready()
    {
        var host = new Host();

        host.Gate.Queued(Request, Human, Bots, tick: 100, humanReadyAtPop: false);

        Assert.Empty(host.Started);
        Assert.True(host.Gate.Checking);
        Assert.Same(Request, host.Gate.Request);
        // The bots answered at the pop: only the human is outstanding (bots are never why a pop fails, BIBLE s0 test 3).
        Assert.Equal(new[] { 2, 3 }, host.Gate.Check!.ReadySeats);

        host.Gate.Advance(100 + ReadyCheck.TimeoutTicks - 1);
        Assert.Empty(host.Started);

        host.Gate.Answer(Human, ready: true, 100 + ReadyCheck.TimeoutTicks - 1);

        Assert.Equal(new[] { Request }, host.Started);
        Assert.Empty(host.Reopened);
        Assert.False(host.Gate.Checking);
        Assert.Null(host.Gate.Request);
    }

    [Fact]
    public void The_run_starts_exactly_once_when_every_seat_is_ready()
    {
        var host = new Host();
        host.Gate.Queued(Request, Human, Bots, tick: 0, humanReadyAtPop: false);

        host.Gate.Answer(Human, ready: true, 5);
        host.Gate.Answer(Human, ready: true, 6);
        host.Gate.Advance(ReadyCheck.TimeoutTicks + 50);

        Assert.Single(host.Started);
        Assert.Empty(host.Reopened);
        Assert.Equal(ReadyPhase.Enter, host.Gate.Check!.Phase);
    }

    [Fact]
    public void A_decline_reopens_the_screen_and_starts_no_run()
    {
        var host = new Host();
        host.Gate.Queued(Request, Human, Bots, tick: 0, humanReadyAtPop: false);

        host.Gate.Answer(Human, ready: false, 4);
        host.Gate.Answer(Human, ready: true, 5);

        Assert.Empty(host.Started);
        var (request, check) = Assert.Single(host.Reopened);
        Assert.Same(Request, request);
        Assert.Equal(ReadyPhase.Requeue, check.Phase);
        // The decliner leaves; the others go back to the queue (QueuePop's Requeued list, no penalty).
        Assert.Equal(new[] { 2, 3 }, check.Requeued);
        Assert.False(host.Gate.Checking);
        Assert.Null(host.Gate.Request);
    }

    [Fact]
    public void No_answer_in_the_window_reopens_the_screen_and_starts_no_run()
    {
        var host = new Host();
        host.Gate.Queued(Request, Human, Bots, tick: 20, humanReadyAtPop: false);

        host.Gate.Advance(20 + ReadyCheck.TimeoutTicks);
        host.Gate.Answer(Human, ready: true, 20 + ReadyCheck.TimeoutTicks + 1);

        Assert.Empty(host.Started);
        var (_, check) = Assert.Single(host.Reopened);
        Assert.Equal(new[] { 1, 2, 3 }, check.Requeued);
    }

    [Fact]
    public void A_scripted_pop_answers_ready_for_the_human_and_starts_the_run_at_once()
    {
        var host = new Host();

        host.Gate.Queued(Request, Human, Bots, tick: 7, humanReadyAtPop: true);

        Assert.Equal(new[] { Request }, host.Started);
        Assert.False(host.Gate.Checking);
        Assert.Contains("QUEUE ready seat=1 answer=ready tick=7", host.Lines);
        Assert.Contains("QUEUE ready end phase=enter requeued=", host.Lines);
    }

    [Fact]
    public void A_solo_pop_waits_on_the_human_alone()
    {
        var host = new Host();

        host.Gate.Queued(Request, Human, System.Array.Empty<int>(), tick: 0, humanReadyAtPop: false);

        Assert.Empty(host.Started);
        Assert.Equal(new[] { Human }, host.Gate.Check!.Seats);
        host.Gate.Answer(Human, ready: true, 1);
        Assert.Single(host.Started);
    }

    [Fact]
    public void The_pop_prints_its_seats_and_the_window()
    {
        var host = new Host();

        host.Gate.Queued(Request, Human, Bots, tick: 3, humanReadyAtPop: false);

        Assert.Equal(
            $"QUEUE pop seats=1,2,3 tick=3 window={ReadyCheck.TimeoutTicks}",
            host.Lines.First(l => l.StartsWith("QUEUE pop ", System.StringComparison.Ordinal)));
        Assert.Equal(1, host.Gate.HumanSeat);
    }

    [Theory]
    [InlineData(true, 0, false, false, 0, true)]   // --autorun: the scripted Warden answers at the pop
    [InlineData(false, 3, false, false, 0, true)]  // --press-enter N: the scripted player answers at the pop
    [InlineData(false, 0, true, false, 0, true)]   // --replay-input of a log recorded before the ready check
    [InlineData(false, 0, true, true, 0, false)]   // --replay-input of a log whose ready answers are recorded keys
    [InlineData(false, 0, false, false, 0, false)] // the interactive game: the human answers
    [InlineData(true, 0, false, false, 30, false)] // --ready-hold F: the scripted answer waits F frames
    public void Which_launches_answer_ready_for_the_human_at_the_pop(
        bool autorun, int pressEnter, bool replaying, bool replayHasReadyKeys, int readyHold, bool expected)
    {
        Assert.Equal(expected, QueuedRunGate.HumanReadyAtPop(autorun, pressEnter, replaying, replayHasReadyKeys, readyHold));
    }
}
