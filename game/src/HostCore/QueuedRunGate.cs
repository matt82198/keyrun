using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Keyrun.Sim.Runs;

namespace Keyrun.Host;

/// <summary>
/// BIBLE s10 "ready check on pop" in the host (finding f-e24b4fa4, RULING R-229): the screen's Queue press
/// (<c>Screen.Queued</c>) pops a pure <see cref="ReadyCheck"/> (Keyrun.Sim QueuePop: the 10-second window, decline and
/// timeout requeue with no penalty) for the matched seats instead of starting the run. The bot seats answer ready at
/// the pop (bots are never the reason a pop fails, BIBLE s0 test 3); the human answers Enter (ready) or Esc (decline)
/// through the Queue node. Every seat ready raises <see cref="Entered"/> with the queued request, which SimDriver starts
/// exactly as a queued run always started (<c>StartQueuedRun</c>); a decline or the timeout raises
/// <see cref="Requeued"/>, on which SimDriver drops the request from the Queue node and reopens the between-runs screen.
/// Godot-free (it holds no clock: the caller passes ticks), so Keyrun.Bots.Tests drives it as the host does.
/// Proof lines keep the Queue node's format: <c>QUEUE pop seats=S tick=T window=W</c>,
/// <c>QUEUE ready seat=N answer=ready|decline tick=T</c> and <c>QUEUE ready end phase=enter|requeue requeued=S</c>.
/// </summary>
public sealed class QueuedRunGate
{
    private readonly Action<string>? _proof;

    /// <summary>Builds the gate.</summary>
    /// <param name="proof">Where proof lines go (the Queue node's autorun proof), or null.</param>
    public QueuedRunGate(Action<string>? proof = null) => _proof = proof;

    /// <summary>Raised once when every seat answered ready: the host starts the queued run.</summary>
    public event Action<QueueRequest>? Entered;

    /// <summary>Raised once when a seat declined or the window closed: the host drops the request and reopens the
    /// screen. The check's <see cref="ReadyCheck.Requeued"/> names the seats returned to the queue.</summary>
    public event Action<QueueRequest, ReadyCheck>? Requeued;

    /// <summary>The request the open check is for, or null when none is open.</summary>
    public QueueRequest? Request { get; private set; }

    /// <summary>The open or last ready check, or null before the first pop.</summary>
    public ReadyCheck? Check { get; private set; }

    /// <summary>The human's seat in the open or last check.</summary>
    public int HumanSeat { get; private set; }

    /// <summary>True while a check waits on answers (the ready dialog is up; Enter belongs to it).</summary>
    public bool Checking => Check is { Phase: ReadyPhase.Pending } && Request is not null;

    /// <summary>
    /// Which launches answer the human's seat ready at the pop, so a scripted launch reaches its run on the same frame
    /// and tick it always did: <c>--autorun</c>, <c>--press-enter N</c> and a <c>--replay-input</c> of a log recorded
    /// before the ready check (no recorded ready answers). The interactive game, a replay whose ready answers are
    /// recorded keys, and <c>--ready-hold F</c> (the scripted answer waits F frames, so the dialog can be seen) do not.
    /// </summary>
    /// <param name="autorun"><c>--autorun</c>.</param>
    /// <param name="pressEnter"><c>--press-enter N</c> (0 = off).</param>
    /// <param name="replaying"><c>--replay-input</c> is playing a log.</param>
    /// <param name="replayHasReadyKeys">The replayed log recorded the human's ready answers as keys.</param>
    /// <param name="readyHoldFrames"><c>--ready-hold F</c> (0 = off).</param>
    /// <returns>True when the host answers for the human at the pop.</returns>
    public static bool HumanReadyAtPop(bool autorun, int pressEnter, bool replaying, bool replayHasReadyKeys, int readyHoldFrames)
    {
        if (readyHoldFrames > 0)
        {
            return false;
        }
        if (replaying)
        {
            return !replayHasReadyKeys;
        }
        return autorun || pressEnter > 0;
    }

    /// <summary>
    /// The screen queued <paramref name="request"/>: pops a ready check for the human seat plus the bot seats at
    /// <paramref name="tick"/>. The bots answer ready at once; with <paramref name="humanReadyAtPop"/> so does the human
    /// (a scripted launch), and the run is entered before this returns. A check still open is replaced (its request is
    /// dropped without an event).
    /// </summary>
    /// <param name="request">The queued request.</param>
    /// <param name="humanSeat">The human's seat (actor id).</param>
    /// <param name="botSeats">The bot seats (actor ids), possibly none (<c>--solo</c>).</param>
    /// <param name="tick">The pop tick (the Queue node's clock).</param>
    /// <param name="humanReadyAtPop">Answer ready for the human at the pop (<see cref="HumanReadyAtPop"/>).</param>
    /// <returns>The check after the pop's answers.</returns>
    public ReadyCheck Queued(QueueRequest request, int humanSeat, IReadOnlyList<int> botSeats, int tick, bool humanReadyAtPop)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(botSeats);
        var check = ReadyCheck.Pop(new[] { humanSeat }.Concat(botSeats).ToArray(), tick);
        Request = request;
        HumanSeat = humanSeat;
        Check = check;
        Proof(FormattableString.Invariant(
            $"QUEUE pop seats={string.Join(',', check.Seats)} tick={tick} window={ReadyCheck.TimeoutTicks}"));
        foreach (var seat in check.Seats.Where(s => s != humanSeat))
        {
            Set(Check.Accept(seat, tick));
        }
        if (humanReadyAtPop)
        {
            Answer(humanSeat, ready: true, tick);
        }
        return Check;
    }

    /// <summary>One seat answers the open check. Ignored (null) when no check is open.</summary>
    /// <param name="seat">The seat.</param>
    /// <param name="ready">True for ready, false for decline.</param>
    /// <param name="tick">The answer tick.</param>
    /// <returns>The check after the answer, or null when none was open.</returns>
    public ReadyCheck? Answer(int seat, bool ready, int tick)
    {
        if (!Checking)
        {
            return null;
        }
        Proof(FormattableString.Invariant($"QUEUE ready seat={seat} answer={(ready ? "ready" : "decline")} tick={tick}"));
        return Set(ready ? Check!.Accept(seat, tick) : Check!.Decline(seat, tick));
    }

    /// <summary>Advances the open check to <paramref name="tick"/> (the window closing requeues everyone).</summary>
    /// <param name="tick">The tick.</param>
    public void Advance(int tick)
    {
        if (Checking)
        {
            Set(Check!.At(tick));
        }
    }

    /// <summary>Ticks left to answer at <paramref name="tick"/>, or 0 when no check is open.</summary>
    /// <param name="tick">The tick.</param>
    /// <returns>The ticks left.</returns>
    public int RemainingTicks(int tick) => Checking ? Check!.RemainingTicks(tick) : 0;

    private ReadyCheck Set(ReadyCheck next)
    {
        Check = next;
        if (next.Phase == ReadyPhase.Pending || Request is not { } request)
        {
            return next;
        }
        Request = null;
        Proof(string.Create(CultureInfo.InvariantCulture,
            $"QUEUE ready end phase={next.Phase.ToString().ToLowerInvariant()} requeued={string.Join(',', next.Requeued)}"));
        if (next.Phase == ReadyPhase.Enter)
        {
            Entered?.Invoke(request);
        }
        else
        {
            Requeued?.Invoke(request, next);
        }
        return next;
    }

    private void Proof(string line) => _proof?.Invoke(line);
}
