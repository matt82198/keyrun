using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Roles;
using Keyrun.Sim.Core;
using Xunit;
using static Keyrun.Bots.Tests.RolesTestKit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// WoW M+ threat pickup (S1 spec, ruling event seq 246 R-BOSS-6 amendment (q), PURPOSE: pin that a bot tank whose taunt is on
/// cooldown picks up a loose mob hitting a non-tank by damage threat). A mob holder-locked on a party seat (HolderId = the
/// Cinder, not the Warden) is loose for the Warden: it is not held and it is aimed at another player. With the taunt on
/// cooldown the Warden must still engage it by damage (target it and press the rotation). The control case takes the same
/// board with the taunt ready and pins the existing taunt path, so the test is not vacuous.
/// </summary>
public sealed class BotTankThreatPickupTests
{
    private const int Tick = 10;

    // The taunt is ready at tick 170, so at Tick 10 it is on cooldown.
    private const int TauntCooldownReadyTick = 170;

    private static ActorState Warden(int tauntReadyTick) =>
        Actor(
            id: 2,
            classId: "warden",
            role: ActorRole.Tank,
            health: 400,
            maxHealth: 400,
            maxResource: 100,
            cooldowns: new[] { ("warden.taunt", tauntReadyTick) });

    // The party seat the mob is holder-locked on: a Cinder hitting the mob from range.
    private static ActorState Cinder() =>
        Actor(id: 3, classId: "cinder", role: ActorRole.Damage, z: 4000, health: 250, maxHealth: 250, maxResource: 100, targetId: 100);

    // A mob holder-locked on the Cinder (seat 3), never on the tank. Its threat table is empty, so only the lock decides.
    private static ActorState LockedMob(long z)
    {
        var mob = Actor(
            id: 100,
            kind: ActorKind.Enemy,
            classId: "choir.melee",
            role: ActorRole.None,
            z: z,
            health: 6720,
            maxHealth: 6720,
            maxResource: 100,
            targetId: 3);
        mob.HolderId = 3;
        return mob;
    }

    private static List<Command> Decide(int tauntReadyTick, long mobZ = 1000) =>
        TankRole.TankDecide(ViewOf(Tick, RealKits, null, Warden(tauntReadyTick), Cinder(), LockedMob(mobZ)), Warden(tauntReadyTick)).ToList();

    [Fact(DisplayName = "Bot tank threat pickup > taunt on cooldown: the Warden engages the loose mob by damage at its decision tick")]
    public void TauntOnCooldownEngagesLooseMobByDamage()
    {
        var cmds = Decide(tauntReadyTick: TauntCooldownReadyTick);

        // Engaging mob 100 means a target command or a rotation cast aimed at it.
        var engagesMob = cmds.Any(c => c is TargetCommand t && t.TargetId == 100)
            || cmds.Any(c => c is CastCommand k && k.TargetId == 100);
        Assert.True(engagesMob, "expected the Warden to target or cast at loose mob 100 with taunt on cooldown; got: "
            + string.Join(", ", cmds.Select(c => c.ToString())));
    }

    [Fact(DisplayName = "Bot tank threat pickup > control: taunt ready, the existing taunt path picks up the same loose mob")]
    public void TauntReadyUsesExistingTauntPath()
    {
        var cmds = Decide(tauntReadyTick: 0);
        Assert.Contains(new CastCommand(Tick, 2, "warden.taunt", 100), cmds);
    }
}
