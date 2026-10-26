using System.Collections.Generic;
using Keyrun.Bots.Shared;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Xunit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// G2.5 / f-7b374e72 (Matt 2026-10-25: "Tanks very often will run ahead of the group while they wait out of LOS, and grab all
/// the packs and bring them back... he should not be just running it down"). S1 SPEC pin for the precedence gap: the tank has
/// already tagged gather A and B (both hurt, both still engaged and held), and a non-gather pack C is idle and in reach. The
/// walk-back to the gather spot must win over chaining the next pack while the gathered packs are still engaged.
/// Fixture mirrors <see cref="TankGatherReturnTests"/>: same layout, same actor builders, same plan shape.
/// </summary>
public sealed class TankPullbackPrecedenceTests
{
    private const int Tick = 1000;
    private const int TankId = 1;
    private const int ChanterId = 2;
    private const int CinderId = 3;
    private const int PackAFirstId = 100;
    private const int PackASecondId = 101;
    private const int PackBFirstId = 200;
    private const int PackBSecondId = 201;
    private const int PackCId = 300;

    /// <summary>The corridor (box 0) and the room (box 1), in route order; box 1 overlaps box 0 along x = 10000..11000.</summary>
    private static readonly Bounds Corridor = new(5000, 11000, -4500, 4500);
    private static readonly Bounds Room = new(10000, 26000, -8000, 8000);

    private static readonly NavLayout Layout = new(
        new[] { new NavBox("corridor", Corridor, false, null), new NavBox("room", Room, true, null) },
        new[] { new NavTrigger("p1", Corridor), new NavTrigger("p2", Room) },
        new[] { new Vec3(10500, 0, 0), new Vec3(21000, 0, 0) });

    /// <summary>The tank stands in the room, near pack B, at (21500,-2000).</summary>
    private static ActorState Tank() => RolesTestKit.Actor(
        id: TankId, classId: "warden", role: ActorRole.Tank, x: 21500, z: -2000, health: 400, maxHealth: 400);

    private static ActorState Chanter() => RolesTestKit.Actor(
        id: ChanterId, classId: "chanter", role: ActorRole.Healer, x: 16000, z: -1000, health: 300, maxHealth: 300);

    private static ActorState Cinder() => RolesTestKit.Actor(
        id: CinderId, classId: "cinder", role: ActorRole.Damage, x: 16000, z: 1000, health: 300, maxHealth: 300);

    /// <summary>Gathered pack member: engaged, held by the tank, hurt to 100 of 1000, leashed to its own post.</summary>
    private static ActorState HurtEngagedMember(int id, string classId, long x, long z)
    {
        var member = RolesTestKit.Actor(
            id: id, kind: ActorKind.Enemy, classId: classId, role: ActorRole.None, x: x, z: z, health: 100, maxHealth: 1000, targetId: TankId);
        member.Idle = false;
        member.HolderId = TankId;
        member.Home = member.Pos;
        member.LeashMm = 30000;
        return member;
    }

    /// <summary>Non-gather pack C member: idle, full health, in reach of the tank but not yet pulled.</summary>
    private static ActorState IdlePackMember(int id, string classId, long x, long z)
    {
        var member = RolesTestKit.Actor(
            id: id, kind: ActorKind.Enemy, classId: classId, role: ActorRole.None, x: x, z: z, health: 1000, maxHealth: 1000);
        member.Idle = true;
        member.Home = member.Pos;
        member.LeashMm = 30000;
        return member;
    }

    private static SimState State() => BotTestKit.MakeState(
        Tick,
        Tank(),
        Chanter(),
        Cinder(),
        HurtEngagedMember(PackAFirstId, "pair.melee", 14000, -5000),
        HurtEngagedMember(PackASecondId, "pair.melee", 14000, -7000),
        HurtEngagedMember(PackBFirstId, "pair.melee", 21500, 1000),
        HurtEngagedMember(PackBSecondId, "pair.melee", 21500, -1000),
        IdlePackMember(PackCId, "pair.melee", 21500, 6000));

    /// <summary>Groups A(0) and B(1) are gathered; C(2) is the next, non-gather pack.</summary>
    private static PullPlan Plan(IReadOnlyList<GatherSpot> gathers) => new(
        new ForcesPlan(new Dictionary<string, int>(), 1000),
        new List<PackGroup>(),
        new List<int>())
    {
        Groups = new List<AggroGroup>
        {
            new("A", 0, new List<int> { PackAFirstId, PackASecondId }, new Vec3(14000, 0, -6000), 3000, new List<string>()),
            new("B", 1, new List<int> { PackBFirstId, PackBSecondId }, new Vec3(21500, 0, 0), 3000, new List<string>()),
            new("C", 2, new List<int> { PackCId }, new Vec3(21500, 0, 6000), 3000, new List<string>()),
        },
        Gathers = gathers,
    };

    [Fact(DisplayName = "Tank pullback: walking a tagged gather back with hurt packs A and B engaged and a non-gather pack C pending in reach, the tank walks west to the spot")]
    public void Tank_walks_gathered_packs_back_before_chaining_pack_C()
    {
        var state = State();
        var tank = state.Actors[TankId];
        var plan = Plan(new[] { new GatherSpot("g", new Vec3(8000, 0, 0), new[] { "A", "B" }) });

        // Anti-vacuity: the walk-back spot must exist and the chain must be aiming at pack C, otherwise the fixture
        // does not reproduce the precedence question at all.
        Assert.NotNull(GatherReturn.Spot(state, Layout, plan, tank));
        Assert.Equal("C", ChainPull.Intent(state, Layout, plan, tank)?.Id);

        var move = ChainPull.Move(Tick, state, Layout, plan, tank);
        Assert.True(move is not null && move.Dir.X < 0,
            $"expected the tank to walk back west toward the gather spot (8000,0,0) before chaining pack C, but Move returned {(move is null ? "null" : $"dir ({move.Dir.X},{move.Dir.Z})")}");
    }
}
