using System;
using System.Collections.Generic;
using System.Linq;

namespace Keyrun.Content;

/// <summary>A party role as the queue reads it (BIBLE s3 soft roles: the roster's tank, healer and damage seats).</summary>
public enum QueueRole
{
    /// <summary>The anchor seat (Warden).</summary>
    Tank,

    /// <summary>The support seat (Chanter).</summary>
    Healer,

    /// <summary>The damage seat (Cinder).</summary>
    Damage,
}

/// <summary>Who holds a party seat.</summary>
public enum SeatHolder
{
    /// <summary>A player.</summary>
    Human,

    /// <summary>A bot covering the seat.</summary>
    Bot,

    /// <summary>Nobody (a <c>--solo</c> party, or a seat left mid-run).</summary>
    Open,
}

/// <summary>One party seat: its role and who holds it.</summary>
/// <param name="Role">The seat's role.</param>
/// <param name="Fill">Who holds it.</param>
public sealed record QueueSeat(QueueRole Role, SeatHolder Fill);

/// <summary>One role's line of the readout.</summary>
/// <param name="Role">The role.</param>
/// <param name="Seats">Party seats of this role.</param>
/// <param name="Humans">Of those, held by players.</param>
/// <param name="Bots">Of those, covered by bots.</param>
/// <param name="Open">Of those, empty.</param>
public sealed record RoleStatus(QueueRole Role, int Seats, int Humans, int Bots, int Open)
{
    /// <summary>
    /// True when a seat of this role has no player in it (a bot covers it or it is empty): the role the queue is short
    /// of, FFXIV Adventurer-in-Need style (research/08 "party needs: 1 tank role").
    /// </summary>
    public bool Scarce => Humans < Seats;
}

/// <summary>
/// BIBLE s10 Queue band "while queued, a role-scarcity readout": which of the party's roles players fill and which the
/// queue still needs. Pure; the Godot Queue node draws the lines.
/// </summary>
public static class RoleReadout
{
    /// <summary>One line per role that has at least one seat, in <see cref="QueueRole"/> order.</summary>
    /// <param name="seats">The party's seats.</param>
    /// <returns>The lines.</returns>
    public static IReadOnlyList<RoleStatus> For(IReadOnlyList<QueueSeat> seats)
    {
        ArgumentNullException.ThrowIfNull(seats);
        var rows = new List<RoleStatus>();
        foreach (var role in Enum.GetValues<QueueRole>())
        {
            var mine = seats.Where(s => s.Role == role).ToList();
            if (mine.Count == 0)
            {
                continue;
            }
            rows.Add(new RoleStatus(role, mine.Count, Count(mine, SeatHolder.Human), Count(mine, SeatHolder.Bot), Count(mine, SeatHolder.Open)));
        }
        return rows;
    }

    /// <summary>The scarce roles, in readout order.</summary>
    /// <param name="rows">The readout.</param>
    /// <returns>The roles no player fills.</returns>
    public static IReadOnlyList<QueueRole> Scarce(IReadOnlyList<RoleStatus> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        return rows.Where(r => r.Scarce).Select(r => r.Role).ToList();
    }

    /// <summary>The summary line, e.g. "Party needs: healer, damage" or "Party needs: nobody".</summary>
    /// <param name="rows">The readout.</param>
    /// <returns>The line.</returns>
    public static string Needs(IReadOnlyList<RoleStatus> rows)
    {
        var scarce = Scarce(rows);
        return "Party needs: " + (scarce.Count == 0 ? "nobody" : string.Join(", ", scarce.Select(Name)));
    }

    /// <summary>One role's line, e.g. "Tank: player" or "Healer: bot (needed)".</summary>
    /// <param name="row">The role's status.</param>
    /// <returns>The line.</returns>
    public static string Label(RoleStatus row)
    {
        ArgumentNullException.ThrowIfNull(row);
        var parts = new List<string>();
        AddPart(parts, row.Humans, "player", row.Seats);
        AddPart(parts, row.Bots, "bot", row.Seats);
        AddPart(parts, row.Open, "open", row.Seats);
        var role = Name(row.Role);
        return char.ToUpperInvariant(role[0]) + role[1..] + ": " + string.Join(", ", parts) + (row.Scarce ? " (needed)" : string.Empty);
    }

    /// <summary>The role's lower-case name ("tank", "healer", "damage").</summary>
    /// <param name="role">The role.</param>
    /// <returns>The name.</returns>
    public static string Name(QueueRole role) => role switch
    {
        QueueRole.Tank => "tank",
        QueueRole.Healer => "healer",
        QueueRole.Damage => "damage",
        _ => throw new ArgumentOutOfRangeException(nameof(role), role, "unknown role"),
    };

    private static int Count(IEnumerable<QueueSeat> seats, SeatHolder fill) => seats.Count(s => s.Fill == fill);

    // A one-seat role reads "player"; a role with several seats counts them ("1 player, 1 bot").
    private static void AddPart(List<string> parts, int n, string word, int seats)
    {
        if (n > 0)
        {
            parts.Add(seats == 1 ? word : FormattableString.Invariant($"{n} {word}"));
        }
    }
}

/// <summary>One seat's answer to a ready check.</summary>
public enum ReadyAnswer
{
    /// <summary>Not answered yet.</summary>
    Waiting,

    /// <summary>Ready.</summary>
    Accepted,

    /// <summary>Not ready.</summary>
    Declined,
}

/// <summary>Where a ready check stands.</summary>
public enum ReadyOutcome
{
    /// <summary>Some seat has not answered and time remains.</summary>
    Waiting,

    /// <summary>Every seat accepted: the run starts.</summary>
    AllReady,

    /// <summary>A seat declined: back to the queue, no penalty (research/08).</summary>
    Declined,

    /// <summary>Time ran out before every seat accepted: back to the queue, no penalty.</summary>
    Expired,
}

/// <summary>
/// BIBLE s10 Queue band "ready check on pop" (research/08: "accept within N seconds or return to queue, no penalty for
/// a POC"). Immutable: every answer or clock step returns the next check. Bots accept on pop; players must accept
/// before <see cref="TimeoutMs"/>. Open seats take no part. Any decline, or the timeout, ends the check without a run.
/// </summary>
public sealed record ReadyCheck
{
    /// <summary>The default answer window, ms.</summary>
    public const int DefaultTimeoutMs = 30_000;

    private ReadyCheck(IReadOnlyList<QueueSeat> seats, IReadOnlyList<ReadyAnswer> answers, int elapsedMs, int timeoutMs)
    {
        Seats = seats;
        Answers = answers;
        ElapsedMs = elapsedMs;
        TimeoutMs = timeoutMs;
    }

    /// <summary>The seats in the check (the party's held seats, in party order).</summary>
    public IReadOnlyList<QueueSeat> Seats { get; }

    /// <summary>Each seat's answer, index-aligned with <see cref="Seats"/>.</summary>
    public IReadOnlyList<ReadyAnswer> Answers { get; }

    /// <summary>Milliseconds since the pop, capped at <see cref="TimeoutMs"/>.</summary>
    public int ElapsedMs { get; }

    /// <summary>The answer window, ms.</summary>
    public int TimeoutMs { get; }

    /// <summary>Milliseconds left to answer.</summary>
    public int RemainingMs => TimeoutMs - ElapsedMs;

    /// <summary>Whole seconds left, rounded up (the countdown the dialog shows).</summary>
    public int SecondsLeft => (RemainingMs + 999) / 1000;

    /// <summary>The seats still to answer, by index into <see cref="Seats"/>.</summary>
    public IReadOnlyList<int> WaitingSeats =>
        Enumerable.Range(0, Answers.Count).Where(i => Answers[i] == ReadyAnswer.Waiting).ToList();

    /// <summary>Where the check stands: a decline beats everything, then all-ready, then the clock.</summary>
    public ReadyOutcome Outcome =>
        Answers.Contains(ReadyAnswer.Declined) ? ReadyOutcome.Declined
        : Answers.All(a => a == ReadyAnswer.Accepted) ? ReadyOutcome.AllReady
        : ElapsedMs >= TimeoutMs ? ReadyOutcome.Expired
        : ReadyOutcome.Waiting;

    /// <summary>The check the queue opens when it pops: bots accept at once, players wait.</summary>
    /// <param name="party">The party's seats; open seats are dropped.</param>
    /// <param name="timeoutMs">The answer window, ms (at least 1).</param>
    /// <returns>The check.</returns>
    /// <exception cref="ArgumentException">No held seat to ask.</exception>
    public static ReadyCheck Pop(IReadOnlyList<QueueSeat> party, int timeoutMs = DefaultTimeoutMs)
    {
        ArgumentNullException.ThrowIfNull(party);
        if (timeoutMs < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(timeoutMs), timeoutMs, "the ready check needs an answer window");
        }
        var seats = party.Where(s => s.Fill != SeatHolder.Open).ToList();
        if (seats.Count == 0)
        {
            throw new ArgumentException("a ready check needs at least one held seat", nameof(party));
        }
        var answers = seats.Select(s => s.Fill == SeatHolder.Bot ? ReadyAnswer.Accepted : ReadyAnswer.Waiting).ToList();
        return new ReadyCheck(seats, answers, 0, timeoutMs);
    }

    /// <summary>Seat <paramref name="seat"/> accepts. Ignored once the check is over or the seat has answered.</summary>
    /// <param name="seat">Index into <see cref="Seats"/>.</param>
    /// <returns>The next check (this one when ignored).</returns>
    public ReadyCheck Accept(int seat) => Answer(seat, ReadyAnswer.Accepted);

    /// <summary>Seat <paramref name="seat"/> declines. Ignored once the check is over or the seat has answered.</summary>
    /// <param name="seat">Index into <see cref="Seats"/>.</param>
    /// <returns>The next check (this one when ignored).</returns>
    public ReadyCheck Decline(int seat) => Answer(seat, ReadyAnswer.Declined);

    /// <summary>The clock moves <paramref name="ms"/> forward (capped at the timeout).</summary>
    /// <param name="ms">Milliseconds, at least 0.</param>
    /// <returns>The next check.</returns>
    public ReadyCheck Advance(int ms)
    {
        if (ms < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ms), ms, "time does not run backwards");
        }
        var elapsed = (int)Math.Min((long)ElapsedMs + ms, TimeoutMs);
        return elapsed == ElapsedMs ? this : new ReadyCheck(Seats, Answers, elapsed, TimeoutMs);
    }

    private ReadyCheck Answer(int seat, ReadyAnswer answer)
    {
        if (seat < 0 || seat >= Seats.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(seat), seat, "no such seat in the ready check");
        }
        if (Outcome != ReadyOutcome.Waiting || Answers[seat] != ReadyAnswer.Waiting)
        {
            return this;
        }
        var answers = Answers.ToArray();
        answers[seat] = answer;
        return new ReadyCheck(Seats, answers, ElapsedMs, TimeoutMs);
    }
}
