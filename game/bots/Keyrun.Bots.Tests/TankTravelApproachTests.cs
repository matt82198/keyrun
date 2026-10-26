using System;
using System.Collections.Generic;
using Keyrun.Bots.Roles;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Xunit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// Travel must not drop the bot tank's approach (WoW Mythic+: the tank pulls the pack that is hitting the party). Measured
/// defect (S5 healer seed 5, pack hall.sentry 507): the leader seat fights inside a room while the bot Warden stands in the
/// corridor outside the room's inset. <see cref="Follow.FollowCommand"/> returns a route walk, so Bot.cs sets
/// <c>travelling = true</c> and drops the role's MoveCommands; <see cref="Roles.TankEngage.ApproachMove"/> is only issued
/// when <c>!travelling</c>, so no approach move ever fires and the Warden walks the route while the enemy hits the healer.
/// Rule under test: an engaged enemy aimed at a non-tank party seat, with the bot tank travelling, must draw a tank approach
/// (or engage) MoveCommand toward that enemy within <see cref="WindowTicks"/> ticks; travel must not drop it.
/// The fixture is the real hollow-vault layout: the leader (lowest living seat, a human-style seat driven by no bot) stands in
/// the first room past the Entrance, the enemy is pinned on it, and the tank bot stands in the corridor box before that room.
/// The guards are the travel precondition (the route walk is issued) and the approach precondition (the approach move the
/// tank would issue without travel is non-null), so the only missing piece is the travel exemption.
/// </summary>
public sealed class TankTravelApproachTests
{
    /// <summary>Seed of the bot PRNG (the reaction and mistake rolls do not decide this fact; travel drops the approach outright).</summary>
    private const long Seed = 1;

    /// <summary>The first tick: past <see cref="Follow.IdleLeaderTicks"/>, so the lead is not handed over.</summary>
    private const int Tick = 2000;

    /// <summary>The window, in ticks, in which the tank must emit an approach or engage move toward the enemy.</summary>
    private const int WindowTicks = 20;

    /// <summary>The leader seat: the lowest living player, a healer that is driven by no bot in this fixture.</summary>
    private const int LeaderId = 1;

    /// <summary>The bot tank (Warden) seat.</summary>
    private const int TankId = 2;

    /// <summary>The engaged enemy, holder-locked on the leader seat.</summary>
    private const int EnemyId = 100;

    /// <summary>Offset (mm) of the enemy from the leader, inside the room, so the leader is the one it hits.</summary>
    private const long EnemyOffsetMm = 1500;

    private static readonly Lazy<ContentRegistry> Registry = new(() => ContentLoader.Load());

    /// <summary>The state for one tick: the leader in the room, the enemy on it, the tank in the corridor box before the room.</summary>
    private static SimState State(int tick, NavLayout layout, int room)
    {
        var roomBox = layout.Boxes[room].Box;
        var corridorBox = layout.Boxes[room - 1].Box;
        var (lx, lz) = Centre(roomBox);
        var (tx, tz) = Centre(corridorBox);

        var leader = RolesTestKit.Actor(
            id: LeaderId, classId: "chanter", role: ActorRole.Healer, x: lx, z: lz, health: 300, maxHealth: 300);
        var tank = RolesTestKit.Actor(
            id: TankId, classId: "warden", role: ActorRole.Tank, x: tx, z: tz, health: 400, maxHealth: 400);
        var enemy = RolesTestKit.Actor(
            id: EnemyId, kind: ActorKind.Enemy, classId: "pair.melee", role: ActorRole.None,
            x: lx + EnemyOffsetMm, z: lz, health: 1000, maxHealth: 1000, targetId: LeaderId);
        enemy.HolderId = LeaderId;
        return BotTestKit.MakeState(tick, leader, tank, enemy);
    }

    /// <summary>The centre of a box on the floor plane, mm.</summary>
    private static (long X, long Z) Centre(Bounds b) => ((b.MinX + b.MaxX) / 2, (b.MinZ + b.MaxZ) / 2);

    /// <summary>The first room box after the Entrance (box 0), and the corridor box before it. Throws when the layout has none.</summary>
    private static int FirstRoomAfterEntrance(NavLayout layout)
    {
        for (var k = 1; k < layout.Boxes.Count; k++)
        {
            if (layout.Boxes[k].IsRoom)
            {
                return k;
            }
        }
        throw new InvalidOperationException("the layout has no room box after the Entrance");
    }

    [Fact(DisplayName = "Tank travel approach: a travelling bot tank with an engaged enemy aimed at a non-tank seat emits an approach or engage move toward it within 20 ticks")]
    public void Travelling_tank_approaches_an_enemy_aimed_at_a_non_tank_seat()
    {
        var nav = NavLayouts.ToSim(Registry.Value.Layout);
        var room = FirstRoomAfterEntrance(nav);
        var kits = KitRegistry.From(Registry.Value);

        // Precondition 1 (travel): the tank is travelling, i.e. the route walk to its slot is issued while the leader fights.
        var state0 = State(Tick, nav, room);
        var view0 = BotViews.BuildBotView(state0, Array.Empty<BotTelegraph>(), ClassKitView.Empty, kits) with { Layout = nav };
        var tank0 = state0.Actors[TankId];
        var walk = Follow.FollowCommand(view0, tank0);
        Assert.True(walk is not null,
            "precondition: the tank is not travelling (Follow.FollowCommand is null), so this fact does not model the travel defect");

        // Precondition 2 (approach): without travel the tank would approach the enemy (the move exists and points at it).
        var approach0 = Roles.TankEngage.ApproachMove(view0, tank0, Roles.RoleShared.KitOf(view0, tank0.ClassId));
        Assert.True(approach0 is not null,
            "precondition: TankEngage.ApproachMove is null for the tank even before travel, so the enemy is in reach and the fact does not model an out-of-reach approach");

        var bots = new SortedDictionary<int, IBot> { [TankId] = Bot.MakeBot(RoleSet.Default) };
        var memories = new SortedDictionary<int, BotMemory> { [TankId] = BotMemory.Default(80) };
        IReadOnlyDictionary<int, BotMemory> mem = memories;
        var prng = Skill.SeedPrng(Seed);

        var trace = new List<string>();
        var found = false;
        var enemyAt = State(Tick, nav, room).Actors[EnemyId];
        for (var tick = Tick; tick < Tick + WindowTicks && !found; tick++)
        {
            var state = State(tick, nav, room);
            var view = BotViews.BuildBotView(state, Array.Empty<BotTelegraph>(), ClassKitView.Empty, kits) with { Layout = nav };
            var batch = Bot.RunBots(view, bots, mem, prng);
            mem = batch.Memories;
            prng = batch.Prng;

            var tank = state.Actors[TankId];
            for (var k = 0; k < batch.Commands.Count; k++)
            {
                if (batch.Commands[k] is not MoveCommand move || move.ActorId != TankId)
                {
                    continue;
                }
                var rule = batch.Reasons[k].RuleId;
                var towardEnemy = (long)move.Dir.X * (enemyAt.Pos.X - tank.Pos.X) + (long)move.Dir.Z * (enemyAt.Pos.Z - tank.Pos.Z) > 0;
                trace.Add($"tick {tick}: tank MoveCommand dir ({move.Dir.X},{move.Dir.Z}) rule {rule} toward enemy {towardEnemy}");
                if (towardEnemy && (rule == "bot:tank-approach" || rule.StartsWith("tank:", StringComparison.Ordinal)))
                {
                    found = true;
                }
            }
            if (trace.Count == 0)
            {
                trace.Add($"tick {tick}: no tank MoveCommand (tank at {tank.Pos.X},{tank.Pos.Z}, enemy at {enemyAt.Pos.X},{enemyAt.Pos.Z})");
            }
        }

        Assert.True(found,
            $"travel dropped the tank's approach: within {WindowTicks} ticks of tick {Tick} no approach or engage MoveCommand pointed at the enemy " +
            $"aimed at the leader seat (room box {nav.Boxes[room].Id}, leader at {enemyAt.Pos.X},{enemyAt.Pos.Z} with the enemy {EnemyOffsetMm} mm away). " +
            "Trace: " + string.Join(" | ", trace));
    }

    /// <summary>
    /// The state for one tick for the leash and idle facts: the same seats as <see cref="State"/>, with the enemy placed
    /// <paramref name="enemyDxMm"/> from the leader along x and set idle when <paramref name="idle"/> is true.
    /// </summary>
    private static SimState EnemyState(int tick, NavLayout layout, int room, long enemyDxMm, bool idle)
    {
        var roomBox = layout.Boxes[room].Box;
        var corridorBox = layout.Boxes[room - 1].Box;
        var (lx, lz) = Centre(roomBox);
        var (tx, tz) = Centre(corridorBox);

        var leader = RolesTestKit.Actor(
            id: LeaderId, classId: "chanter", role: ActorRole.Healer, x: lx, z: lz, health: 300, maxHealth: 300);
        var tank = RolesTestKit.Actor(
            id: TankId, classId: "warden", role: ActorRole.Tank, x: tx, z: tz, health: 400, maxHealth: 400);
        var enemy = RolesTestKit.Actor(
            id: EnemyId, kind: ActorKind.Enemy, classId: "pair.melee", role: ActorRole.None,
            x: lx + enemyDxMm, z: lz, health: 1000, maxHealth: 1000, targetId: LeaderId);
        enemy.HolderId = LeaderId;
        enemy.Idle = idle;
        if (idle)
        {
            // A sleeping pack the tank has already selected: the tank's own target is the idle enemy, so the engage
            // fallback (EngageTarget, current target) is the path under test.
            tank.TargetId = EnemyId;
        }
        return BotTestKit.MakeState(tick, leader, tank, enemy);
    }

    /// <summary>
    /// Runs the bots for <see cref="WindowTicks"/> ticks from <see cref="Tick"/> on the <see cref="EnemyState"/> fixture and
    /// reports whether the bot tank emitted an approach or engage MoveCommand toward the enemy, with a per-tick trace.
    /// </summary>
    private static (bool Found, List<string> Trace, ActorState Enemy) ScanApproachWindow(NavLayout nav, int room, long enemyDxMm, bool idle)
    {
        var kits = KitRegistry.From(Registry.Value);
        var bots = new SortedDictionary<int, IBot> { [TankId] = Bot.MakeBot(RoleSet.Default) };
        var memories = new SortedDictionary<int, BotMemory> { [TankId] = BotMemory.Default(80) };
        IReadOnlyDictionary<int, BotMemory> mem = memories;
        var prng = Skill.SeedPrng(Seed);

        var trace = new List<string>();
        var found = false;
        var enemyAt = EnemyState(Tick, nav, room, enemyDxMm, idle).Actors[EnemyId];
        for (var tick = Tick; tick < Tick + WindowTicks && !found; tick++)
        {
            var state = EnemyState(tick, nav, room, enemyDxMm, idle);
            var view = BotViews.BuildBotView(state, Array.Empty<BotTelegraph>(), ClassKitView.Empty, kits) with { Layout = nav };
            var batch = Bot.RunBots(view, bots, mem, prng);
            mem = batch.Memories;
            prng = batch.Prng;

            var tank = state.Actors[TankId];
            for (var k = 0; k < batch.Commands.Count; k++)
            {
                if (batch.Commands[k] is not MoveCommand move || move.ActorId != TankId)
                {
                    continue;
                }
                var rule = batch.Reasons[k].RuleId;
                var towardEnemy = (long)move.Dir.X * (enemyAt.Pos.X - tank.Pos.X) + (long)move.Dir.Z * (enemyAt.Pos.Z - tank.Pos.Z) > 0;
                trace.Add($"tick {tick}: tank MoveCommand dir ({move.Dir.X},{move.Dir.Z}) rule {rule} toward enemy {towardEnemy}");
                if (towardEnemy && (rule == "bot:tank-approach" || rule.StartsWith("tank:", StringComparison.Ordinal)))
                {
                    found = true;
                }
            }
            if (trace.Count == 0)
            {
                trace.Add($"tick {tick}: no tank MoveCommand (tank at {tank.Pos.X},{tank.Pos.Z}, enemy at {enemyAt.Pos.X},{enemyAt.Pos.Z})");
            }
        }
        return (found, trace, enemyAt);
    }

    [Fact(DisplayName = "Tank travel leash: a travelling bot tank does not approach an engaged enemy that is more than PartyLeashMm from every living seat")]
    public void Travelling_tank_does_not_approach_an_enemy_beyond_the_party_leash()
    {
        var nav = NavLayouts.ToSim(Registry.Value.Layout);
        var room = FirstRoomAfterEntrance(nav);
        var kits = KitRegistry.From(Registry.Value);
        const long farDxMm = 13000;

        // Precondition 1 (leash): the enemy is more than PartyLeashMm from the leader and from the tank, so no living seat
        // is in reach of the party's leash and the approach would out-run the party.
        var state0 = EnemyState(Tick, nav, room, farDxMm, idle: false);
        var enemy0 = state0.Actors[EnemyId];
        foreach (var seat in new[] { state0.Actors[LeaderId], state0.Actors[TankId] })
        {
            var dx = enemy0.Pos.X - seat.Pos.X;
            var dz = enemy0.Pos.Z - seat.Pos.Z;
            Assert.True(dx * dx + dz * dz > ChainPull.PartyLeashMm * ChainPull.PartyLeashMm,
                $"precondition: the enemy is within PartyLeashMm ({ChainPull.PartyLeashMm} mm) of seat {seat.Id}, so this fact does not model the leash");
        }

        // Precondition 2 (travel): the tank is travelling, i.e. the route walk to its slot is issued while the leader fights.
        var view0 = BotViews.BuildBotView(state0, Array.Empty<BotTelegraph>(), ClassKitView.Empty, kits) with { Layout = nav };
        var tank0 = state0.Actors[TankId];
        Assert.True(Follow.FollowCommand(view0, tank0) is not null,
            "precondition: the tank is not travelling (Follow.FollowCommand is null), so this fact does not model the travel defect");

        // Precondition 3 (approach): the approach move exists, so the test would catch a leash-free approach if one were issued.
        var approach0 = Roles.TankEngage.ApproachMove(view0, tank0, Roles.RoleShared.KitOf(view0, tank0.ClassId));
        Assert.True(approach0 is not null,
            "precondition: TankEngage.ApproachMove is null for the tank at the far enemy, so the fact would pass vacuously");

        var (found, trace, enemyAt) = ScanApproachWindow(nav, room, farDxMm, idle: false);
        Assert.False(found,
            $"the travelling tank approached an enemy beyond the party leash within {WindowTicks} ticks of tick {Tick} " +
            $"(enemy at {enemyAt.Pos.X},{enemyAt.Pos.Z}, {farDxMm} mm from the leader, PartyLeashMm {ChainPull.PartyLeashMm}). " +
            "Trace: " + string.Join(" | ", trace));
    }

    [Fact(DisplayName = "Tank travel idle: a travelling bot tank does not approach an Idle (sleeping) enemy whose TargetId names a seat")]
    public void Travelling_tank_does_not_approach_an_idle_enemy_aimed_at_a_seat()
    {
        var nav = NavLayouts.ToSim(Registry.Value.Layout);
        var room = FirstRoomAfterEntrance(nav);
        var kits = KitRegistry.From(Registry.Value);

        // Precondition 1 (travel): the tank is travelling, i.e. the route walk to its slot is issued.
        var state0 = EnemyState(Tick, nav, room, EnemyOffsetMm, idle: true);
        var view0 = BotViews.BuildBotView(state0, Array.Empty<BotTelegraph>(), ClassKitView.Empty, kits) with { Layout = nav };
        var tank0 = state0.Actors[TankId];
        Assert.True(Follow.FollowCommand(view0, tank0) is not null,
            "precondition: the tank is not travelling (Follow.FollowCommand is null), so this fact does not model the travel defect");

        // Precondition 2 (idle): the enemy is Idle and names the leader seat as its target, the sleeping-pack shape.
        var enemy0 = state0.Actors[EnemyId];
        Assert.True(enemy0.Idle && enemy0.Alive && enemy0.TargetId == LeaderId,
            "precondition: the enemy is not an Idle living enemy aimed at the leader seat, so the fact does not model a sleeping pack");

        // Precondition 3 (approach): the approach move exists for the idle enemy, so a missing Idle filter would show as an approach.
        var approach0 = Roles.TankEngage.ApproachMove(view0, tank0, Roles.RoleShared.KitOf(view0, tank0.ClassId));
        Assert.True(approach0 is not null,
            "precondition: TankEngage.ApproachMove is null for the tank at the idle enemy, so the fact would pass vacuously");

        var (found, trace, enemyAt) = ScanApproachWindow(nav, room, EnemyOffsetMm, idle: true);
        Assert.False(found,
            $"the travelling tank approached an Idle enemy within {WindowTicks} ticks of tick {Tick} " +
            $"(enemy at {enemyAt.Pos.X},{enemyAt.Pos.Z}, Idle, aimed at the leader seat). " +
            "Trace: " + string.Join(" | ", trace));
    }
}
