using System.Collections.Generic;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Xunit;

namespace Keyrun.Sim.Tests.Encounter;

/// <summary>tests/sim/encounter/timer.test.ts (death and wipe time on the run clock, finish result against par; D35).</summary>
public sealed class TimerTests
{
    private static EncounterState EncounterAtKey(int keyLevel) =>
        EncounterTestKit.BaseEncounter(slack: Timer.InitialSlackTicks(keyLevel), par: 10000, keyLevel: keyLevel);

    [Fact]
    public void Key_10_starts_with_the_documented_bank_base_plus_per_key_times_9()
    {
        Assert.Equal(Timer.SlackBaseTicks + (Timer.SlackPerKeyTicks * 9), Timer.InitialSlackTicks(10));
        Assert.Equal(780, Timer.InitialSlackTicks(10));
    }

    [Fact]
    public void Two_deaths_at_key_10_add_their_cost_to_the_clock_and_leave_the_bank_untouched()
    {
        var encounter = EncounterAtKey(10);
        encounter = Timer.ChargeDeath(encounter);
        encounter = Timer.ChargeDeath(encounter);
        Assert.Equal(2 * Timer.DeathSlackCostTicks, encounter.PenaltyTicks);
        Assert.Equal(780, encounter.SlackTicks);
        Assert.False(encounter.Depleted);
    }

    [Fact]
    public void A_third_death_past_the_bank_size_still_only_adds_time_and_never_depletes()
    {
        // D35: the key-10 bank is 780 and three deaths cost 900, which used to deplete the key on its own.
        var encounter = EncounterAtKey(10);
        encounter = Timer.ChargeDeath(encounter);
        encounter = Timer.ChargeDeath(encounter);
        encounter = Timer.ChargeDeath(encounter);
        Assert.Equal(900, encounter.PenaltyTicks);
        Assert.Equal(3, encounter.Deaths);
        Assert.Equal(780, encounter.SlackTicks);
        Assert.False(encounter.Depleted);
    }

    [Fact]
    public void A_wipe_adds_the_documented_cost_to_the_clock()
    {
        var encounter = Timer.ChargeWipe(EncounterAtKey(10));
        Assert.Equal(Timer.WipeSlackCostTicks, encounter.PenaltyTicks);
        Assert.Equal(500, encounter.PenaltyTicks);
        Assert.Equal(0, encounter.Deaths);
    }

    [Fact]
    public void Finish_reports_depleted_once_the_clock_with_death_time_passes_par()
    {
        var events = new List<SimEvent>();
        var encounter = EncounterAtKey(10);
        encounter = Timer.ChargeDeath(encounter);
        encounter = Timer.ChargeDeath(encounter);
        encounter = Timer.ChargeDeath(encounter);
        Assert.Equal(RunResult.Timed, Timer.FinishRun(encounter, 100, completed: true, events.Add));
        var result = Timer.FinishRun(encounter, 10000 - 900 + 1, completed: true, events.Add);
        Assert.Equal(RunResult.Depleted, result);
        Assert.Equal(EncounterTestKit.Ev("{\"tick\":9101,\"type\":\"finish\",\"result\":\"depleted\"}"), events[^1].ToCanonicalJson());
    }

    [Fact]
    public void Finish_reports_abandoned_when_the_run_was_not_completed_regardless_of_Slack()
    {
        var events = new List<SimEvent>();
        var result = Timer.FinishRun(EncounterAtKey(10), 100, completed: false, events.Add);
        Assert.Equal(RunResult.Abandoned, result);
        Assert.Equal(new[] { EncounterTestKit.Ev("{\"tick\":100,\"type\":\"finish\",\"result\":\"abandoned\"}") }, EncounterTestKit.Json(events));
    }

    [Fact]
    public void Finish_reports_timed_on_a_completed_run_with_Slack_remaining()
    {
        Assert.Equal(RunResult.Timed, Timer.FinishRun(EncounterAtKey(10), 100, completed: true, _ => { }));
        Assert.Equal("timed", RunResult.Timed.ToJs());
    }

    [Fact]
    public void Over_par_is_strictly_greater_than_par()
    {
        var encounter = EncounterAtKey(2);
        Assert.False(Timer.IsOverPar(10000, encounter));
        Assert.True(Timer.IsOverPar(10001, encounter));
    }
}
