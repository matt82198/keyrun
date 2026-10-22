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

    // CHASE-HOLDER (f-2f6982e9, PR #601): FixupTargeting parks every untargeted enemy's TargetId on the tank, so a body-pulled
    // enemy is chasing its holder while TargetId still names the tank. The loose-enemy test must read the holder.
    private static System.Collections.Generic.SortedDictionary<int, ActorState> Party(ActorState enemy)
    {
        var tank = Actors.Create(1, ActorKind.Player, "warden", ActorRole.Tank, new Vec3(0, 0, 0), 400, 0);
        var dps = Actors.Create(3, ActorKind.Player, "cinder", ActorRole.Damage, new Vec3(2400, 0, 0), 250, 0);
        return new System.Collections.Generic.SortedDictionary<int, ActorState> { [1] = tank, [3] = dps, [enemy.Id] = enemy };
    }

    [Fact]
    public void An_enemy_locked_on_a_DPS_is_loose_even_while_its_TargetId_still_names_the_tank()
    {
        var enemy = Enemy(3);
        enemy.TargetId = 1;
        var actors = Party(enemy);
        Assert.Same(enemy, TankEngage.PickLooseEnemy(actors, actors[1]));
    }

    [Fact]
    public void An_enemy_the_tank_holds_is_not_loose_whoever_its_TargetId_names()
    {
        var enemy = Enemy(1);
        enemy.TargetId = 3;
        var actors = Party(enemy);
        Assert.Null(TankEngage.PickLooseEnemy(actors, actors[1]));
    }

    [Fact]
    public void With_no_holder_the_threat_table_still_decides()
    {
        Assert.True(TankEngage.HoldsThreat(Enemy(null, (1, 50L), (3, 10L)), 1));
        Assert.False(TankEngage.HoldsThreat(Enemy(null, (1, 10L), (3, 50L)), 1));
    }
}
