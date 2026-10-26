using Keyrun.Bots.Shared;
using Keyrun.Sim.Core;
using Xunit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// S1 SPEC (seed 4 idle leader, ruling seq 261): <see cref="Follow.FightAtOtherSeat"/> only looks for an engaged enemy within
/// <see cref="Follow.ReturnReachMm"/> of a living non-leader seat. A ranged enemy (nine enemy abilities reach past 5000 mm,
/// the largest brineMother.undertow at 27500 mm) can hold a follower from further away than that, so the returning leader
/// walks on and leaves the held seat behind. Here seat 1 is a returning leader (no living seat and no engaged enemy within
/// reach of it), seat 2 is the tank, and a living engaged enemy stands more than 5000 mm from every seat with HolderId = seat 2.
/// The fight at seat 2 must hand the lead to seat 2, so seat 1 issues no forward move: it holds or returns.
/// </summary>
public sealed class FollowRangedHolderTests
{
    private const int Tick = 900;
    private const int LeaderId = 1;
    private const int HeldSeatId = 2;
    private const int RangedEnemyId = 100;

    /// <summary>The ranged enemy: alive, engaged (not idle), 8000 mm from the held seat and 20000 mm from the leader.</summary>
    private static ActorState RangedHolder()
    {
        var enemy = RolesTestKit.Actor(
            id: RangedEnemyId, kind: ActorKind.Enemy, classId: "brineMother", role: ActorRole.None,
            x: 20000, z: 0, health: 1000, maxHealth: 1000, targetId: HeldSeatId);
        enemy.HolderId = HeldSeatId;
        return enemy;
    }

    [Fact(DisplayName = "Ranged holder past 5000 mm: a returning leader hands the lead to the seat the enemy holds and issues no forward move")]
    public void Leader_hands_over_to_seat_held_by_ranged_enemy_beyond_reach()
    {
        var leader = RolesTestKit.Actor(id: LeaderId, classId: "chanter", role: ActorRole.Healer, x: 0, z: 0, health: 300, maxHealth: 300);
        var held = RolesTestKit.Actor(id: HeldSeatId, classId: "warden", role: ActorRole.Tank, x: 12000, z: 0, health: 400, maxHealth: 400);
        var state = BotTestKit.MakeState(Tick, leader, held, RangedHolder());
        var view = BotTestKit.View(state);

        var returnSeat = Follow.ReturnSeat(state.Actors[LeaderId], state);
        var leaderOf = Follow.LeaderOf(view);
        var leaderText = leaderOf is null ? "null" : $"seat {leaderOf.Id}";
        var returnText = returnSeat is null ? "null" : $"seat {returnSeat.Id}";

        Assert.True(returnSeat is not null,
            $"precondition: seat 1 is not a returning leader (ReturnSeat null); LeaderOf={leaderText}");
        Assert.True(leaderOf is not null && leaderOf.Id == HeldSeatId,
            $"the returning leader seat 1 kept the lead while seat 2 is held by a ranged enemy 8000+ mm away: " +
            $"LeaderOf={leaderText}, seat 1 ReturnSeat={returnText} (expected seat 2, the held seat)");
        Assert.Null(Follow.LeadCommand(view, state.Actors[LeaderId]));
    }
}
