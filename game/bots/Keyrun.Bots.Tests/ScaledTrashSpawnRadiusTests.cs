using System.Linq;
using System.Reflection;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Core;
using Xunit;
using C = Keyrun.Content;

namespace Keyrun.Bots.Tests;

/// <summary>
/// R-BOSSROOM-1 (b3): a spawn built from an enemy definition carries that definition's collision radius, exactly as it
/// carries the speed and chase margin. <c>ScaledTrashSpawn</c> is private to <see cref="HostRunSetup"/>, so the test
/// reaches it by reflection; the expected 900 is the literal in game/content/data/enemies/hollow-warden.json.
/// </summary>
public sealed class ScaledTrashSpawnRadiusTests
{
    private const long WardenRadiusMm = 900;

    [Fact]
    public void ScaledTrashSpawn_carries_the_enemy_def_radius_onto_the_spawn()
    {
        var registry = ContentLoader.Load();
        Assert.True(registry.Enemies.TryGetValue("hollowWarden.boss", out C.EnemyDef? def));
        Assert.Equal<long?>(WardenRadiusMm, def!.RadiusMm);

        MethodInfo method = typeof(HostRunSetup).GetMethod(
            "ScaledTrashSpawn",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new Xunit.Sdk.XunitException("HostRunSetup.ScaledTrashSpawn not found");
        var spawn = (PullSpawn)method.Invoke(null, new object[] { def!, 1, Keyrun.Sim.Core.Vec3.Zero, 2, 0 })!;

        Assert.Equal<long?>(WardenRadiusMm, spawn.RadiusMm);
    }

    /// <summary>
    /// R-BOSSROOM-1 (b3, ruling 175): the boss pull's spawn built by <c>ToSimDungeon</c> (through <see cref="HostRunSetup.Build"/>)
    /// carries the Warden's radius on the boss actor (<see cref="HostRunSetup.BossActorId"/>).
    /// </summary>
    [Fact]
    public void Built_boss_PullSpawn_carries_the_hollow_warden_radius()
    {
        var registry = ContentLoader.Load();
        var setup = HostRunSetup.Build(registry, "hollow-vault", 2, "warden");

        var boss = setup.Dungeon.Pulls.SelectMany(p => p.Spawns).Single(s => s.ActorId == HostRunSetup.BossActorId);
        Assert.Equal("hollowWarden.boss", boss.ClassId);
        Assert.Equal<long?>(WardenRadiusMm, boss.RadiusMm);
    }
}
