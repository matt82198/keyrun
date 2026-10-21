using Keyrun.Bots.Shared;
using Keyrun.Sim.Core;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// B57 / B58 (U3 #495 review): the Queue readout's open-seat and seats-filled branches must be reachable from the Queue
/// node's state. Each case derives the gate state the way the node does (<see cref="QueueWait.StateFor"/>, fed the live
/// run's party size and open seats) and reads the text <see cref="QueueWait.Describe"/> returns.
/// </summary>
public sealed class QueueReadoutBranchesTests
{
    private const int Hz = Tick.TickHz;

    [Fact(DisplayName = "B57: a live run with one open seat after a leaver reads the open-seat form (backfill path)")]
    public void OpenSeatAfterLeaverReadsOpenSeatForm()
    {
        var state = QueueWait.StateFor(
            checkPhase: null,
            checking: false,
            checkSeats: 0,
            humanReady: false,
            ticksLeft: 0,
            outcomeShowing: false,
            partySeats: 3,
            openSeats: 1,
            ticksUntilFill: 0);

        Assert.Equal("Bots filling 1 open seat(s) at the next pull", QueueWait.Describe(state));
    }

    [Fact(DisplayName = "B57: a known fill ETA keeps the timed open-seat form")]
    public void OpenSeatWithKnownEtaReadsTimedForm()
    {
        var state = QueueWait.StateFor(null, false, 0, false, 0, false, 3, 2, 5 * Hz);

        Assert.Equal("Bots filling 2 open seat(s) in 5s", QueueWait.Describe(state));
    }

    [Fact(DisplayName = "B58: a live run with every seat filled reads the seats-filled form")]
    public void AllSeatsFilledReadsSeatsFilledForm()
    {
        var state = QueueWait.StateFor(null, false, 0, false, 0, false, 3, 0, 0);

        Assert.Equal("Seats filled 3 of 3.", QueueWait.Describe(state));
    }

    [Fact(DisplayName = "B58: no live run and nothing queued reads the empty readout")]
    public void NoRunReadsEmpty()
    {
        var state = QueueWait.StateFor(null, false, 0, false, 0, false, 0, 0, 0);

        Assert.Equal(string.Empty, QueueWait.Describe(state));
    }

    [Fact(DisplayName = "B57: an open ready check still reads the wait, never the seat forms")]
    public void PendingCheckOutranksSeatForms()
    {
        var state = QueueWait.StateFor(ReadyPhase.Pending, true, 3, false, 3 * Hz + 1, false, 3, 1, 0);

        Assert.Equal("Group found. Enter: ready   Esc: decline (4s)", QueueWait.Describe(state));
    }
}
