using System;
using System.Collections.Generic;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Xunit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// G2.5 / f-7b374e72 S1 SPEC (Matt 2026-10-25: "Tanks very often will run ahead of the group while they wait out of LOS, and
/// grab all the packs and bring them back... he should not be just running it down"). While the tank has a non-null chain
/// intent (<see cref="ChainPull.Intent"/>, the next non-gather pack C), the non-tank seats must not follow him toward the packs:
/// they hold at the gather spot the tank is walking back to, until he returns or the pull is engaged at that spot.
/// Fixture mirrors <see cref="TankPullbackPrecedenceTests"/> (same layout, actor builders and plan shape) with the chanter and
/// cinder placed at the gather spot (8000,0,0), where the pull's party waits.
/// </summary>
public sealed class PartyHoldsGatherDuringChainPullTests
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

    /// <summary>The gather spot the tank walks the gathered packs back to (TankPullbackPrecedenceTests fixture).</summary>
    private static readonly Vec3 Spot = new(8000, 0, 0);

    /// <summary>
    /// The "small radius" the party holds within: the gather spot's own arrival radius (<see cref="GatherReturn.ArriveMm"/>,
    /// 2000 mm), the distance at which the walk-back counts as having reached the spot. Seats are placed inside it.
    /// </summary>
    private const long HoldRadiusMm = GatherReturn.ArriveMm;

    /// <summary>The corridor (box 0) and the room (box 1), in route order; box 1 overlaps box 0 along x = 10000..11000.</summary>
    private static readonly Bounds Corridor = new(5000, 11000, -4500, 4500);
    private static readonly Bounds Room = new(10000, 26000, -8000, 8000);

    private static readonly NavLayout Layout = new(
        new[] { new NavBox("corridor", Corridor, false, null), new NavBox("room", Room, true, null) },
        new[] { new NavTrigger("p1", Corridor), new NavTrigger("p2", Room) },
        new[] { new Vec3(10500, 0, 0), new Vec3(21000, 0, 0) });

    /// <summary>The tank stands in the room near pack B, at (21500,-2000), as in TankPullbackPrecedenceTests.</summary>
    private static ActorState Tank() => RolesTestKit.Actor(
        id: TankId, classId: "warden", role: ActorRole.Tank, x: 21500, z: -2000, health: 400, maxHealth: 400);

    /// <summary>The chanter waits at the gather spot, 1000 mm north of it (inside <see cref="HoldRadiusMm"/>).</summary>
    private static ActorState Chanter() => RolesTestKit.Actor(
        id: ChanterId, classId: "chanter", role: ActorRole.Healer, x: 8000, z: -1000, health: 300, maxHealth: 300);

    /// <summary>The cinder waits at the gather spot, 1000 mm south of it (inside <see cref="HoldRadiusMm"/>).</summary>
    private static ActorState Cinder() => RolesTestKit.Actor(
        id: CinderId, classId: "cinder", role: ActorRole.Damage, x: 8000, z: 1000, health: 300, maxHealth: 300);

    /// <summary>Gathered pack member: engaged, held by the tank, hurt to 100 of 1000, leashed to its own post.</summary>
    private static ActorState HurtEngagedMember(int id, long x, long z)
    {
        var member = RolesTestKit.Actor(
            id: id, kind: ActorKind.Enemy, classId: "pair.melee", role: ActorRole.None, x: x, z: z, health: 100, maxHealth: 1000, targetId: TankId);
        member.Idle = false;
        member.HolderId = TankId;
        member.Home = member.Pos;
        member.LeashMm = 30000;
        return member;
    }

    /// <summary>Fully engaged pack member at full health, held by the tank, leashed to its own post.</summary>
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

    /// <summary>Non-gather pack C member: idle, full health, in reach of the tank but not yet pulled.</summary>
    private static ActorState IdlePackMember(int id, long x, long z)
    {
        var member = RolesTestKit.Actor(
            id: id, kind: ActorKind.Enemy, classId: "pair.melee", role: ActorRole.None, x: x, z: z, health: 1000, maxHealth: 1000);
        member.Idle = true;
        member.Home = member.Pos;
        member.LeashMm = 30000;
        return member;
    }

    /// <summary>The chain fixture: gathered packs A and B hurt and engaged in the room, pack C idle and in reach, the party at the spot.</summary>
    private static SimState ChainState() => BotTestKit.MakeState(
        Tick,
        Tank(),
        Chanter(),
        Cinder(),
        HurtEngagedMember(PackAFirstId, 14000, -5000),
        HurtEngagedMember(PackASecondId, 14000, -7000),
        HurtEngagedMember(PackBFirstId, 21500, 1000),
        HurtEngagedMember(PackBSecondId, 21500, -1000),
        IdlePackMember(PackCId, 21500, 6000));

    /// <summary>The control fixture: the same tank and party, packs A and B all engaged at full health, no pack pending.</summary>
    private static SimState ControlState() => BotTestKit.MakeState(
        Tick,
        Tank(),
        Chanter(),
        Cinder(),
        EngagedMember(PackAFirstId, 14000, -5000),
        EngagedMember(PackASecondId, 14000, -7000),
        EngagedMember(PackBFirstId, 21500, 1000),
        EngagedMember(PackBSecondId, 21500, -1000));

    /// <summary>Groups A(0) and B(1) are gathered; with <paramref name="withPackC"/>, C(2) is the next, non-gather pack.</summary>
    private static PullPlan Plan(IReadOnlyList<GatherSpot> gathers, bool withPackC) => new(
        new ForcesPlan(new Dictionary<string, int>(), 1000),
        new List<PackGroup>(),
        new List<int>())
    {
        Groups = withPackC
            ? new List<AggroGroup>
            {
                new("A", 0, new List<int> { PackAFirstId, PackASecondId }, new Vec3(14000, 0, -6000), 3000, new List<string>()),
                new("B", 1, new List<int> { PackBFirstId, PackBSecondId }, new Vec3(21500, 0, 0), 3000, new List<string>()),
                new("C", 2, new List<int> { PackCId }, new Vec3(21500, 0, 6000), 3000, new List<string>()),
            }
            : new List<AggroGroup>
            {
                new("A", 0, new List<int> { PackAFirstId, PackASecondId }, new Vec3(14000, 0, -6000), 3000, new List<string>()),
                new("B", 1, new List<int> { PackBFirstId, PackBSecondId }, new Vec3(21500, 0, 0), 3000, new List<string>()),
            },
        Gathers = gathers,
    };

    /// <summary>The bot view over <paramref name="state"/> with the layout, the pull plan and the real content formation.</summary>
    private static BotView View(SimState state, PullPlan plan) =>
        BotTestKit.View(state) with { Layout = Layout, PullPlan = plan, Kits = KitRegistry.From(ContentLoader.Load()) };

    /// <summary>True when <paramref name="p"/> lies within <see cref="HoldRadiusMm"/> of the gather spot.</summary>
    private static bool WithinHoldRadius(Vec3 p)
    {
        long dx = p.X - Spot.X;
        long dz = p.Z - Spot.Z;
        return dx * dx + dz * dz <= HoldRadiusMm * HoldRadiusMm;
    }

    [Fact(DisplayName = "Chain pull holds the party: while the tank chain-pulls pack C from the gather spot, each non-tank seat holds within 2000 mm of the spot")]
    public void Chain_pull_holds_non_tank_seats_at_the_gather_spot()
    {
        var state = ChainState();
        var tank = state.Actors[TankId];
        // Pack C is a member of the gather set: the tank is chain-pulling a gather-tagged pack back to the spot (R-GATHER-1).
        var gathers = new[] { new GatherSpot("g", Spot, new[] { "A", "B", "C" }) };
        var plan = Plan(gathers, withPackC: true);

        // Anti-vacuity: the chained pack C is in Gathers and the chain intent names pack C, and both non-tank seats start inside
        // the hold radius, so a non-null follow move would take them away from the spot. (GatherReturn.Spot is null here by design:
        // while a gather-tagged group is still pending, GatherNext is open and the hold comes from the open gather tag instead.)
        Assert.Contains("C", gathers[0].GroupIds);
        Assert.Equal("C", ChainPull.Intent(state, Layout, plan, tank)?.Id);
        Assert.True(WithinHoldRadius(state.Actors[ChanterId].Pos), "fixture: the chanter must start inside the hold radius");
        Assert.True(WithinHoldRadius(state.Actors[CinderId].Pos), "fixture: the cinder must start inside the hold radius");

        var view = View(state, plan);
        foreach (var seatId in new[] { ChanterId, CinderId })
        {
            var seat = state.Actors[seatId];
            var move = Follow.FollowCommand(view, seat);
            Assert.True(move is null,
                $"expected seat {seatId} to hold at the gather spot (8000,0,0) while the tank chain-pulls pack C, but FollowCommand " +
                $"walked it dir ({move?.Dir.X},{move?.Dir.Z}) toward the packs");
        }
    }

    [Fact(DisplayName = "Chain pull control: with no chain intent, the non-tank seats follow the tank as today and walk toward him")]
    public void Without_a_chain_intent_the_followers_walk_toward_the_tank()
    {
        var state = ControlState();
        var tank = state.Actors[TankId];
        var plan = Plan(Array.Empty<GatherSpot>(), withPackC: false);

        // Anti-vacuity: every pack is engaged and none is pending, so the chain intent is null and no gather walk-back exists.
        Assert.Null(ChainPull.Intent(state, Layout, plan, tank));
        Assert.Null(GatherReturn.Spot(state, Layout, plan, tank));

        var cinder = state.Actors[CinderId];
        var move = Follow.FollowCommand(View(state, plan), cinder);
        Assert.True(move is not null && move.Dir.X > 0,
            $"control: expected the cinder to walk east toward the tank in the room, but FollowCommand returned " +
            $"{(move is null ? "null" : $"dir ({move.Dir.X},{move.Dir.Z})")}");
    }

    [Fact(DisplayName = "Chain pull of a non-gather pack: while the tank chain-pulls pack C, which no gather holds, the non-tank seats walk toward the tank")]
    public void Chain_pull_of_a_non_gather_pack_lets_the_followers_walk_toward_the_tank()
    {
        var state = ChainState();
        var tank = state.Actors[TankId];

        // Gathers hold A and B only; the chained pack C is outside every gather, so the party follows the tank (R-GATHER-1, the
        // non-gather branch; mirrors PartyFollowsTankToNextGroupTests).
        var gathers = new[] { new GatherSpot("g", Spot, new[] { "A", "B" }) };
        var plan = Plan(gathers, withPackC: true);

        // Anti-vacuity: the chain intent names pack C, C is not in Gathers, and the walk-back spot exists.
        Assert.DoesNotContain("C", gathers[0].GroupIds);
        Assert.Equal("C", ChainPull.Intent(state, Layout, plan, tank)?.Id);
        Assert.NotNull(GatherReturn.Spot(state, Layout, plan, tank));

        var cinder = state.Actors[CinderId];
        var move = Follow.FollowCommand(View(state, plan), cinder);
        Assert.True(move is not null && move.Dir.X > 0,
            $"expected the cinder to walk east toward the tank while the tank chain-pulls non-gather pack C, but FollowCommand returned " +
            $"{(move is null ? "null" : $"dir ({move.Dir.X},{move.Dir.Z})")}");
    }
}
