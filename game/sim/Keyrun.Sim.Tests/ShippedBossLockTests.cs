using System.Linq;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Xunit;
using static Keyrun.Sim.Tests.BossTestKit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// The Lock-at-50% and enrage checks of <see cref="BossRulesTests"/>, run against the boss at the shipped content's
/// max health (<see cref="BossTestKit.BuildShippedRun"/>: the k2 oracle setup's Tempo-scaled hollowWarden.boss, content
/// maxHealth 5000) instead of the kit's 60000. The kit's own tests cannot tell a threshold read from the boss's
/// MaxHealth from one read off a hard-coded 60000, because there the two are the same number; here they are not.
/// </summary>
public sealed class ShippedBossLockTests
{
    /// <summary>The shipped max health: far below the kit's 60000, so the two thresholds disagree.</summary>
    private static readonly long Max = ShippedBossMaxHealth();

    /// <summary>The highest health at or below 50% of <see cref="Max"/> (health * 10000 &lt;= max * 5000).</summary>
    private static readonly long Half = Max / 2;

    [Fact(DisplayName = "Shipped boss: the run's boss spawns at the shipped content's max health, not the kit's 60000")]
    public void Shipped_run_boss_has_the_shipped_max_health()
    {
        var run = BuildShippedRun();

        Assert.Equal(Max, run.State.Actors[Boss].MaxHealth);
        Assert.Equal(Max, run.State.Actors[Boss].Health);
        Assert.True(Max * 2 < 60000, $"shipped boss max health {Max} is not well below the kit's 60000; this suite would not discriminate");
    }

    [Fact(DisplayName = "Shipped boss: one hp above 50% of the shipped max the Lock never opens and the boss never enrages")]
    public void Lock_stays_shut_one_hp_above_half_of_the_shipped_max()
    {
        var (run, events) = StepUntil(BuildShippedRun(bossHealth: Half + 1), 1900);

        Assert.Equal(Half + 1, run.State.Actors[Boss].Health);
        Assert.DoesNotContain(events, e => e.Type == "lock_open");
        Assert.DoesNotContain(events, e => e.Type == "boss_enrage");
        Assert.DoesNotContain(run.State.Actors[Boss].Effects, e => e.EffectId == "lockShield");
        Assert.False(run.Encounter.Boss!.Enraged);
    }

    [Fact(DisplayName = "Shipped boss: at 50% of the shipped max the Lock opens; three unbroken Locks enrage once")]
    public void Lock_opens_at_half_of_the_shipped_max_and_the_third_fail_enrages()
    {
        var (run, _) = StepUntil(BuildShippedRun(bossHealth: Half + 1), 20);
        run.State.Actors[Boss].Health = Half;
        var (after, events) = StepUntil(run, 1900);

        Assert.Equal(new[] { 20, 620, 1220, 1820 }, events.OfType("lock_open").Select(e => e.Tick).ToArray());
        Assert.Equal(Boss, events.OfType("lock_open").First().IntField("actorId"));
        var enrage = Assert.Single(events, e => e.Type == "boss_enrage");
        Assert.Equal(1280, enrage.Tick);
        Assert.Equal(15000, enrage.IntField("tempoBp"));
        Assert.True(after.Encounter.Boss!.Enraged);
    }
}
