using System;
using System.Collections.Generic;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Runs;

/// <summary>
/// BIBLE s10 Queue band: "while queued, a role-scarcity readout". Which of the party's roles the waiting queue is short
/// on, read from the roles of the waiting requests. It is information only: the queue still matches on key level only
/// (BIBLE s3 soft roles), so a scarce role never blocks anyone. It names roles, never a count, score or rating
/// (BIBLE s0 test 3, "no shareable score to judge by").
/// </summary>
/// <param name="Tanks">Waiting tanks.</param>
/// <param name="Healers">Waiting healers.</param>
/// <param name="Damage">Waiting damage.</param>
/// <param name="Scarce">The short roles, shortest first, ties in party order (tank, healer, damage).</param>
public sealed record RoleScarcity(int Tanks, int Healers, int Damage, IReadOnlyList<ActorRole> Scarce)
{
    /// <summary>The party's roles, in party order (HostRunSetup's three seats: warden tank, chanter healer, cinder damage).</summary>
    public static IReadOnlyList<ActorRole> PartyRoles { get; } = new[] { ActorRole.Tank, ActorRole.Healer, ActorRole.Damage };

    /// <summary>
    /// Reads the queue: a party role is scarce when fewer of it wait than of the most-waiting party role (it is the
    /// role a party forming from this queue runs out of first). Roles outside the party (<see cref="ActorRole.None"/>)
    /// are ignored. An empty queue, or one with every party role waiting equally, has no scarce role.
    /// </summary>
    /// <param name="queued">The waiting requests' roles, in any order.</param>
    /// <returns>The readout state.</returns>
    public static RoleScarcity Read(IEnumerable<ActorRole> queued)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// The one line the Queue band shows: "No one else waiting", "Roles even", or "Short on: healer, tank". Role names
    /// only, never a number.
    /// </summary>
    /// <returns>The line.</returns>
    public string Readout()
    {
        throw new NotImplementedException();
    }
}

/// <summary>Where a <see cref="ReadyCheck"/> stands.</summary>
public enum ReadyPhase
{
    /// <summary>Waiting on answers, inside the timeout.</summary>
    Pending,

    /// <summary>Every seat answered ready: the group enters the dungeon.</summary>
    Enter,

    /// <summary>A seat declined or the check timed out: <see cref="ReadyCheck.Requeued"/> go back to the queue.</summary>
    Requeue,
}

/// <summary>
/// BIBLE s10 "ready check on pop": when the queue pops a group, each seat answers ready or decline. Every seat ready
/// enters the dungeon. A decline returns the other seats to the queue at once (the decliner simply leaves the queue);
/// no answer from everyone within <see cref="TimeoutTicks"/> (10 seconds) returns everyone to the queue. Nobody is
/// penalised for declining or timing out: the check produces no lock and no mark (research/08:127-128). Pure and
/// tick-based (no clock): the caller passes the tick of each answer.
/// </summary>
/// <param name="Seats">The popped seats, in pop order, distinct.</param>
/// <param name="StartTick">The pop tick.</param>
/// <param name="ReadySeats">Seats that answered ready, in answer order.</param>
/// <param name="Phase">Where the check stands.</param>
/// <param name="Requeued">Seats returned to the queue (in <see cref="Seats"/> order) once <see cref="Phase"/> is Requeue.</param>
public sealed record ReadyCheck(
    IReadOnlyList<int> Seats,
    int StartTick,
    IReadOnlyList<int> ReadySeats,
    ReadyPhase Phase,
    IReadOnlyList<int> Requeued)
{
    /// <summary>The answer window: 10 seconds of ticks (BIBLE s10).</summary>
    public const int TimeoutTicks = 10 * Tick.TickHz;

    /// <summary>Opens a check for the popped <paramref name="seats"/> at <paramref name="tick"/>.</summary>
    /// <param name="seats">The popped seats (at least one; duplicates collapse).</param>
    /// <param name="tick">The pop tick.</param>
    /// <returns>A pending check.</returns>
    public static ReadyCheck Pop(IReadOnlyList<int> seats, int tick)
    {
        throw new NotImplementedException();
    }

    /// <summary>The check as it stands at <paramref name="tick"/>: a pending check past its window is Requeue (everyone).</summary>
    /// <param name="tick">The tick.</param>
    /// <returns>The check, this instance when nothing changes.</returns>
    public ReadyCheck At(int tick)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// <paramref name="seat"/> answers ready at <paramref name="tick"/>. The last seat ready enters. Ignored (this
    /// instance) for a seat not in the check, a repeat answer, or a check already resolved (a late answer after the
    /// window resolves as the timeout).
    /// </summary>
    /// <param name="seat">The seat.</param>
    /// <param name="tick">The answer tick.</param>
    /// <returns>The check after the answer.</returns>
    public ReadyCheck Accept(int seat, int tick)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// <paramref name="seat"/> declines at <paramref name="tick"/>: every other seat is requeued at once, with no penalty
    /// for anyone. Ignored as <see cref="Accept"/> is.
    /// </summary>
    /// <param name="seat">The seat.</param>
    /// <param name="tick">The answer tick.</param>
    /// <returns>The check after the answer.</returns>
    public ReadyCheck Decline(int seat, int tick)
    {
        throw new NotImplementedException();
    }

    /// <summary>Ticks left to answer at <paramref name="tick"/> (0 once the window has closed).</summary>
    /// <param name="tick">The tick.</param>
    /// <returns>The ticks left.</returns>
    public int RemainingTicks(int tick)
    {
        throw new NotImplementedException();
    }
}
