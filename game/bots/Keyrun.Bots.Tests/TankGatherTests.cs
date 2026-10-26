using System.Collections.Generic;
using Keyrun.Bots.Shared;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Xunit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// G2.5 / f-7b374e72 (Matt 2026-10-25: "Tanks very often will run ahead of the group while they wait out of LOS, and grab all
/// the packs and bring them back... he should not be just running it down"). A bot tank with a gather spot tags the next
/// gather pack instead of fighting the first pack at its post. The fixture is the smallest one: a warden tank in a room, a
/// fresh engaged pack A at its post (full health, not idle), an idle pack B further along the same room, and the party
/// (chanter and cinder) in the corridor where the gather spot is. Fact (a) is the RED pin: with <see cref="PullPlan.Gathers"/>
/// naming the gather, the tank's chain move points toward pack B. Fact (b) is the CONTROL: the same state with no gather keeps
/// today's rule, where a fresh engaged pack at its post holds the tank (<see cref="ChainPull"/> EngagedHealthPct).
/// </summary>
public sealed class TankGatherTests
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

    private static ActorState Tank() => RolesTestKit.Actor(
        id: TankId, classId: "warden", role: ActorRole.Tank, x: 14000, z: -6000, health: 400, maxHealth: 400);

    private static ActorState Chanter() => RolesTestKit.Actor(
        id: ChanterId, classId: "chanter", role: ActorRole.Healer, x: 8000, z: -1000, health: 300, maxHealth: 300);

    private static ActorState Cinder() => RolesTestKit.Actor(
        id: CinderId, classId: "cinder", role: ActorRole.Damage, x: 8000, z: 1000, health: 300, maxHealth: 300);

    /// <summary>Pack A member: at its post, not idle, held by the tank, full health, leashed to its own post.</summary>
    private static ActorState PackAMember(int id, long z)
    {
        var member = RolesTestKit.Actor(
            id: id, kind: ActorKind.Enemy, classId: "pair.melee", role: ActorRole.None, x: 14000, z: z, health: 1000, maxHealth: 1000, targetId: TankId);
        member.HolderId = TankId;
        member.Home = member.Pos;
        member.LeashMm = 30000;
        return member;
    }

    /// <summary>Pack B member: idle at its post in the room, the next pack the tank would tag.</summary>
    private static ActorState PackBMember(int id, long x, long z)
    {
        var member = RolesTestKit.Actor(
            id: id, kind: ActorKind.Enemy, classId: "pair.melee", role: ActorRole.None, x: x, z: z, health: 1000, maxHealth: 1000);
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
        PackAMember(PackAFirstId, -5000),
        PackAMember(PackASecondId, -7000),
        PackBMember(PackBFirstId, 21500, 1000),
        PackBMember(PackBSecondId, 21500, -1000));

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

    [Fact(DisplayName = "Tank gather: with a gather spot, a bot tank tags the next gather pack (pack B) instead of fighting the first pack at its post")]
    public void Tank_tags_the_next_gather_pack_instead_of_fighting_the_first_at_its_post()
    {
        var state = State();
        var tank = state.Actors[TankId];
        var plan = Plan(new[] { new GatherSpot("g", new Vec3(8000, 0, 0), new[] { "A", "B" }) });

        var move = ChainPull.Move(Tick, state, Layout, plan, tank);
        Assert.NotNull(move);
        Assert.True(move!.Dir.X > 0,
            $"the tank's move points away from pack B (dir {move.Dir.X},{move.Dir.Z}); it is still fighting pack A at its post");
    }

    [Fact(DisplayName = "Tank gather control: without a gather spot, a bot tank still fights a fresh pack at its post and does not chain away")]
    public void Without_a_gather_the_tank_still_fights_a_fresh_pack_at_its_post()
    {
        var state = State();
        var tank = state.Actors[TankId];
        var plan = Plan(System.Array.Empty<GatherSpot>());

        Assert.Null(ChainPull.Move(Tick, state, Layout, plan, tank));
    }

    /// <summary>F1 (review s5-tank-gather-review.txt): the engaged gather's own group is never its own next gather group.</summary>
    [Fact(DisplayName = "Tank gather F1: an engaged gather group with one idle straggler is not returned as the next gather group")]
    public void Engaged_gather_group_with_an_idle_straggler_is_not_its_own_next_gather_group()
    {
        var state = StateWith(Enemy(102, "pair.melee", 15000, -6000, idle: true));
        var tank = state.Actors[TankId];
        var plan = PlanOf(
            new[] { new GatherSpot("g", new Vec3(8000, 0, 0), new[] { "A", "B" }) },
            new[] { GroupA(new[] { PackAFirstId, PackASecondId, 102 }), GroupB(PackBFirstId, PackBSecondId) });

        var intent = ChainPull.Intent(state, Layout, plan, tank);
        Assert.False(intent?.Id == "A",
            $"GatherNext returned the engaged gather group A (its idle straggler is the only pending member); intent group was {intent?.Id}");
    }

    /// <summary>F4 (a): a next gather group holding an elite or boss is never gathered.</summary>
    [Fact(DisplayName = "Tank gather F4a: a next gather group holding a boss is not gathered (no gather move)")]
    public void Gather_group_holding_an_elite_or_boss_is_not_gathered()
    {
        var state = StateWith(Enemy(202, "boss.warden", 21500, -1000, idle: true));
        var tank = state.Actors[TankId];
        var gathers = new[] { new GatherSpot("g", new Vec3(8000, 0, 0), new[] { "A", "B" }) };
        var groups = new[] { GroupA(new[] { PackAFirstId, PackASecondId }), GroupB(PackBFirstId, 202) };

        // Anti-vacuity: with no blocked class the same state does gather pack B, so the block below is the rule at work.
        var unblocked = PlanOf(gathers, groups);
        Assert.Equal("B", ChainPull.Intent(state, Layout, unblocked, tank)?.Id);

        var blocked = PlanOf(gathers, groups, new HashSet<string> { "boss.warden" });
        Assert.Null(ChainPull.Intent(state, Layout, blocked, tank));
        Assert.Null(ChainPull.Move(Tick, state, Layout, blocked, tank));
    }

    /// <summary>F4 (b): a seat beyond the 12000 mm leash wins over a gather move.</summary>
    [Fact(DisplayName = "Tank gather F4b: a seat beyond the 12000 mm leash holds the tank, the gather move does not go")]
    public void Seat_beyond_the_leash_wins_over_a_gather_move()
    {
        // The far seat (13000 mm from the tank) has a living neighbour within ReturnReachMm, so it is a straggler and not a seat
        // walking back from a respawn (Follow.ReturnSeat would exempt it from the leash). The neighbour is inside the leash.
        var state = StateWith();
        var farSeat = RolesTestKit.Actor(id: 4, kind: ActorKind.Player, classId: "cinder", role: ActorRole.Damage, x: 14000, z: 7000, health: 300, maxHealth: 300);
        var neighbour = RolesTestKit.Actor(id: 5, kind: ActorKind.Player, classId: "chanter", role: ActorRole.Healer, x: 14000, z: 5000, health: 300, maxHealth: 300);
        state.Actors[farSeat.Id] = farSeat;
        state.Actors[neighbour.Id] = neighbour;
        var tank = state.Actors[TankId];
        var plan = PlanOf(
            new[] { new GatherSpot("g", new Vec3(8000, 0, 0), new[] { "A", "B" }) },
            new[] { GroupA(new[] { PackAFirstId, PackASecondId }), GroupB(PackBFirstId, PackBSecondId) });

        // Anti-vacuity: the gather path is live (it names pack B), so the null move below is the leash at work.
        Assert.Equal("B", ChainPull.Intent(state, Layout, plan, tank)?.Id);
        Assert.Null(ChainPull.Move(Tick, state, Layout, plan, tank));
    }

    /// <summary>F4 (c): a declared gather with no engaged gather group behaves exactly like plain NextGroup.</summary>
    [Fact(DisplayName = "Tank gather F4c: a gather declared with no engaged gather group behaves like plain NextGroup")]
    public void Gather_declared_without_an_engaged_gather_group_matches_next_group()
    {
        // A lone engaged enemy (300, group C, not in the gather) is a straggler, so the health hold does not apply and the
        // control compares the gather's result with plain NextGroup on the same state.
        var state = BotTestKit.MakeState(
            Tick,
            Tank(),
            Chanter(),
            Cinder(),
            Enemy(300, "pair.melee", 9000, 0, idle: false),
            Enemy(100, "pair.melee", 14000, -5000, idle: true),
            Enemy(101, "pair.melee", 14000, -7000, idle: true),
            PackBMember(PackBFirstId, 21500, 1000),
            PackBMember(PackBSecondId, 21500, -1000));
        var tank = state.Actors[TankId];
        var plan = PlanOf(
            new[] { new GatherSpot("g", new Vec3(8000, 0, 0), new[] { "A", "B" }) },
            new[] { GroupA(new[] { 100, 101 }), GroupB(PackBFirstId, PackBSecondId), new AggroGroup("C", 2, new List<int> { 300 }, new Vec3(9000, 0, 0), 3000, new List<string>()) });

        var next = ChainPull.NextGroup(state, Layout, plan, tank);
        Assert.NotNull(next);
        Assert.Equal(next!.Id, ChainPull.Intent(state, Layout, plan, tank)?.Id);
    }

    /// <summary>F4 (d): a next gather group behind the leader is not gathered.</summary>
    [Fact(DisplayName = "Tank gather F4d: a next gather group behind the leader is not gathered")]
    public void Gather_group_behind_the_leader_is_not_gathered()
    {
        var state = StateWith();
        var tank = state.Actors[TankId];
        var plan = PlanOf(
            new[] { new GatherSpot("g", new Vec3(8000, 0, 0), new[] { "A", "B" }) },
            new[] { GroupA(new[] { PackAFirstId, PackASecondId }), GroupB(PackBFirstId, PackBSecondId, new Vec3(8000, 0, 0)) });

        Assert.Null(ChainPull.Intent(state, Layout, plan, tank));
    }

    /// <summary>F4 (d): a next gather group beyond ReachMm of the leader is not gathered.</summary>
    [Fact(DisplayName = "Tank gather F4d: a next gather group beyond ReachMm is not gathered")]
    public void Gather_group_beyond_reach_is_not_gathered()
    {
        var state = StateWith();
        var tank = state.Actors[TankId];
        var wideRoom = new Bounds(10000, 60000, -8000, 8000);
        var wideLayout = new NavLayout(
            new[] { new NavBox("corridor", Corridor, false, null), new NavBox("room", wideRoom, true, null) },
            new[] { new NavTrigger("p1", Corridor), new NavTrigger("p2", wideRoom) },
            new[] { new Vec3(10500, 0, 0), new Vec3(21000, 0, 0) });
        var plan = PlanOf(
            new[] { new GatherSpot("g", new Vec3(8000, 0, 0), new[] { "A", "B" }) },
            new[] { GroupA(new[] { PackAFirstId, PackASecondId }), GroupB(PackBFirstId, PackBSecondId, new Vec3(50000, 0, 0)) });

        Assert.Null(ChainPull.Intent(state, wideLayout, plan, tank));
    }

    /// <summary>F3 (review s5-tank-gather-review.txt): a gather never returns a pending pack the forces plan does not want.</summary>
    [Fact(DisplayName = "Tank gather F3: a gather never returns a pending pack the plan does not want, and still returns the wanted one")]
    public void Gather_never_returns_a_pending_pack_the_plan_does_not_want()
    {
        // Group C (PullIndex -1, idle, in reach and ahead of the leader) is not in the plan's wanted set, and the empty forces plan
        // picks no pack. It is listed first in the gather, so an unfiltered GatherNext would return it ahead of wanted group B.
        var state = StateWith(Enemy(210, "pair.melee", 21500, 4000, idle: true), Enemy(211, "pair.melee", 21500, 5000, idle: true));
        var tank = state.Actors[TankId];
        var plan = PlanOf(
            new[] { new GatherSpot("g", new Vec3(8000, 0, 0), new[] { "A", "C", "B" }) },
            new[]
            {
                GroupA(new[] { PackAFirstId, PackASecondId }),
                new AggroGroup("C", -1, new List<int> { 210, 211 }, new Vec3(21500, 0, 4500), 3000, new List<string>()),
                GroupB(PackBFirstId, PackBSecondId),
            });

        var intent = ChainPull.Intent(state, Layout, plan, tank);
        Assert.False(intent?.Id == "C",
            "GatherNext returned pack C, which the forces plan does not want (PullIndex -1, not the planner's pick)");
        Assert.Equal("B", intent?.Id);
    }

    /// <summary>An enemy at its post with the given class; idle when it has not engaged, leashed to its own post.</summary>
    private static ActorState Enemy(int id, string classId, long x, long z, bool idle)
    {
        var m = RolesTestKit.Actor(id: id, kind: ActorKind.Enemy, classId: classId, role: ActorRole.None, x: x, z: z, health: 1000, maxHealth: 1000);
        m.Idle = idle;
        m.Home = m.Pos;
        m.LeashMm = 30000;
        return m;
    }

    /// <summary>The engaged-at-post fixture of the G2.5 tests, plus extra actors.</summary>
    private static SimState StateWith(params ActorState[] extra)
    {
        var actors = new List<ActorState> { Tank(), Chanter(), Cinder(), PackAMember(PackAFirstId, -5000), PackAMember(PackASecondId, -7000), PackBMember(PackBFirstId, 21500, 1000), PackBMember(PackBSecondId, 21500, -1000) };
        actors.AddRange(extra);
        return BotTestKit.MakeState(Tick, actors.ToArray());
    }

    private static AggroGroup GroupA(IReadOnlyList<int> ids) =>
        new("A", 0, ids, new Vec3(14000, 0, -6000), 3000, new List<string>());

    private static AggroGroup GroupB(int first, int second, Vec3? center = null) =>
        new("B", 1, new List<int> { first, second }, center ?? new Vec3(21500, 0, 0), 3000, new List<string>());

    private static PullPlan PlanOf(IReadOnlyList<GatherSpot> gathers, IReadOnlyList<AggroGroup> groups, IReadOnlySet<string>? blocked = null) => new(
        new ForcesPlan(new Dictionary<string, int>(), 1000) { ChainBlocked = blocked ?? new HashSet<string>() },
        new List<PackGroup>(),
        new List<int>())
    {
        Groups = groups,
        Gathers = gathers,
    };
}
