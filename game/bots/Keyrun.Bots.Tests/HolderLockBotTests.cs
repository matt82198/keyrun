using Keyrun.Bots.Roles;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Xunit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// HOLDER-LOCK (STATE.md D30): the bot tank reads an enemy's locked holder, not the threat table, to decide whether it
/// holds the enemy. Without this a tank that tops the table (Momentum threat) would never taunt an enemy that a DPS
/// body-pulled, and the enemy would stay on the DPS for the whole pull.
/// </summary>
public sealed class HolderLockBotTests
{
    private static ActorState Enemy(int? holderId, params (int Id, long Val)[] threat)
    {
        var e = Actors.Create(100, ActorKind.Enemy, "test.brute", ActorRole.None, new Vec3(0, 0, 0), 1000, 0);
        e.HolderId = holderId;
        foreach (var (id, v) in threat)
        {
            e.Threat[id] = v;
        }
        return e;
    }

    [Fact]
    public void A_tank_topping_the_threat_table_does_not_hold_an_enemy_locked_on_someone_else()
    {
        Assert.False(TankEngage.HoldsThreat(Enemy(3, (1, 500L), (3, 10L)), 1));
    }

    [Fact]
    public void The_holder_holds_the_enemy_whatever_the_threat_table_says()
    {
        Assert.True(TankEngage.HoldsThreat(Enemy(1, (3, 5000L)), 1));
    }

    [Fact]
    public void With_no_holder_the_threat_table_still_decides()
    {
        Assert.True(TankEngage.HoldsThreat(Enemy(null, (1, 50L), (3, 10L)), 1));
        Assert.False(TankEngage.HoldsThreat(Enemy(null, (1, 10L), (3, 50L)), 1));
    }
}
