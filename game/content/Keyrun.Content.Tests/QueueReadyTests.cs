using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Keyrun.Content.Tests;

/// <summary>
/// BIBLE s10 Queue band: "while queued, a role-scarcity readout; ready check on pop". The readout and the ready check are
/// pure state (<see cref="RoleReadout"/>, <see cref="ReadyCheck"/>); the Godot Queue node only draws them and forwards
/// the player's accept or decline.
/// </summary>
public sealed class QueueReadyTests
{
    // The shipped three-seat party (HostRunSetup.Roster): the human Warden tank, a Chanter healer bot, a Cinder damage bot.
    private static readonly QueueSeat[] Shipped =
    {
        new(QueueRole.Tank, SeatHolder.Human),
        new(QueueRole.Healer, SeatHolder.Bot),
        new(QueueRole.Damage, SeatHolder.Bot),
    };

    [Fact]
    public void Shipped_party_reads_tank_filled_and_healer_and_damage_scarce()
    {
        var rows = RoleReadout.For(Shipped);

        Assert.Equal(new[] { QueueRole.Tank, QueueRole.Healer, QueueRole.Damage }, rows.Select(r => r.Role).ToArray());
        Assert.False(rows[0].Scarce);
        Assert.True(rows[1].Scarce);
        Assert.True(rows[2].Scarce);
        Assert.Equal(new[] { QueueRole.Healer, QueueRole.Damage }, RoleReadout.Scarce(rows).ToArray());
        Assert.Equal("Party needs: healer, damage", RoleReadout.Needs(rows));
        Assert.Equal(new[] { "Tank: player", "Healer: bot (needed)", "Damage: bot (needed)" }, rows.Select(RoleReadout.Label).ToArray());
    }

    [Fact]
    public void A_party_of_players_needs_nobody()
    {
        var rows = RoleReadout.For(Shipped.Select(s => s with { Fill = SeatHolder.Human }).ToArray());

        Assert.All(rows, r => Assert.False(r.Scarce));
        Assert.Empty(RoleReadout.Scarce(rows));
        Assert.Equal("Party needs: nobody", RoleReadout.Needs(rows));
    }

    [Fact]
    public void An_open_seat_is_scarce_and_reads_open()
    {
        // --solo: the human's seat alone, the other roster roles open.
        var solo = new[] { Shipped[0], Shipped[1] with { Fill = SeatHolder.Open }, Shipped[2] with { Fill = SeatHolder.Open } };
        var rows = RoleReadout.For(solo);

        Assert.Equal(new[] { "Tank: player", "Healer: open (needed)", "Damage: open (needed)" }, rows.Select(RoleReadout.Label).ToArray());
        Assert.Equal(1, rows[1].Open);
        Assert.Equal(0, rows[1].Bots);
    }

    [Fact]
    public void Rows_count_every_seat_of_a_role_and_skip_roles_with_no_seat()
    {
        var seats = new[]
        {
            new QueueSeat(QueueRole.Damage, SeatHolder.Human),
            new QueueSeat(QueueRole.Tank, SeatHolder.Bot),
            new QueueSeat(QueueRole.Damage, SeatHolder.Bot),
        };
        var rows = RoleReadout.For(seats);

        Assert.Equal(new[] { QueueRole.Tank, QueueRole.Damage }, rows.Select(r => r.Role).ToArray());
        var damage = rows[1];
        Assert.Equal((2, 1, 1, 0), (damage.Seats, damage.Humans, damage.Bots, damage.Open));
        Assert.True(damage.Scarce);
        Assert.Equal("Damage: 1 player, 1 bot (needed)", RoleReadout.Label(damage));
    }

    [Fact]
    public void Pop_accepts_for_the_bots_and_waits_on_the_player()
    {
        var check = ReadyCheck.Pop(Shipped);

        Assert.Equal(new[] { ReadyAnswer.Waiting, ReadyAnswer.Accepted, ReadyAnswer.Accepted }, check.Answers.ToArray());
        Assert.Equal(ReadyOutcome.Waiting, check.Outcome);
        Assert.Equal(ReadyCheck.DefaultTimeoutMs, check.TimeoutMs);
        Assert.Equal(new[] { 0 }, check.WaitingSeats.ToArray());
    }

    [Fact]
    public void The_player_accepting_makes_the_party_ready()
    {
        var popped = ReadyCheck.Pop(Shipped);
        var accepted = popped.Accept(0);

        Assert.Equal(ReadyOutcome.AllReady, accepted.Outcome);
        Assert.Equal(ReadyOutcome.Waiting, popped.Outcome);
        Assert.Empty(accepted.WaitingSeats);
    }

    [Fact]
    public void The_player_declining_returns_the_party_to_the_queue_and_later_answers_are_ignored()
    {
        var declined = ReadyCheck.Pop(Shipped).Decline(0);

        Assert.Equal(ReadyOutcome.Declined, declined.Outcome);
        Assert.Same(declined, declined.Accept(0));
        Assert.Equal(ReadyOutcome.Declined, declined.Accept(0).Outcome);
    }

    [Fact]
    public void A_bot_answer_cannot_be_changed()
    {
        var check = ReadyCheck.Pop(Shipped);

        Assert.Same(check, check.Decline(1));
        Assert.Equal(ReadyOutcome.Waiting, check.Decline(1).Outcome);
    }

    [Fact]
    public void An_unanswered_check_expires_at_the_timeout_and_ignores_a_late_accept()
    {
        var check = ReadyCheck.Pop(Shipped, timeoutMs: 10_000);

        var almost = check.Advance(9_999);
        Assert.Equal(ReadyOutcome.Waiting, almost.Outcome);
        Assert.Equal(1, almost.RemainingMs);
        Assert.Equal(1, almost.SecondsLeft);

        var expired = almost.Advance(1);
        Assert.Equal(ReadyOutcome.Expired, expired.Outcome);
        Assert.Equal(0, expired.RemainingMs);
        Assert.Same(expired, expired.Accept(0));
        Assert.Equal(10_000, expired.Advance(5_000).ElapsedMs);
    }

    [Fact]
    public void Seconds_left_round_up()
    {
        var check = ReadyCheck.Pop(Shipped, timeoutMs: 30_000);

        Assert.Equal(30, check.SecondsLeft);
        Assert.Equal(30, check.Advance(1).SecondsLeft);
        Assert.Equal(29, check.Advance(1_000).SecondsLeft);
    }

    [Fact]
    public void An_all_bot_party_is_ready_on_pop()
    {
        var bots = Shipped.Select(s => s with { Fill = SeatHolder.Bot }).ToArray();

        Assert.Equal(ReadyOutcome.AllReady, ReadyCheck.Pop(bots).Outcome);
    }

    [Fact]
    public void Open_seats_take_no_part_in_the_check()
    {
        var solo = new[] { Shipped[0], Shipped[1] with { Fill = SeatHolder.Open }, Shipped[2] with { Fill = SeatHolder.Open } };
        var check = ReadyCheck.Pop(solo);

        Assert.Single(check.Seats);
        Assert.Equal(ReadyOutcome.AllReady, check.Accept(0).Outcome);
    }

    [Fact]
    public void Bad_input_fails_closed()
    {
        Assert.Throws<ArgumentException>(() => ReadyCheck.Pop(Array.Empty<QueueSeat>()));
        Assert.Throws<ArgumentException>(() => ReadyCheck.Pop(new[] { new QueueSeat(QueueRole.Tank, SeatHolder.Open) }));
        Assert.Throws<ArgumentOutOfRangeException>(() => ReadyCheck.Pop(Shipped, timeoutMs: 0));
        var check = ReadyCheck.Pop(Shipped);
        Assert.Throws<ArgumentOutOfRangeException>(() => check.Accept(3));
        Assert.Throws<ArgumentOutOfRangeException>(() => check.Decline(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => check.Advance(-1));
        Assert.Throws<ArgumentNullException>(() => RoleReadout.For(null!));
    }
}
