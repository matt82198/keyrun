using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Roles;
using Keyrun.Sim.Core;
using Xunit;
using static Keyrun.Bots.Tests.RolesTestKit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// f-c0201273 (G7 R-K10S7-1 (g)(b), S5 trace hollow-vault k10 seed 7, seat 2): a bot healer holding melee aggro from an
/// engaged pack must move toward its tank. At T1850 the Chanter (seat 2) stood at 68/250 with three cantor-vigil melee
/// enemies engaged on it, and the bot issued no command until it died at T1853. Observable: a bot healer holding aggro
/// from an engaged pack issues a move toward the tank within 20 ticks.
/// </summary>
public sealed class HealerRespondsToAggroTests
{
    /// <summary>The observable's reaction window, in ticks.</summary>
    public const int ReactWithinTicks = 20;

    private const int Warden = 1;
    private const int Chanter = 2;
    private const int PackOnTank = 503;
    private const int VigilA = 504;
    private const int VigilB = 505;
    private const int VigilC = 506;
    private const int Tick = 1850;

    /// <summary>The Chanter's GCD is still running for 15 ticks from the start of the window.</summary>
    private const int GcdReadyTick = Tick + 15;

    private static ActorState Healer() =>
        Actor(id: Chanter, classId: "chanter", role: ActorRole.Healer, x: 11508, z: -2812,
            health: 68, maxHealth: 250, resource: 1, maxResource: 1, gcdReadyTick: GcdReadyTick);

    private static ActorState Tank() =>
        Actor(id: Warden, classId: "warden", role: ActorRole.Tank, x: 11368, z: -1964,
            health: 94, maxHealth: 400, targetId: PackOnTank);

    private static ActorState[] Foes() => new[]
    {
        // Three choir.melee members within melee range (5000 mm) of seat 2, all targeting it.
        Actor(id: VigilA, kind: ActorKind.Enemy, classId: "choir.melee", x: 12909, z: -3819, health: 300, maxHealth: 300, targetId: Chanter),
        Actor(id: VigilB, kind: ActorKind.Enemy, classId: "choir.melee", x: 12300, z: -2600, health: 300, maxHealth: 300, targetId: Chanter),
        Actor(id: VigilC, kind: ActorKind.Enemy, classId: "choir.melee", x: 11100, z: -3300, health: 300, maxHealth: 300, targetId: Chanter),
        // A pack member holding the tank: the tank's own engagement, not the healer's.
        Actor(id: PackOnTank, kind: ActorKind.Enemy, classId: "choir.melee", x: 12007, z: -3693, health: 300, maxHealth: 300, targetId: Warden),
    };

    /// <summary>True when <paramref name="dir"/> reduces the distance from <paramref name="self"/> to <paramref name="tank"/>.</summary>
    private static bool ClosesOn(Dir2 dir, ActorState self, ActorState tank)
    {
        var toTankX = tank.Pos.X - self.Pos.X;
        var toTankZ = tank.Pos.Z - self.Pos.Z;
        return dir.X * toTankX + dir.Z * toTankZ > 0;
    }

    [Fact(DisplayName = "Healer responds to aggro > a Chanter at 27% holding melee aggro from an engaged pack moves toward the tank within 20 ticks")]
    public void HealerUnderMeleeAggroMovesTowardTankWithin20Ticks()
    {
        var moved = false;
        for (var tick = Tick; tick < Tick + ReactWithinTicks && !moved; tick++)
        {
            var healer = Healer();
            var tank = Tank();
            var view = ViewOf(tick, RealKits, null, new[] { healer, tank }.Concat(Foes()).ToArray());

            var cmds = HealerRole.HealerDecide(view, healer);
            moved = cmds.OfType<MoveCommand>().Any(m => m.ActorId == Chanter && ClosesOn(m.Dir, healer, tank));
        }

        Assert.True(moved, "healer seat 2 under melee aggro issued no move toward the tank within 20 ticks");
    }
}
