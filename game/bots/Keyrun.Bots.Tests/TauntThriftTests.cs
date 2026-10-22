using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Roles;
using Keyrun.Sim.Core;
using Xunit;
using static Keyrun.Bots.Tests.RolesTestKit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// R-TAUNT-4 (event seq 280, finding f-dc7eff7d): the bot tank's taunt selector must not waste the 160-tick taunt on an
/// enemy that dies inside the cooldown window (rule a), and on TankEngage the first taunt goes to the enemy aimed at the
/// lowest-HP non-tank seat (rule c). Rule b (never taunt when every live enemy already targets the tank) is already
/// enforced by PickLooseEnemy, which only yields enemies aimed at another player, so it has no test here.
/// </summary>
public sealed class TauntThriftTests
{
    private static ActorState Warden() =>
        Actor(id: 2, classId: "warden", role: ActorRole.Tank, health: 400, maxHealth: 400, maxResource: 100);

    private static ActorState Seat(int id, long z, long health) =>
        Actor(id: id, classId: "cinder", role: ActorRole.Damage, health: health, maxHealth: health, maxResource: 100, z: z);

    private static ActorState Enemy(int id, long z, int targetId, long health) =>
        Actor(id: id, kind: ActorKind.Enemy, classId: "choir.melee", role: ActorRole.None, z: z, health: health, maxHealth: health,
            maxResource: 100, targetId: targetId);

    private static List<Command> Decide(params ActorState[] actors)
    {
        var tank = actors[0];
        return TankRole.TankDecide(ViewOf(10, RealKits, null, actors), tank).ToList();
    }

    [Fact(DisplayName = "Warden taunt thrift R-TAUNT-4a > does not taunt an enemy whose remaining HP is below the party's committed damage over the taunt window")]
    public void NoTauntOnEnemyThatDiesInWindow()
    {
        // One enemy with 1 HP aimed at the Cinder seat: the party's committed damage over the 160-tick window is far above 1.
        var cmds = Decide(Warden(), Seat(3, 4000, 250), Enemy(100, 2000, targetId: 3, health: 1));
        Assert.DoesNotContain(new CastCommand(10, 2, "warden.taunt", 100), cmds);
    }

    [Fact(DisplayName = "Warden taunt thrift R-TAUNT-4a > still taunts a healthy loose enemy inside taunt range (control)")]
    public void TauntsHealthyLooseEnemy()
    {
        var cmds = Decide(Warden(), Seat(3, 4000, 250), Enemy(100, 2000, targetId: 3, health: 6720));
        Assert.Contains(new CastCommand(10, 2, "warden.taunt", 100), cmds);
    }

    [Fact(DisplayName = "Warden taunt thrift R-TAUNT-4c > the first taunt goes to the enemy aimed at the lowest-HP non-tank seat")]
    public void FirstTauntGoesToLowestHpSeat()
    {
        // Enemy 100 is nearer the tank and hurts the 250-HP seat; enemy 101 hurts the 100-HP seat. The lowest-HP seat wins.
        var cmds = Decide(
            Warden(),
            Seat(3, 4000, 250),
            Seat(4, 5000, 100),
            Enemy(100, 1000, targetId: 3, health: 6720),
            Enemy(101, 2500, targetId: 4, health: 6720));
        Assert.Contains(new CastCommand(10, 2, "warden.taunt", 101), cmds);
        Assert.DoesNotContain(new CastCommand(10, 2, "warden.taunt", 100), cmds);
    }
}
