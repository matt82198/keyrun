using System;
using System.Linq;
using Keyrun.Content;
using Keyrun.Host;
using Xunit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// PURPOSE: this artifact exists to make a stun stop every cast of a stunnable actor in the keyrun sim (finding
/// f-c63b43bf, ruling: defer to WoW), with the uninterruptible flag meaning kick-immune only and boss stun-immunity a
/// separate flag; status: real.
/// The REAL host setup (game/src/Host/HostRunSetup.cs over <see cref="ContentLoader.Load()"/>) hands the sim the stun
/// rule: <c>stunned</c> is a stop and a stun (it stops every cast of a stunnable actor), <c>silenced</c> is a stop but
/// not a stun, and the stun-immune classes are run.json's stunImmuneEnemies, which name the boss actor's class.
/// </summary>
public sealed class HostStunWiringTests
{
    private static readonly Lazy<ContentRegistry> Registry = new(() => ContentLoader.Load());

    private static HostRunSetup Setup() => HostRunSetup.Build(Registry.Value, "hollow-vault", 2, "warden");

    [Fact(DisplayName = "Host run: stunned is a stun; silenced stops only interruptible casts")]
    public void Host_marks_stunned_as_the_stun()
    {
        var content = Setup().Content;
        Assert.Equal(new[] { "stunned", "silenced" }, content.StopEffectIds);
        Assert.Equal(new[] { "stunned" }, content.StunEffectIds);
    }

    [Fact(DisplayName = "Host run: the boss actor's class is the stun-immune class from run.json")]
    public void Host_makes_the_boss_stun_immune()
    {
        var setup = Setup();
        Assert.Equal(Registry.Value.Run.StunImmuneEnemies.ToArray(), setup.Content.StunImmuneClassIds);
        var bossSpawn = setup.Dungeon.Pulls.SelectMany(p => p.Spawns).Single(s => s.ActorId == HostRunSetup.BossActorId);
        Assert.Contains(bossSpawn.ClassId, setup.Content.StunImmuneClassIds!);
        Assert.All(setup.Boss.Twins, twin => Assert.DoesNotContain(twin.ClassId, setup.Content.StunImmuneClassIds!));
    }
}
