using System.Collections.Generic;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Xunit;

namespace Keyrun.Sim.Tests.Encounter;

/// <summary>
/// FORCES-ROUTE-PLAN M1 TIMER-TIERS (BIBLE s8) under STATE.md D35: a run is timed when its clock (elapsed plus death and
/// wipe time) finishes no later than par, and a timed run upgrades the key by +1, +2 within 80% of par, and +3 within 60% of par. Untimed keeps the key.
/// </summary>
public sealed class UpgradeTierTests
{
    private const int Par = 10000;

    private static EncounterState AtKey(int keyLevel) =>
        EncounterTestKit.BaseEncounter(slack: Timer.InitialSlackTicks(keyLevel), par: Par, keyLevel: keyLevel);

    [Fact]
    public void A_clean_run_finishing_one_tick_past_par_is_depleted()
    {
        var encounter = AtKey(10);
        var deadline = Par;
        Assert.Equal(RunResult.Timed, Timer.FinishRun(encounter, deadline, completed: true, _ => { }));
        var events = new List<SimEvent>();
        Assert.Equal(RunResult.Depleted, Timer.FinishRun(encounter, deadline + 1, completed: true, events.Add));
        Assert.Equal(
            EncounterTestKit.Ev("{\"tick\":" + (deadline + 1) + ",\"type\":\"finish\",\"result\":\"depleted\"}"),
            events[^1].ToCanonicalJson());
    }

    [Fact]
    public void A_death_pulls_the_deadline_in_by_its_cost_so_one_death_at_par_less_15s_is_still_timed()
    {
        var encounter = Timer.ChargeDeath(AtKey(2));
        Assert.Equal(Par - Timer.DeathSlackCostTicks, Timer.DeadlineTicks(encounter));
        Assert.Equal(RunResult.Timed, Timer.FinishRun(encounter, Par - Timer.DeathSlackCostTicks, completed: true, _ => { }));
        Assert.Equal(RunResult.Depleted, Timer.FinishRun(encounter, (int)Timer.DeadlineTicks(encounter) + 1, completed: true, _ => { }));
    }

    [Fact]
    public void Finishes_at_exactly_6000_8000_and_10000_bp_of_par_upgrade_by_3_2_and_1()
    {
        Assert.Equal(3, Timer.UpgradeTier(RunResult.Timed, 6000, Par));
        Assert.Equal(2, Timer.UpgradeTier(RunResult.Timed, 6001, Par));
        Assert.Equal(2, Timer.UpgradeTier(RunResult.Timed, 8000, Par));
        Assert.Equal(1, Timer.UpgradeTier(RunResult.Timed, 8001, Par));
        Assert.Equal(1, Timer.UpgradeTier(RunResult.Timed, 10000, Par));
        Assert.Equal(1, Timer.UpgradeTier(RunResult.Timed, 10500, Par));
    }

    [Fact]
    public void A_depleted_or_abandoned_run_does_not_upgrade_and_the_key_stays_at_its_level()
    {
        Assert.Equal(0, Timer.UpgradeTier(RunResult.Depleted, 5000, Par));
        Assert.Equal(0, Timer.UpgradeTier(RunResult.Abandoned, 5000, Par));
        Assert.Equal(7, Timer.NextKeyLevel(7, Timer.UpgradeTier(RunResult.Depleted, 5000, Par)));
        Assert.Equal(10, Timer.NextKeyLevel(7, Timer.UpgradeTier(RunResult.Timed, 5000, Par)));
        Assert.Equal(8, Timer.NextKeyLevel(7, Timer.UpgradeTier(RunResult.Timed, 9000, Par)));
    }

    [Fact]
    public void A_wipe_forced_depletion_stays_depleted_even_inside_par()
    {
        var encounter = AtKey(5) with { Depleted = true };
        Assert.Equal(RunResult.Depleted, Timer.FinishRun(encounter, 100, completed: true, _ => { }));
    }
}
