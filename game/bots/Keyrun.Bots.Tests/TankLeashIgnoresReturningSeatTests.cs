using System.Collections.Generic;
using Keyrun.Bots.Shared;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Xunit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// R-PATH-5 amendment (r) and R-BOSS-6 (d), dungeon loop, defer to WoW Mythic+: a tank holds the pull while a dead player runs
/// back; the party does not retreat to meet the corpse-runner. Measured defect (S5 trace, tick 711..750): the respawned leader
/// seat walks back from the Entrance, and <see cref="ChainPull.PartyLeashMm"/> (12000 mm) counts that returning seat as a
/// straggler, so the bot tank's chain-pull stops and it retreats. The fixture is the smallest one: a warden tank in room 0,
/// one engaged straggler at melee range, a chainable idle pack in the corridor ahead, and a chanter seat far behind it.
/// Fact (a) is the RED pin: the chanter is returning (<see cref="Follow.ReturnSeat"/> non-null for it), so the leash must not
/// fire and the tank must chain toward the pack. Fact (b) is the CONTROL: the same geometry with a chanter that is not
/// returning (a living enemy stands within <see cref="ReturnReachMm"/> of it) keeps the leash firing as today.
/// </summary>
public sealed class TankLeashIgnoresReturningSeatTests
{
    private const int Tick = 711;
    private const int TankId = 2;
    private const int ChanterId = 1;
    private const int EngagedId = 100;
    private const int PackId = 200;
    private const int NearChanterEnemyId = 201;

    /// <summary>Room 0 (the tank's room) and corridor 1 (the next pack), in route order; box 1 overlaps box 0 along z = 3000.</summary>
    private static readonly Bounds Room = new(12000, 22000, -3000, 3000);
    private static readonly Bounds Corridor = new(12000, 22000, 3000, 20000);

    private static readonly NavLayout Layout = new(
        new[] { new NavBox("room", Room, true, null), new NavBox("corridor", Corridor, true, null) },
        new[] { new NavTrigger("p1", Room), new NavTrigger("p2", Corridor) },
        new[] { new Vec3(17000, 0, 0), new Vec3(17000, 0, 9000) });

    private static ActorState Tank() => RolesTestKit.Actor(
        id: TankId, classId: "warden", role: ActorRole.Tank, x: 17016, z: 152, health: 400, maxHealth: 400);

    /// <summary>The respawned leader seat: alive, 13016 mm from the tank (past the 12000 mm leash), at the Entrance.</summary>
    private static ActorState Chanter() => RolesTestKit.Actor(
        id: ChanterId, classId: "chanter", role: ActorRole.Healer, x: 4000, z: 0, health: 300, maxHealth: 300);

    /// <summary>The engaged straggler: alive, not idle, 1500 mm in front of the tank (inside melee reach), low health.</summary>
    private static ActorState Engaged() => RolesTestKit.Actor(
        id: EngagedId, kind: ActorKind.Enemy, classId: "pair.melee", role: ActorRole.None, x: 17016, z: 1652, health: 100, maxHealth: 1000, targetId: TankId);

    /// <summary>The next pack: idle, centred in corridor 1, about 8800 mm from the tank (inside the 25000 mm reach).</summary>
    private static ActorState PackMember()
    {
        var pack = RolesTestKit.Actor(
            id: PackId, kind: ActorKind.Enemy, classId: "pair.melee", role: ActorRole.None, x: 17000, z: 9000, health: 1000, maxHealth: 1000);
        pack.Idle = true;
        return pack;
    }

    private static PullPlan Plan() => new(
        new ForcesPlan(new Dictionary<string, int>(), 1000),
        new List<PackGroup>(),
        new List<int>())
    {
        Groups = new List<AggroGroup> { new("pack", 1, new List<int> { PackId }, new Vec3(17000, 0, 9000), 3000, new List<string>()) },
    };

    /// <summary>The state with the tank, the chanter, the engaged straggler and the idle pack, plus any extra actors.</summary>
    private static SimState State(params ActorState[] extra)
    {
        var actors = new List<ActorState> { Tank(), Chanter(), Engaged(), PackMember() };
        actors.AddRange(extra);
        return BotTestKit.MakeState(Tick, actors.ToArray());
    }

    [Fact(DisplayName = "Tank leash ignores a returning seat: a respawned seat walking back from the entrance does not stop the tank's chain-pull toward the next pack")]
    public void Tank_chain_pull_is_not_leashed_by_a_returning_seat()
    {
        var state = State();
        var tank = state.Actors[TankId];
        var chanter = state.Actors[ChanterId];

        // Precondition: the chanter is a returning seat (Follow.ReturnSeat names the tank as the seat it walks back to).
        var returnSeat = Follow.ReturnSeat(chanter, state);
        Assert.True(returnSeat is not null && returnSeat.Id == TankId,
            "precondition: the chanter is not a returning seat (Follow.ReturnSeat is null), so this fact does not model the respawn walk");
        Assert.NotNull(ChainPull.Intent(state, Layout, Plan(), tank));

        var move = ChainPull.Move(Tick, state, Layout, Plan(), tank);
        Assert.True(move is not null,
            $"the tank's chain-pull leash fired on the returning chanter at {chanter.Pos.X},{chanter.Pos.Z} " +
            $"({Dist(chanter, tank)} mm from the tank, leash {ChainPull.PartyLeashMm} mm): no chain move toward the pack was issued");
        Assert.True(move!.Dir.Z > 0,
            $"the tank's chain move points away from the pack (dir {move.Dir.X},{move.Dir.Z}); it is a walk back toward the returning seat");
    }

    [Fact(DisplayName = "Tank leash control: a far seat that is not returning still fires the 12000 mm leash and stops the tank's chain-pull")]
    public void Tank_chain_pull_is_leashed_by_a_far_seat_that_is_not_returning()
    {
        // A living enemy stands 2000 mm from the chanter, so the chanter is in a fight and is not a returning seat.
        var nearChanter = RolesTestKit.Actor(
            id: NearChanterEnemyId, kind: ActorKind.Enemy, classId: "pair.melee", role: ActorRole.None, x: 4000, z: 2000, health: 50, maxHealth: 1000, targetId: ChanterId);
        var state = State(nearChanter);
        var tank = state.Actors[TankId];
        var chanter = state.Actors[ChanterId];

        Assert.Null(Follow.ReturnSeat(chanter, state));
        Assert.NotNull(ChainPull.Intent(state, Layout, Plan(), tank));
        Assert.True(Dist(chanter, tank) > ChainPull.PartyLeashMm, "precondition: the chanter is not more than PartyLeashMm from the tank");

        Assert.Null(ChainPull.Move(Tick, state, Layout, Plan(), tank));
    }

    /// <summary>Planar distance between two actors, mm, rounded down.</summary>
    private static long Dist(ActorState a, ActorState b)
    {
        var dx = a.Pos.X - b.Pos.X;
        var dz = a.Pos.Z - b.Pos.Z;
        return (long)System.Math.Sqrt((double)(dx * dx + dz * dz));
    }
}
