using System.Linq;
using Keyrun.Sim.Core;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// BIBLE s10 Queue band: the role-scarcity readout while queued and the ready check on pop
/// (game/sim/Keyrun.Sim/Run/QueuePop.cs). Scarcity is read from the waiting roles and names roles only (s0 test 3: no
/// score); the ready check enters on all-ready, requeues the others on a decline, and requeues everyone at the
/// 10-second timeout, with no penalty either way.
/// </summary>
public sealed class QueuePopTests
{
    private static readonly int[] Party = { 1, 2, 3 };

    [Fact(DisplayName = "Queue roles: the missing and the short roles are scarce, shortest first")]
    public void Missing_and_short_roles_are_scarce()
    {
        var read = RoleScarcity.Read(new[] { ActorRole.Damage, ActorRole.Tank, ActorRole.Damage });

        Assert.Equal((1, 0, 2), (read.Tanks, read.Healers, read.Damage));
        Assert.Equal(new[] { ActorRole.Healer, ActorRole.Tank }, read.Scarce);
        Assert.Equal("Short on: healer, tank", read.Readout());
    }

    [Fact(DisplayName = "Queue roles: equal shortfalls list in party order (tank, healer, damage)")]
    public void Ties_list_in_party_order()
    {
        var read = RoleScarcity.Read(new[] { ActorRole.Damage, ActorRole.Damage, ActorRole.Damage });

        Assert.Equal(new[] { ActorRole.Tank, ActorRole.Healer }, read.Scarce);
    }

    [Fact(DisplayName = "Queue roles: every role waiting equally means nothing is scarce")]
    public void Even_queue_has_no_scarce_role()
    {
        var read = RoleScarcity.Read(new[] { ActorRole.Healer, ActorRole.Tank, ActorRole.Damage, ActorRole.Tank, ActorRole.Damage, ActorRole.Healer });

        Assert.Empty(read.Scarce);
        Assert.Equal("Roles even", read.Readout());
    }

    [Fact(DisplayName = "Queue roles: an empty queue says no one else is waiting")]
    public void Empty_queue_reads_no_one_waiting()
    {
        var read = RoleScarcity.Read(Enumerable.Empty<ActorRole>());

        Assert.Empty(read.Scarce);
        Assert.Equal("No one else waiting", read.Readout());
    }

    [Fact(DisplayName = "Queue roles: a role outside the party is ignored")]
    public void None_role_is_ignored()
    {
        var read = RoleScarcity.Read(new[] { ActorRole.None, ActorRole.None });

        Assert.Equal((0, 0, 0), (read.Tanks, read.Healers, read.Damage));
        Assert.Equal("No one else waiting", read.Readout());
    }

    [Fact(DisplayName = "Queue roles: the readout never shows a number (no score, s0 test 3)")]
    public void Readout_has_no_number()
    {
        var read = RoleScarcity.Read(Enumerable.Repeat(ActorRole.Damage, 12).Append(ActorRole.Tank));

        Assert.DoesNotContain(read.Readout(), char.IsDigit);
        Assert.Equal("Short on: healer, tank", read.Readout());
    }

    [Fact(DisplayName = "Ready check: the window is 10 seconds of ticks")]
    public void Window_is_ten_seconds()
    {
        Assert.Equal(10 * Tick.TickHz, ReadyCheck.TimeoutTicks);
        var check = ReadyCheck.Pop(Party, 100);
        Assert.Equal(ReadyCheck.TimeoutTicks, check.RemainingTicks(100));
        Assert.Equal(1, check.RemainingTicks(100 + ReadyCheck.TimeoutTicks - 1));
        Assert.Equal(0, check.RemainingTicks(100 + ReadyCheck.TimeoutTicks + 50));
    }

    [Fact(DisplayName = "Ready check: everyone ready enters the dungeon")]
    public void All_ready_enters()
    {
        var check = ReadyCheck.Pop(Party, 0).Accept(2, 5).Accept(1, 9);
        Assert.Equal(ReadyPhase.Pending, check.Phase);

        check = check.Accept(3, 20);

        Assert.Equal(ReadyPhase.Enter, check.Phase);
        Assert.Empty(check.Requeued);
        Assert.Equal(new[] { 2, 1, 3 }, check.ReadySeats);
    }

    [Fact(DisplayName = "Ready check: a decline requeues the others at once, not the decliner")]
    public void Decline_requeues_the_others()
    {
        var check = ReadyCheck.Pop(Party, 0).Accept(1, 3).Decline(2, 4);

        Assert.Equal(ReadyPhase.Requeue, check.Phase);
        Assert.Equal(new[] { 1, 3 }, check.Requeued);
    }

    [Fact(DisplayName = "Ready check: no full answer by 10 seconds requeues everyone")]
    public void Timeout_requeues_everyone()
    {
        var check = ReadyCheck.Pop(Party, 40).Accept(1, 41).Accept(3, 42);

        Assert.Equal(ReadyPhase.Pending, check.At(40 + ReadyCheck.TimeoutTicks - 1).Phase);
        var timedOut = check.At(40 + ReadyCheck.TimeoutTicks);
        Assert.Equal(ReadyPhase.Requeue, timedOut.Phase);
        Assert.Equal(new[] { 1, 2, 3 }, timedOut.Requeued);
    }

    [Fact(DisplayName = "Ready check: a ready answer after the window resolves as the timeout, not an entry")]
    public void Late_ready_is_a_timeout()
    {
        var check = ReadyCheck.Pop(Party, 0).Accept(1, 1).Accept(2, 2).Accept(3, ReadyCheck.TimeoutTicks);

        Assert.Equal(ReadyPhase.Requeue, check.Phase);
        Assert.Equal(new[] { 1, 2, 3 }, check.Requeued);
    }

    [Fact(DisplayName = "Ready check: answers from outside the group, repeats and answers after it resolves are ignored")]
    public void Stray_answers_are_ignored()
    {
        var pending = ReadyCheck.Pop(Party, 0).Accept(1, 1);
        Assert.Same(pending, pending.Accept(1, 2));
        Assert.Same(pending, pending.Accept(9, 2));
        Assert.Same(pending, pending.Decline(9, 2));

        var entered = pending.Accept(2, 3).Accept(3, 4);
        Assert.Same(entered, entered.Decline(1, 5));
        Assert.Same(entered, entered.At(ReadyCheck.TimeoutTicks + 100));

        var requeued = ReadyCheck.Pop(Party, 0).Decline(3, 1);
        Assert.Same(requeued, requeued.Accept(1, 2));
        Assert.Equal(new[] { 1, 2 }, requeued.Requeued);
    }

    [Fact(DisplayName = "Ready check: a pop collapses duplicate seats and needs at least one")]
    public void Pop_seats_are_distinct_and_non_empty()
    {
        var check = ReadyCheck.Pop(new[] { 1, 2, 2, 3 }, 0);
        Assert.Equal(new[] { 1, 2, 3 }, check.Seats);
        Assert.Equal(ReadyPhase.Pending, check.Phase);
        Assert.Empty(check.ReadySeats);

        Assert.Throws<System.ArgumentException>(() => ReadyCheck.Pop(System.Array.Empty<int>(), 0));
    }
}
