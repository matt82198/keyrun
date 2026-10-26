using System;
using System.Linq;
using Keyrun.Bots.Roles;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Xunit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// KICK-HOLD-UNINTERRUPTIBLE (S1 SPEC, hypothesis for the kick-hold Cleave red): the owed-kick hold must not count a
/// boss cast the content marks uninterruptible. The only casting enemy is the Hollow Warden's Cleave
/// (<c>hollowWarden.cleave</c>, <c>interruptible: false</c>); no kick can land on it, so no seat owes a kick and no damage
/// cast is held back by <see cref="RoleShared.OwedKickDeadline"/> or <see cref="RoleShared.FitsOwedKick(BotView, ActorState, string)"/>.
/// The control test runs the same fixture with an interruptible Choir heal and must produce an owed deadline, so the
/// negative assertion cannot pass vacuously (no layout, no assignment, no range).
/// </summary>
public sealed class KickHoldUninterruptibleTests
{
    private const string CleaveCast = "hollowWarden.cleave";
    private const string HealCast = "choir.healCast";
    private const int EnemyId = 100;
    private const int CastStart = 100;
    private const int CastTicks = 40;
    private const int CastEnds = CastStart + CastTicks;
    private const int CastTickDeadlineOffset = 60;

    private static readonly Lazy<NavLayout> Nav = new(() => NavLayouts.ToSim(ContentLoader.Load().Layout));

    private static string InterruptOf(string classId)
    {
        var view = RolesTestKit.ViewOf(0, RolesTestKit.RealKits, null);
        return RoleShared.KitOf(view, classId).InterruptAbilityId!;
    }

    private static ActorState Seat(int id, string classId, ActorRole role, long x = 4000) => RolesTestKit.Actor(
        id: id, classId: classId, role: role, x: x, health: 1000, maxHealth: 1000,
        cooldowns: new[] { (InterruptOf(classId), 0) });

    private static ActorState Caster(string castId, string classId) => RolesTestKit.Actor(
        id: EnemyId, kind: ActorKind.Enemy, classId: classId, role: ActorRole.Healer, x: 5000,
        health: 1000, maxHealth: 1000, castId: castId, castStartTick: CastStart, castEndsTick: CastEnds);

    private static BotView ViewWith(string castId, string classId, int tick) =>
        RolesTestKit.ViewOf(tick, RolesTestKit.RealKits, null,
            Caster(castId, classId), Seat(1, "warden", ActorRole.Tank), Seat(2, "chanter", ActorRole.Healer), Seat(3, "cinder", ActorRole.Damage))
        with { Layout = Nav.Value };

    [Fact(DisplayName = "Kick hold: an uninterruptible Cleave owes no kick to any seat, so no cast is held for it")]
    public void Uninterruptible_cast_owes_no_kick()
    {
        var view = ViewWith(CleaveCast, "hollowWarden", CastStart);
        var cinder = view.State.Actors[3];
        var payoff = RoleShared.KitOf(view, cinder.ClassId).PayoffAbilityIds.First();

        foreach (var seatId in new[] { 1, 2, 3 })
        {
            Assert.Null(RoleShared.OwedKickDeadline(view, view.State.Actors[seatId]));
        }

        Assert.True(RoleShared.FitsOwedKick(view, cinder, payoff), "a damage cast was held for a kick that can never land on an uninterruptible cast");
    }

    [Fact(DisplayName = "Kick hold control: an interruptible Choir heal does owe a kick, so the uninterruptible case above is not vacuous")]
    public void Interruptible_cast_owes_a_kick()
    {
        var view = ViewWith(HealCast, "choir", CastStart);

        var deadline = RoleShared.OwedKickDeadline(view, view.State.Actors[1]);

        Assert.Equal(CastStart + CastTickDeadlineOffset, deadline);
    }
}
