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
/// KICK-NO-OVERHEAL (R-FORCES-COMPLETE-1 with f-7aa25aa6 / f-3dcc702c; WoW Mythic+ kick discipline). Pulling to 100% forces
/// brings the Warrior Remnant (two Cantors healing each other every 200 ticks) against three 480-tick kicks, which answer
/// about six of ten Mendings: spent on Mendings that heal nobody, the kicks are down for the Mending that lands on a Cantor
/// at 1400 health, and the pull stalls until the key is depleted. A must-kick heal cast by an optional-pack enemy is held
/// while no fighting enemy is missing a quarter of the healer's own health; the dungeon's own pulls teach the kick and are
/// always kicked. One 80-tick Mending (100..180, kick opens at 140), integer ticks.
/// </summary>
public sealed class OverhealHoldTests
{
    private const int CasterId = 500;
    private const int TeachingId = 100;
    private const int Tick = 150;

    private static readonly Lazy<NavLayout> Nav = new(() => NavLayouts.ToSim(ContentLoader.Load().Layout));

    private static ActorState Caster(int id, long health) => RolesTestKit.Actor(
        id: id, kind: ActorKind.Enemy, classId: "choir.lieutenant", role: ActorRole.Healer, x: 5000,
        health: health, maxHealth: 1000, castId: "choir.healCast", castStartTick: 100, castEndsTick: 180);

    private static ActorState Mob(int id, long health) => RolesTestKit.Actor(
        id: id, kind: ActorKind.Enemy, classId: "pair.melee", x: 5200, health: health, maxHealth: 400);

    private static ActorState Warden() => RolesTestKit.Actor(id: 1, classId: "warden", role: ActorRole.Tank, x: 4000, health: 1000, maxHealth: 1000);

    private static PullPlan Pulls(params int[] teaching) =>
        new(new ForcesPlan(new Dictionary<string, int>(), 1000), Array.Empty<PackGroup>(), teaching);

    private static InterruptAssignment? Resolve(PullPlan? pulls, params ActorState[] actors)
    {
        var view = RolesTestKit.ViewOf(Tick, RolesTestKit.RealKits, null, actors) with { Layout = Nav.Value, PullPlan = pulls };
        var seats = view.State.Actors.Values
            .Where(a => a.Kind == ActorKind.Player)
            .Select(a => (a.Id, Kit: RoleShared.KitOf(view, a.ClassId)))
            .Select(s => (ActorId: s.Id, InterruptAbilityId: s.Kit.InterruptAbilityId!, RangeMm: (long)view.Kits.Content!.Abilities[s.Kit.InterruptAbilityId!].Range, InterruptOffGcd: Readiness.IsOffGcd(s.Kit, s.Kit.InterruptAbilityId!)))
            .ToList();
        return Ledger.ResolvePartyInterrupt(view.State.Actors, seats, Tick, view.Kits.PriorityInterruptAbilityIds(), pulls);
    }

    [Fact(DisplayName = "Kick no overheal: an optional-pack Mending is held while no fighting enemy is hurt a quarter of the healer's health")]
    public void Unhurt_optional_pack_mending_is_held()
    {
        Assert.Null(Resolve(Pulls(TeachingId), Warden(), Caster(CasterId, 1000), Mob(501, 400)));
    }

    [Fact(DisplayName = "Kick no overheal: a small add missing health does not make a Cantor's Mending worth a kick")]
    public void A_hurt_small_add_does_not_count()
    {
        Assert.Null(Resolve(Pulls(TeachingId), Warden(), Caster(CasterId, 1000), Mob(501, 300)));
    }

    [Fact(DisplayName = "Kick no overheal: the Mending is kicked once an enemy is missing a quarter of the healer's health")]
    public void Hurt_enemy_makes_the_mending_worth_a_kick()
    {
        var assignment = Resolve(Pulls(TeachingId), Warden(), Caster(CasterId, 700), Mob(501, 400));
        Assert.NotNull(assignment);
        Assert.Equal(CasterId, assignment!.TargetId);
    }

    [Fact(DisplayName = "Kick no overheal: the dungeon's own pull teaches the kick, so its Mending is always kicked")]
    public void Teaching_pull_is_always_kicked()
    {
        Assert.NotNull(Resolve(Pulls(TeachingId), Warden(), Caster(TeachingId, 1000), Mob(501, 400)));
    }

    [Fact(DisplayName = "Kick no overheal: with no pull plan (the oracle runner) nothing is held")]
    public void No_pull_plan_holds_nothing()
    {
        Assert.NotNull(Resolve(null, Warden(), Caster(CasterId, 1000), Mob(501, 400)));
    }
}
