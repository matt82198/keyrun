using System.Collections.Generic;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Xunit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// G3 / f-7b374e72 (ruling R-THREAT-2 amendment (o), slice G3; amends R-BOTTANK-LEASH-1 for an open gather). Matt 2026-10-25:
/// "Tanks very often will run ahead of the group while they wait out of LOS, and grab all the packs and bring them back...
/// he should not be just running it down". With a gather open (the tank is tagging the gather's next pack at its spot), the
/// party waits at the gather spot instead of walking into the room the tank fights in, and the tank's leash is measured from
/// that spot, not from himself. S1 SPEC: the tank is the lowest living seat (the leader), engaged with pack A at its post in
/// the room, pack B idle in the same room (the gather's next pack), and the gather spot is (8000,0,0) in the corridor.
/// The fixture mirrors <see cref="TankGatherTests"/>, with the tank as seat 1 so he leads the party.
/// </summary>
public sealed class PartyWaitsAtGatherSpotTests
{
    private const int Tick = 1000;
    private const int TankId = 1;
    private const int ChanterId = 2;
    private const int CinderId = 3;
    private const int PackAFirstId = 100;
    private const int PackASecondId = 101;
    private const int PackBFirstId = 200;
    private const int PackBSecondId = 201;

    /// <summary>The gather spot every fact below walks back to (TankGatherTests fixture).</summary>
    private static readonly Vec3 Spot = new(8000, 0, 0);

    /// <summary>The corridor (box 0) and the room (box 1), in route order; box 1 overlaps box 0 along x = 10000..11000.</summary>
    private static readonly Bounds Corridor = new(5000, 11000, -4500, 4500);
    private static readonly Bounds Room = new(10000, 26000, -8000, 8000);

    private static readonly NavLayout Layout = new(
        new[] { new NavBox("corridor", Corridor, false, null), new NavBox("room", Room, true, null) },
        new[] { new NavTrigger("p1", Corridor), new NavTrigger("p2", Room) },
        new[] { new Vec3(10500, 0, 0), new Vec3(21000, 0, 0) });

    /// <summary>The tank stands in the room at pack A's post, holding it.</summary>
    private static ActorState Tank() => RolesTestKit.Actor(
        id: TankId, classId: "warden", role: ActorRole.Tank, x: 14000, z: -6000, health: 400, maxHealth: 400);

    private static ActorState Chanter(long x = 8000, long z = -1000) => RolesTestKit.Actor(
        id: ChanterId, classId: "chanter", role: ActorRole.Healer, x: x, z: z, health: 300, maxHealth: 300);

    private static ActorState Cinder(long x = 8000, long z = 1000) => RolesTestKit.Actor(
        id: CinderId, classId: "cinder", role: ActorRole.Damage, x: x, z: z, health: 300, maxHealth: 300);

    /// <summary>Pack A member: engaged at its post, held by the tank, full health, leashed to its own post.</summary>
    private static ActorState EngagedMember(int id, long x, long z)
    {
        var member = RolesTestKit.Actor(
            id: id, kind: ActorKind.Enemy, classId: "pair.melee", role: ActorRole.None, x: x, z: z, health: 1000, maxHealth: 1000, targetId: TankId);
        member.Idle = false;
        member.HolderId = TankId;
        member.Home = member.Pos;
        member.LeashMm = 30000;
        return member;
    }

    /// <summary>Pack B member: idle at its post in the room, the next pack the tank tags.</summary>
    private static ActorState IdleMember(int id, long x, long z)
    {
        var member = RolesTestKit.Actor(
            id: id, kind: ActorKind.Enemy, classId: "pair.melee", role: ActorRole.None, x: x, z: z, health: 1000, maxHealth: 1000);
        member.Idle = true;
        member.Home = member.Pos;
        member.LeashMm = 30000;
        return member;
    }

    private static SimState State(ActorState chanter, ActorState cinder) => BotTestKit.MakeState(
        Tick,
        Tank(),
        chanter,
        cinder,
        EngagedMember(PackAFirstId, 14000, -5000),
        EngagedMember(PackASecondId, 14000, -7000),
        IdleMember(PackBFirstId, 21500, 1000),
        IdleMember(PackBSecondId, 21500, -1000));

    private static PullPlan Plan(IReadOnlyList<GatherSpot> gathers) => new(
        new ForcesPlan(new Dictionary<string, int>(), 1000),
        new List<PackGroup>(),
        new List<int>())
    {
        Groups = new List<AggroGroup>
        {
            new("A", 0, new List<int> { PackAFirstId, PackASecondId }, new Vec3(14000, 0, -6000), 3000, new List<string>()),
            new("B", 1, new List<int> { PackBFirstId, PackBSecondId }, new Vec3(21500, 0, 0), 3000, new List<string>()),
        },
        Gathers = gathers,
    };

    /// <summary>The bot view over <paramref name="state"/> with the layout, the pull plan and the real content formation.</summary>
    private static BotView View(SimState state, PullPlan plan) =>
        BotTestKit.View(state) with { Layout = Layout, PullPlan = plan, Kits = KitRegistry.From(ContentLoader.Load()) };

    [Fact(DisplayName = "Gather wait control: with no gather spot, a cinder in the corridor walks into the room the tank fights in")]
    public void Without_a_gather_the_follower_still_walks_into_the_fight_room()
    {
        // Anti-vacuity: the fixture reaches the fight-in-room branch of FollowCommand, so the wait fact below is the rule at work.
        var state = State(Chanter(), Cinder());
        var cinder = state.Actors[CinderId];
        var view = View(state, Plan(System.Array.Empty<GatherSpot>()));

        Assert.NotNull(Follow.FollowCommand(view, cinder));
    }

    [Fact(DisplayName = "Party waits at the gather spot: while the tank tags the next gather pack, a cinder at the spot holds there instead of walking into the room")]
    public void Party_waits_at_the_gather_spot_while_the_tank_tags_the_next_pack()
    {
        var state = State(Chanter(), Cinder());
        var cinder = state.Actors[CinderId];
        var view = View(state, Plan(new[] { new GatherSpot("g", Spot, new[] { "A", "B" }) }));

        var move = Follow.FollowCommand(view, cinder);
        Assert.True(move is null,
            $"expected the cinder at the gather spot (8000,0,0) to hold while the tank tags pack B, but FollowCommand walked it " +
            $"dir ({move?.Dir.X},{move?.Dir.Z}) toward the fight room");
    }

    [Fact(DisplayName = "Party waits on the walk-back: the tank walks a tagged gather back to its spot with packs A and B engaged, so a cinder at the spot holds there")]
    public void Party_waits_at_the_gather_spot_while_the_tank_walks_back_from_the_packs()
    {
        // G3 walk-back (R-THREAT-2 (o)): the tank is in the room at (18000,0), walking the tagged gather back to (8000,0). Packs A and
        // B are both engaged, so no pack is pending and the tag is closed (ChainPull.OpenGatherSpot is null). The gather spot is
        // still the walk-back target (GatherReturn.Spot), so the cinder at the spot must hold instead of walking into the room.
        var tank = RolesTestKit.Actor(id: TankId, classId: "warden", role: ActorRole.Tank, x: 18000, z: 0, health: 400, maxHealth: 400);
        var state = BotTestKit.MakeState(
            Tick,
            tank,
            Chanter(),
            Cinder(x: 8000, z: 0),
            EngagedMember(PackAFirstId, 14000, -5000),
            EngagedMember(PackASecondId, 14000, -7000),
            EngagedMember(PackBFirstId, 21500, 1000),
            EngagedMember(PackBSecondId, 21500, -1000));
        var plan = Plan(new[] { new GatherSpot("g", Spot, new[] { "A", "B" }) });

        // Anti-vacuity: the walk-back condition holds (the leader is outside the spot and the tag is closed), so the hold below is the rule at work.
        Assert.NotNull(GatherReturn.Spot(state, Layout, plan, state.Actors[TankId]));
        Assert.Null(ChainPull.Intent(state, Layout, plan, state.Actors[TankId]));

        var cinder = state.Actors[CinderId];
        var move = Follow.FollowCommand(View(state, plan), cinder);
        Assert.True(move is null,
            $"expected the cinder at the gather spot (8000,0,0) to hold while the tank walks back from the packs, but FollowCommand walked it " +
            $"dir ({move?.Dir.X},{move?.Dir.Z}) toward the fight room");
    }

    [Fact(DisplayName = "Gather leash: the tank's leash is measured from the open gather spot, so a seat within 12000 mm of the spot lets him run ahead to the next pack")]
    public void Leash_is_measured_from_the_open_gather_spot()
    {
        // The cinder stands 12311 mm from the tank (over the 12000 mm leash), 3840 mm from the gather spot (inside it), and 4665 mm
        // from the chanter: inside Follow.ReturnReachMm (5000) of a living seat, so she is not a returning seat (R-PATH-5 (r)).
        var state = State(Chanter(), Cinder(x: 5600, z: 3000));
        var tank = state.Actors[TankId];
        var plan = Plan(new[] { new GatherSpot("g", Spot, new[] { "A", "B" }) });

        var move = ChainPull.Move(Tick, state, Layout, plan, tank);
        Assert.True(move is not null && move.Dir.X > 0,
            $"expected the tank to run ahead toward pack B (21500,0,0) because every seat is within the 12000 mm leash of the open " +
            $"gather spot (8000,0,0), but Move returned {(move is null ? "null" : $"dir ({move.Dir.X},{move.Dir.Z})")}");
    }

    [Fact(DisplayName = "Gather leash control: with every seat within the leash of the tank, the tank runs ahead to the next gather pack")]
    public void Leash_control_with_every_seat_near_the_tank_runs_ahead()
    {
        // Anti-vacuity: the same gather with the cinder inside 12000 mm of the tank, so the leash is not the reason for a null.
        var state = State(Chanter(), Cinder());
        var tank = state.Actors[TankId];
        var plan = Plan(new[] { new GatherSpot("g", Spot, new[] { "A", "B" }) });

        var move = ChainPull.Move(Tick, state, Layout, plan, tank);
        Assert.True(move is not null && move.Dir.X > 0,
            $"control: expected the tank to run ahead toward pack B, but Move returned {(move is null ? "null" : $"dir ({move.Dir.X},{move.Dir.Z})")}");
    }
}
