using System.Collections.Generic;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Xunit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// S1 SPEC for f-7b374e72 (R-AOE-1 (c), WoW M+): when the tank goes for a non-gather group (ChainPull.NextGroup), the followers
/// walk with him to their slots behind him. They hold at a gather spot only while a gather tag is open
/// (ChainPull.OpenGatherSpot) or the tank is walking a tagged gather back to its spot (GatherReturn.Spot).
/// The fixture is PartyWaitsAtGatherSpotTests: the tank is seat 1 (the leader) in the room, the gather spot is (8000,0,0).
/// Here the gather holds only pack A, which is engaged with a single member, so the gather tag is closed (GatherNext null)
/// and the tank's next group is pack B, a group outside every gather, so Intent names NextGroup and OpenGatherSpot is null.
/// </summary>
public sealed class PartyFollowsTankToNextGroupTests
{
    private const int Tick = 1000;
    private const int TankId = 1;
    private const int ChanterId = 2;
    private const int CinderId = 3;
    private const int PackAFirstId = 100;
    private const int PackBFirstId = 200;

    /// <summary>The gather spot the tank walked a tagged gather to (PartyWaitsAtGatherSpotTests fixture).</summary>
    private static readonly Vec3 Spot = new(8000, 0, 0);

    private static readonly Bounds Corridor = new(5000, 11000, -4500, 4500);
    private static readonly Bounds Room = new(10000, 26000, -8000, 8000);

    private static readonly NavLayout Layout = new(
        new[] { new NavBox("corridor", Corridor, false, null), new NavBox("room", Room, true, null) },
        new[] { new NavTrigger("p1", Corridor), new NavTrigger("p2", Room) },
        new[] { new Vec3(10500, 0, 0), new Vec3(21000, 0, 0) });

    /// <summary>The tank stands in the room, 10000 mm past the gather spot, walking nowhere yet: he is engaged with pack A.</summary>
    private static ActorState Tank() => RolesTestKit.Actor(
        id: TankId, classId: "warden", role: ActorRole.Tank, x: 18000, z: 0, health: 400, maxHealth: 400);

    private static ActorState Chanter() => RolesTestKit.Actor(
        id: ChanterId, classId: "chanter", role: ActorRole.Healer, x: 8000, z: -1000, health: 300, maxHealth: 300);

    private static ActorState Cinder() => RolesTestKit.Actor(
        id: CinderId, classId: "cinder", role: ActorRole.Damage, x: 8000, z: 0, health: 300, maxHealth: 300);

    /// <summary>The single pack A member: engaged at its post, held by the tank, and below 40% so the chain's engaged-health gate passes.</summary>
    private static ActorState EngagedAMember()
    {
        var member = RolesTestKit.Actor(
            id: PackAFirstId, kind: ActorKind.Enemy, classId: "pair.melee", role: ActorRole.None, x: 14000, z: -5000, health: 100, maxHealth: 1000, targetId: TankId);
        member.Idle = false;
        member.HolderId = TankId;
        member.Home = member.Pos;
        member.LeashMm = 30000;
        return member;
    }

    /// <summary>Pack B member: idle at its post in the room, outside every gather, the group the tank chains into next.</summary>
    private static ActorState IdleBMember()
    {
        var member = RolesTestKit.Actor(
            id: PackBFirstId, kind: ActorKind.Enemy, classId: "pair.melee", role: ActorRole.None, x: 21500, z: 1000, health: 1000, maxHealth: 1000);
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
        EngagedAMember(),
        IdleBMember());

    /// <summary>Group A (engaged, the only gather member) and group B (idle, outside every gather).</summary>
    private static PullPlan Plan() => new(
        new ForcesPlan(new Dictionary<string, int>(), 1000),
        new List<PackGroup>(),
        new List<int>())
    {
        Groups = new List<AggroGroup>
        {
            new("A", 0, new List<int> { PackAFirstId }, new Vec3(14000, 0, -5000), 3000, new List<string>()),
            new("B", 1, new List<int> { PackBFirstId }, new Vec3(21500, 0, 1000), 3000, new List<string>()),
        },
        Gathers = new[] { new GatherSpot("g", Spot, new[] { "A" }) },
    };

    /// <summary>The bot view over <paramref name="state"/> with the layout, the pull plan and the real content formation.</summary>
    private static BotView View(SimState state, PullPlan plan) =>
        BotTestKit.View(state) with { Layout = Layout, PullPlan = plan, Kits = KitRegistry.From(ContentLoader.Load()) };

    [Fact(DisplayName = "Party follows the tank to the next group: with a gather spot set and the tank chaining a non-gather group, a cinder at the spot walks toward the tank")]
    public void Party_follows_the_tank_when_he_chains_a_non_gather_group()
    {
        var state = State();
        var plan = Plan();
        var tank = state.Actors[TankId];

        // Anti-vacuity: the tank's intent is the non-gather group B (so no gather tag is open) and the walk-back spot is set,
        // so the move below is decided by the FollowCommand gather branch, not by a null fixture.
        var intent = ChainPull.Intent(state, Layout, plan, tank);
        Assert.NotNull(intent);
        Assert.Equal("B", intent!.Id);
        Assert.NotNull(GatherReturn.Spot(state, Layout, plan, tank));

        var cinder = state.Actors[CinderId];
        var move = Follow.FollowCommand(View(state, plan), cinder);
        Assert.True(move is not null && move.Dir.X > 0,
            $"expected the cinder to walk with the tank toward his slot east of the spot (tank at (18000,0), NextGroup B at (21500,0)), " +
            $"but FollowCommand returned {(move is null ? "null (the cinder holds at the gather spot (8000,0,0))" : $"dir ({move.Dir.X},{move.Dir.Z})")}");
    }
}
