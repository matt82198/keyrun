using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Roles;
using Keyrun.Sim.Core;
using Xunit;
using static Keyrun.Bots.Tests.RolesTestKit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// f-c0201273 (G7 R-K10S7-1 (g)(b), S5 trace hollow-vault k10 seed 7, seat 2): a bot healer pulled onto by an engaged
/// pack must react within 20 ticks. The S5 trace shows seat 2 taking melee from six cantor-vigil members with no command
/// from T1810 to T1850 and dying at T1853. The reaction the G2.1 toolkit requires is one of: a self-targeted heal, a
/// defensive, or a move toward the tank. Here the healer is the HolderId of a living engaged enemy in melee range and is
/// GCD-locked for the first 15 ticks, so no heal is ready at the start of the window.
/// </summary>
public sealed class HealerAggroResponseTests
{
    /// <summary>The observable's reaction window, in ticks.</summary>
    public const int ReactWithinTicks = 20;

    private const int Warden = 1;
    private const int Chanter = 2;
    private const int VigilA = 504;
    private const int Tick = 1850;

    /// <summary>The healer's GCD runs for 15 ticks from the start of the window.</summary>
    private const int GcdReadyTick = Tick + 15;

    private static ActorState Healer() =>
        Actor(id: Chanter, classId: "chanter", role: ActorRole.Healer, x: 11508, z: -2812,
            health: 68, maxHealth: 250, resource: 1, maxResource: 1, gcdReadyTick: GcdReadyTick);

    private static ActorState Tank() =>
        Actor(id: Warden, classId: "warden", role: ActorRole.Tank, x: 11368, z: -1964,
            health: 94, maxHealth: 400);

    /// <summary>One living melee enemy within 5 m, held by the healer seat (HolderId == Chanter).</summary>
    private static ActorState HeldMelee()
    {
        var enemy = Actor(id: VigilA, kind: ActorKind.Enemy, classId: "choir.melee", x: 12909, z: -3819,
            health: 300, maxHealth: 300);
        enemy.HolderId = Chanter;
        return enemy;
    }

    /// <summary>True when <paramref name="dir"/> reduces the distance from <paramref name="self"/> to <paramref name="tank"/>.</summary>
    private static bool ClosesOn(Dir2 dir, ActorState self, ActorState tank)
    {
        var toTankX = tank.Pos.X - self.Pos.X;
        var toTankZ = tank.Pos.Z - self.Pos.Z;
        return dir.X * toTankX + dir.Z * toTankZ > 0;
    }

    [Fact(DisplayName = "Healer responds to aggro > a Chanter held by a living melee enemy issues a self-heal, a defensive or a move toward the tank within 20 ticks")]
    public void HealerHeldByMeleeRespondsWithin20Ticks()
    {
        var reacted = false;
        for (var tick = Tick; tick < Tick + ReactWithinTicks && !reacted; tick++)
        {
            var healer = Healer();
            var tank = Tank();
            var enemy = HeldMelee();
            var view = ViewOf(tick, RealKits, null, healer, tank, enemy);
            var kit = RoleShared.KitOf(view, healer.ClassId);

            var cmds = HealerRole.HealerDecide(view, healer);
            reacted = cmds.Any(c =>
                (c is CastCommand cast && cast.ActorId == Chanter
                    && ((cast.AbilityId == kit.Heals.SelfHealAbilityId && cast.TargetId == Chanter)
                        || cast.AbilityId == kit.DefensiveAbilityId))
                || (c is MoveCommand move && move.ActorId == Chanter && ClosesOn(move.Dir, healer, tank)));
        }

        Assert.True(reacted, "bot healer held aggro for 20 ticks without a self-heal, defensive or move-to-tank command");
    }
}
