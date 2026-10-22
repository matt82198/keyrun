using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Roles;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Xunit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// KICK-LEDGER-COOLDOWN-GAP (finding f-13b5c33a; design/BIBLE.md section 9, the Choir teaches the kick ledger). In the
/// Playtest default run (key 2 seed 1) the Choir's Choral Mending at 639..719 resolved because the Chanter's and Cinder's
/// kicks were on cooldown and the Warden's 3 m Shield Bash was ready but 7 m away, so the ledger named nobody. The WoW
/// Mythic+ rule: the next must-kick goes to whichever seat has an interrupt READY by the cast's resolve tick; a seat whose
/// kick is on cooldown but comes off it before the cast resolves waits and kicks late; a melee kicker out of reach walks
/// in. The tank's kick counts. These cases pin that on one 80-tick Mending (100..180, kick opens at 140), integer ticks.
/// </summary>
public sealed class KickCooldownGapTests
{
    private const int ChoirId = 100;
    private const int Start = 100;
    private const int Ends = 180;
    private const int Open = 140;
    private const long CasterX = 5000;
    private const int OnCooldown = 480;

    private static readonly Lazy<NavLayout> Nav = new(() => NavLayouts.ToSim(ContentLoader.Load().Layout));

    private static ActorState Choir() => RolesTestKit.Actor(
        id: ChoirId, kind: ActorKind.Enemy, classId: "choir", role: ActorRole.Healer, x: CasterX,
        health: 1000, maxHealth: 1000, castId: "choir.healCast", castStartTick: Start, castEndsTick: Ends);

    private static string InterruptOf(string classId) =>
        RoleShared.KitOf(RolesTestKit.ViewOf(0, RolesTestKit.RealKits, null), classId).InterruptAbilityId!;

    private static ActorState Seat(int id, string classId, ActorRole role, int interruptReady, long x = 4000) => RolesTestKit.Actor(
        id: id, classId: classId, role: role, x: x, health: 1000, maxHealth: 1000,
        cooldowns: new[] { (InterruptOf(classId), interruptReady) });

    private static SortedDictionary<int, ActorState> Table(params ActorState[] actors) =>
        new(actors.ToDictionary(a => a.Id));

    private static BotView View(int tick, params ActorState[] actors) =>
        RolesTestKit.ViewOf(tick, RolesTestKit.RealKits, null, actors) with { Layout = Nav.Value };

    /// <summary>Every seat's partyInterrupt seat tuple, built the way RoleShared.LedgerKickCommand builds it.</summary>
    private static InterruptAssignment? Resolve(int tick, params ActorState[] actors)
    {
        var view = View(tick, actors);
        var seats = view.State.Actors.Values
            .Where(a => a.Kind == ActorKind.Player)
            .Select(a => (a.Id, Kit: RoleShared.KitOf(view, a.ClassId)))
            .Select(s => (ActorId: s.Id, InterruptAbilityId: s.Kit.InterruptAbilityId!, RangeMm: (long)view.Kits.Content!.Abilities[s.Kit.InterruptAbilityId!].Range, InterruptOffGcd: Readiness.IsOffGcd(s.Kit, s.Kit.InterruptAbilityId!)))
            .ToList();
        return Ledger.ResolvePartyInterrupt(view.State.Actors, seats, tick, view.Kits.PriorityInterruptAbilityIds());
    }

    private static CastCommand? LedgerKick(int tick, int seatId, params ActorState[] actors)
    {
        var view = View(tick, actors);
        var self = view.State.Actors[seatId];
        return RoleShared.LedgerKickCommand(view, self, RoleShared.KitOf(view, self.ClassId));
    }

    private static MoveCommand? Approach(int tick, int seatId, params ActorState[] actors)
    {
        var view = View(tick, actors);
        var self = view.State.Actors[seatId];
        return RoleShared.LedgerKickApproachCommand(view, self, RoleShared.KitOf(view, self.ClassId));
    }

    [Theory(DisplayName = "Kick ledger gap: the seat whose cooldown ends before the resolve tick is named, even though it is not ready yet")]
    [InlineData(141)]
    [InlineData(170)]
    [InlineData(179)]
    public void Seat_whose_cooldown_ends_before_resolve_is_named(int chanterReady)
    {
        var assignment = Resolve(Open,
            Choir(), Seat(1, "warden", ActorRole.Tank, OnCooldown), Seat(2, "chanter", ActorRole.Healer, chanterReady), Seat(3, "cinder", ActorRole.Damage, OnCooldown));
        Assert.NotNull(assignment);
        Assert.Equal(ChoirId, assignment!.TargetId);
        Assert.Equal(2, assignment.KickerId);
    }

    [Theory(DisplayName = "Kick ledger gap: a cooldown that ends on or after the resolve tick names nobody (the cast completes before that tick's commands)")]
    [InlineData(180)]
    [InlineData(300)]
    public void Cooldown_ending_at_or_after_resolve_names_nobody(int chanterReady)
    {
        var assignment = Resolve(Open,
            Choir(), Seat(1, "warden", ActorRole.Tank, OnCooldown), Seat(2, "chanter", ActorRole.Healer, chanterReady), Seat(3, "cinder", ActorRole.Damage, OnCooldown));
        Assert.Null(assignment);
    }

    [Fact(DisplayName = "Kick ledger gap: of the seats that come off cooldown before resolve, the earliest lands first (Cinder 160 before Chanter 170)")]
    public void Earliest_landing_wins_among_waiting_seats()
    {
        var assignment = Resolve(Open,
            Choir(), Seat(1, "warden", ActorRole.Tank, OnCooldown), Seat(2, "chanter", ActorRole.Healer, 170), Seat(3, "cinder", ActorRole.Damage, 160));
        Assert.Equal(3, assignment?.KickerId);
    }

    [Fact(DisplayName = "Kick ledger gap: the tank's kick counts (Warden 1 comes off cooldown first and is named over later seats)")]
    public void Tank_kick_counts()
    {
        var assignment = Resolve(Open,
            Choir(), Seat(1, "warden", ActorRole.Tank, 150), Seat(2, "chanter", ActorRole.Healer, 170), Seat(3, "cinder", ActorRole.Damage, 175));
        Assert.Equal(1, assignment?.KickerId);
    }

    [Fact(DisplayName = "Kick ledger gap: a seat ready NOW and in range still beats a seat that is merely about to be ready")]
    public void Ready_in_range_beats_waiting()
    {
        var assignment = Resolve(Open,
            Choir(), Seat(1, "warden", ActorRole.Tank, 150), Seat(2, "chanter", ActorRole.Healer, 0), Seat(3, "cinder", ActorRole.Damage, 145));
        Assert.Equal(2, assignment?.KickerId);
    }

    [Fact(DisplayName = "Kick ledger gap: the Warden, ready but 7 m from the caster, is named when every ranged kick is on cooldown past the resolve")]
    public void Out_of_reach_warden_is_named_when_it_can_walk_in_before_resolve()
    {
        var assignment = Resolve(Open,
            Choir(), Seat(1, "warden", ActorRole.Tank, 0, x: -2000), Seat(2, "chanter", ActorRole.Healer, 300), Seat(3, "cinder", ActorRole.Damage, 300));
        Assert.Equal(1, assignment?.KickerId);
    }

    [Fact(DisplayName = "Kick ledger gap: a Warden too far to walk in before the resolve is not named (a kick that cannot land is no assignment)")]
    public void Warden_that_cannot_reach_in_time_is_not_named()
    {
        var assignment = Resolve(Open,
            Choir(), Seat(1, "warden", ActorRole.Tank, 0, x: -20000), Seat(2, "chanter", ActorRole.Healer, 300), Seat(3, "cinder", ActorRole.Damage, 300));
        Assert.Null(assignment);
    }

    [Fact(DisplayName = "Kick ledger gap: a named seat whose cooldown has not ended waits (no kick press), then presses the tick it is ready")]
    public void Named_seat_waits_for_its_cooldown_then_kicks()
    {
        Assert.Null(LedgerKick(160, 2,
            Choir(), Seat(1, "warden", ActorRole.Tank, OnCooldown), Seat(2, "chanter", ActorRole.Healer, 170), Seat(3, "cinder", ActorRole.Damage, OnCooldown)));
        var kick = LedgerKick(170, 2,
            Choir(), Seat(1, "warden", ActorRole.Tank, OnCooldown), Seat(2, "chanter", ActorRole.Healer, 170), Seat(3, "cinder", ActorRole.Damage, OnCooldown));
        Assert.Equal(InterruptOf("chanter"), kick?.AbilityId);
        Assert.Equal(ChoirId, kick?.TargetId);
    }

    [Fact(DisplayName = "Kick ledger gap: the out-of-reach Warden walks toward the caster, then presses Shield Bash once inside 3 m")]
    public void Out_of_reach_warden_walks_in_then_bashes()
    {
        ActorState[] Far() => new[] { Choir(), Seat(1, "warden", ActorRole.Tank, 0, x: -2000), Seat(2, "chanter", ActorRole.Healer, 300), Seat(3, "cinder", ActorRole.Damage, 300) };
        Assert.Null(LedgerKick(Open, 1, Far()));
        var step = Approach(Open, 1, Far());
        Assert.NotNull(step);
        Assert.True(step!.Dir.X > 0, "the Warden walks toward the Choir (+x), not away");

        ActorState[] Near() => new[] { Choir(), Seat(1, "warden", ActorRole.Tank, 0, x: 2500), Seat(2, "chanter", ActorRole.Healer, 300), Seat(3, "cinder", ActorRole.Damage, 300) };
        Assert.Null(Approach(Open + 24, 1, Near()));
        var bash = LedgerKick(Open + 24, 1, Near());
        Assert.Equal(InterruptOf("warden"), bash?.AbilityId);
        Assert.Equal(ChoirId, bash?.TargetId);
    }

    [Fact(DisplayName = "Kick ledger gap: a seat that is not the named kicker never walks in for the kick")]
    public void Unnamed_seat_does_not_approach()
    {
        Assert.Null(Approach(Open, 1,
            Choir(), Seat(1, "warden", ActorRole.Tank, 0, x: -2000), Seat(2, "chanter", ActorRole.Healer, 0), Seat(3, "cinder", ActorRole.Damage, 300)));
    }
}
