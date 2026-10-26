using System.Collections.Generic;
using Keyrun.Bots.Shared;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Xunit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// G2.5 / f-7b374e72 (Matt 2026-10-25: "Tanks very often will run ahead of the group while they wait out of LOS, and grab all
/// the packs and bring them back... he should not be just running it down"). S1 SPEC pin: both pack A and pack B are engaged
/// and held by the tank, and the tank stands in the room near pack B at (21500,0,-2000). With a gather spot at (8000,0,0)
/// naming A then B, the tank's chain move must walk back west toward the gather spot instead of staying in the room.
/// The fixture mirrors <see cref="TankGatherTests"/>: same layout, same actor builders, same plan shape.
/// </summary>
public sealed class TankGatherReturnTests
{
    private const int Tick = 1000;
    private const int TankId = 2;
    private const int ChanterId = 1;
    private const int CinderId = 3;
    private const int PackAFirstId = 100;
    private const int PackASecondId = 101;
    private const int PackBFirstId = 200;
    private const int PackBSecondId = 201;

    /// <summary>The corridor (box 0) and the room (box 1), in route order; box 1 overlaps box 0 along x = 10000..11000.</summary>
    private static readonly Bounds Corridor = new(5000, 11000, -4500, 4500);
    private static readonly Bounds Room = new(10000, 26000, -8000, 8000);

    private static readonly NavLayout Layout = new(
        new[] { new NavBox("corridor", Corridor, false, null), new NavBox("room", Room, true, null) },
        new[] { new NavTrigger("p1", Corridor), new NavTrigger("p2", Room) },
        new[] { new Vec3(10500, 0, 0), new Vec3(21000, 0, 0) });

    /// <summary>The tank stands in the room, near pack B.</summary>
    private static ActorState Tank() => RolesTestKit.Actor(
        id: TankId, classId: "warden", role: ActorRole.Tank, x: 21500, z: -2000, health: 400, maxHealth: 400);

    private static ActorState Chanter() => RolesTestKit.Actor(
        id: ChanterId, classId: "chanter", role: ActorRole.Healer, x: 8000, z: -1000, health: 300, maxHealth: 300);

    private static ActorState Cinder() => RolesTestKit.Actor(
        id: CinderId, classId: "cinder", role: ActorRole.Damage, x: 8000, z: 1000, health: 300, maxHealth: 300);

    /// <summary>Pack A member: engaged at its post, held by the tank, full health, leashed to its own post.</summary>
    private static ActorState EngagedMember(int id, string classId, long x, long z)
    {
        var member = RolesTestKit.Actor(
            id: id, kind: ActorKind.Enemy, classId: classId, role: ActorRole.None, x: x, z: z, health: 1000, maxHealth: 1000, targetId: TankId);
        member.Idle = false;
        member.HolderId = TankId;
        member.Home = member.Pos;
        member.LeashMm = 30000;
        return member;
    }

    private static SimState State() => BotTestKit.MakeState(
        Tick,
        Tank(),
        Chanter(),
        Cinder(),
        EngagedMember(PackAFirstId, "pair.melee", 14000, -5000),
        EngagedMember(PackASecondId, "pair.melee", 14000, -7000),
        EngagedMember(PackBFirstId, "pair.melee", 21500, 1000),
        EngagedMember(PackBSecondId, "pair.melee", 21500, -1000));

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

    [Fact(DisplayName = "Tank gather return: with both packs engaged and a gather spot, the tank walks back west toward the gather spot")]
    public void Tank_walks_the_tagged_gather_back_to_the_spot()
    {
        var state = State();
        var tank = state.Actors[TankId];
        var plan = Plan(new[] { new GatherSpot("g", new Vec3(8000, 0, 0), new[] { "A", "B" }) });

        var move = ChainPull.Move(Tick, state, Layout, plan, tank);
        Assert.True(move?.Dir.X < 0,
            $"expected the tank to walk back west toward the gather spot (8000,0,0), but Move returned {(move is null ? "null" : $"dir ({move.Dir.X},{move.Dir.Z})")}");
    }

    [Fact(DisplayName = "Tank gather return control: a tank within arrive distance of the gather spot does not walk back")]
    public void Tank_within_arrive_distance_of_the_spot_does_not_walk_back()
    {
        // The same fixture as above, with the tank moved to (8500, 500): about 707 mm from the spot at (8000,0,0), inside
        // GatherReturn.ArriveMm (2000). Both packs stay engaged, so the chain holds and the walk-back has no reason to fire.
        var near = RolesTestKit.Actor(
            id: TankId, classId: "warden", role: ActorRole.Tank, x: 8500, z: 500, health: 400, maxHealth: 400);
        var state = BotTestKit.MakeState(
            Tick,
            near,
            Chanter(),
            Cinder(),
            EngagedMember(PackAFirstId, "pair.melee", 14000, -5000),
            EngagedMember(PackASecondId, "pair.melee", 14000, -7000),
            EngagedMember(PackBFirstId, "pair.melee", 21500, 1000),
            EngagedMember(PackBSecondId, "pair.melee", 21500, -1000));
        var plan = Plan(new[] { new GatherSpot("g", new Vec3(8000, 0, 0), new[] { "A", "B" }) });

        var move = ChainPull.Move(Tick, state, Layout, plan, near);
        Assert.True(move is null,
            $"expected no move for a tank already at the gather spot, but Move returned dir ({move?.Dir.X},{move?.Dir.Z})");
    }
}
