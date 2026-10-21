using System;
using Keyrun.Sim.Core;
using Keyrun.Sim.Runs;

namespace Keyrun.Bots.Shared;

/// <summary>
/// The host's queue-pop gate state, as the Queue node reads it (PURPOSE: this artifact exists to make the Queue state
/// honest about what it is waiting on (expected wait from the gate state), per BIBLE.md lines 538-540; status: real).
/// </summary>
/// <param name="Phase">The open or last ready check's phase, or null when no check is open.</param>
/// <param name="SeatsTotal">The seats the check (or the party) covers; 0 when nothing is queued.</param>
/// <param name="HumanReady">True when the human's seat has answered ready.</param>
/// <param name="TicksLeft">Ticks left to answer the open check.</param>
/// <param name="OpenSeats">Party seats waiting for a bot to fill them.</param>
/// <param name="TicksUntilFill">Ticks until the bots fill the open seats.</param>
public sealed record QueueGateState(
    ReadyPhase? Phase,
    int SeatsTotal,
    bool HumanReady,
    int TicksLeft,
    int OpenSeats,
    int TicksUntilFill);

/// <summary>
/// The Queue node's wait readout as a pure function of the gate state (BIBLE s10 "while queued" readout). The node only
/// renders the text this returns; it never computes the wait itself.
/// </summary>
public static class QueueWait
{
    /// <summary>Describes the wait for <paramref name="state"/>.</summary>
    /// <param name="state">The gate state.</param>
    /// <returns>The readout text, or the empty string when there is no wait to show (the node shows its scarcity line).</returns>
    public static string Describe(QueueGateState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        switch (state.Phase)
        {
            case ReadyPhase.Enter:
                return "Run starting.";
            case ReadyPhase.Requeue:
                return "Back in the queue: declined or timed out.";
            case ReadyPhase.Pending:
                var seconds = Seconds(state.TicksLeft);
                return state.HumanReady
                    ? $"Group found. Waiting on the others ({seconds}s)"
                    : $"Group found. Enter: ready   Esc: decline ({seconds}s)";
        }
        if (state.OpenSeats > 0)
        {
            return $"Bots filling {state.OpenSeats} open seat(s) in {Seconds(state.TicksUntilFill)}s";
        }
        return state.SeatsTotal > 0 ? $"Seats filled {state.SeatsTotal} of {state.SeatsTotal}." : string.Empty;
    }

    /// <summary>Whole seconds left for <paramref name="ticks"/>, rounded up (the node never shows 0s while time is left).</summary>
    private static int Seconds(int ticks) => (ticks + Tick.TickHz - 1) / Tick.TickHz;
}
