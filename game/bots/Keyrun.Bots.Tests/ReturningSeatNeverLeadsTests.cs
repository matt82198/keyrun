using System.Collections.Generic;
using Keyrun.Bots.Shared;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Xunit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// R-PATH-5 (t), WoW: the tank leads an engaged pull; a player running back from the graveyard is never the party leader.
/// A seat whose <see cref="Follow.ReturnSeat"/> is non-null is never <see cref="Follow.LeaderOf"/> while the party is engaged.
/// Fact (a) is the RED pin: seat 1 (the chanter) is a returning seat that has left box 0 (the Entrance) and walks back toward
/// the engaged tank in seat 2 (the warden). <see cref="Follow.LeadHandedOver"/> returns false while the lead seat stands outside
/// box 0, so <see cref="Follow.LeaderOf"/> keeps naming seat 1 and the engaged warden is demoted to a follower.
/// Fact (b) is the CONTROL: the same party with seat 1 standing inside the engaged room (ReturnSeat null) keeps seat 1 as the lead.
/// </summary>
public sealed class ReturningSeatNeverLeadsTests
{
    private const int Tick = 711;
    private const int ChanterId = 1;
    private const int TankId = 2;
    private const int CinderId = 3;
    private const int ChoirId = 100;

    /// <summary>Box 0 is the Entrance, box 1 the corridor the returning seat walks through, box 2 the engaged room.</summary>
    private static readonly Bounds Entrance = new(12000, 22000, -3000, 3000);
    private static readonly Bounds Corridor = new(12000, 22000, 3000, 10000);
    private static readonly Bounds EngagedRoom = new(12000, 22000, 10000, 20000);

    private static readonly NavLayout Layout = new(
        new[]
        {
            new NavBox("entrance", Entrance, true, null),
            new NavBox("corridor", Corridor, true, null),
            new NavBox("engaged", EngagedRoom, true, null),
        },
        new[] { new NavTrigger("p0", Entrance), new NavTrigger("p1", Corridor), new NavTrigger("p2", EngagedRoom) },
        new[] { new Vec3(17000, 0, 0), new Vec3(17000, 0, 6500), new Vec3(17000, 0, 15000) });

    /// <summary>The respawned seat: alive, in the corridor (box 1, out of the Entrance), far from the tank and the Choir.</summary>
    private static ActorState ReturningChanter() => RolesTestKit.Actor(
        id: ChanterId, classId: "chanter", role: ActorRole.Healer, x: 17000, z: 5000, health: 300, maxHealth: 300);

    /// <summary>The engaged-room seat: the bot tank, alive, 12000 mm ahead of the returning seat, fighting the Choir.</summary>
    private static ActorState Tank() => RolesTestKit.Actor(
        id: TankId, classId: "warden", role: ActorRole.Tank, x: 17000, z: 17000, health: 400, maxHealth: 400);

    /// <summary>The third seat, beside the tank in the engaged room.</summary>
    private static ActorState Cinder() => RolesTestKit.Actor(
        id: CinderId, classId: "cinder", role: ActorRole.Damage, x: 17800, z: 17000, health: 250, maxHealth: 250);

    /// <summary>The engaged Choir: alive, not idle, targeting the tank, inside the engaged room.</summary>
    private static ActorState Choir() => RolesTestKit.Actor(
        id: ChoirId, kind: ActorKind.Enemy, classId: "choir.melee", role: ActorRole.None, x: 17000, z: 18000, health: 500, maxHealth: 1000, targetId: TankId);

    /// <summary>Builds the state, with the lead seat (seat 1) at the given actor.</summary>
    private static BotView ViewOf(params ActorState[] actors)
    {
        var state = BotTestKit.MakeState(Tick, actors);
        return BotTestKit.View(state) with { Layout = Layout };
    }

    [Fact(DisplayName = "Returning seat is not the leader: a seat walking back from the entrance does not take the lead from the engaged tank")]
    public void Returning_seat_is_not_the_leader_while_the_tank_is_engaged()
    {
        var view = ViewOf(ReturningChanter(), Tank(), Cinder(), Choir());
        var chanter = view.State.Actors[ChanterId];

        var returnSeat = Follow.ReturnSeat(chanter, view.State);
        var leaderOf = Follow.LeaderOf(view);
        var leaderText = leaderOf is null ? "null" : $"seat {leaderOf.Id}";
        var returnText = returnSeat is null ? "null" : $"seat {returnSeat.Id}";
        var box = Layout.BoxIndexAt(chanter.Pos);

        Assert.True(returnSeat is not null,
            $"precondition: seat 1 is not a returning seat (ReturnSeat null); LeaderOf={leaderText}, seat 1 ReturnSeat={returnText}, seat 1 box index={box}");
        Assert.True(box != 0,
            $"precondition: seat 1 is still in box 0 (the Entrance); LeaderOf={leaderText}, seat 1 ReturnSeat={returnText}, seat 1 box index={box}");
        Assert.True(leaderOf is not null && leaderOf.Id == TankId,
            $"the returning seat 1 became the party leader while the tank is engaged: LeaderOf={leaderText}, seat 1 ReturnSeat={returnText}, seat 1 box index={box} (expected seat 2, the engaged tank)");
    }

    [Fact(DisplayName = "Returning seat control: the lowest living seat standing with the party in the engaged room still leads")]
    public void Lowest_living_seat_with_the_party_leads()
    {
        // Seat 1 stands in the engaged room next to the tank, so it is not returning and the lead stays on seat 1.
        var chanter = RolesTestKit.Actor(
            id: ChanterId, classId: "chanter", role: ActorRole.Healer, x: 17000, z: 16000, health: 300, maxHealth: 300);
        var view = ViewOf(chanter, Tank(), Cinder(), Choir());
        var seat1 = view.State.Actors[ChanterId];

        var returnSeat = Follow.ReturnSeat(seat1, view.State);
        var leaderOf = Follow.LeaderOf(view);
        var leaderText = leaderOf is null ? "null" : $"seat {leaderOf.Id}";
        var returnText = returnSeat is null ? "null" : $"seat {returnSeat.Id}";
        var box = Layout.BoxIndexAt(seat1.Pos);

        Assert.True(returnSeat is null,
            $"precondition: seat 1 is a returning seat (ReturnSeat={returnText}); LeaderOf={leaderText}, seat 1 box index={box}");
        Assert.True(leaderOf is not null && leaderOf.Id == ChanterId,
            $"the lowest living seat with the party did not lead: LeaderOf={leaderText}, seat 1 ReturnSeat={returnText}, seat 1 box index={box} (expected seat 1)");
    }
}
