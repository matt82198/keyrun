using System.Collections.Generic;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Xunit;

namespace Keyrun.Sim.Tests.Encounter;

/// <summary>tests/sim/encounter/timer.test.ts ported 1:1 (Slack bank, depletion, finish result).</summary>
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
    public void Two_deaths_at_key_10_leave_exactly_the_expected_bank_and_do_not_deplete_it()
    {
        var encounter = EncounterAtKey(10);
        var emitted = new List<SimEvent>();
        encounter = Timer.SpendSlackOnDeath(encounter, 1, emitted.Add);
        encounter = Timer.SpendSlackOnDeath(encounter, 2, emitted.Add);
        Assert.Equal(780 - (2 * Timer.DeathSlackCostTicks), encounter.SlackTicks);
        Assert.Equal(180, encounter.SlackTicks);
        Assert.False(encounter.Depleted);
        Assert.Empty(emitted);
    }

    [Fact]
    public void A_third_death_depletes_the_bank_clamps_at_0_and_emits_depleted_exactly_once()
    {
        var encounter = EncounterAtKey(10);
        var emitted = new List<SimEvent>();
        encounter = Timer.SpendSlackOnDeath(encounter, 1, emitted.Add);
        encounter = Timer.SpendSlackOnDeath(encounter, 2, emitted.Add);
        encounter = Timer.SpendSlackOnDeath(encounter, 3, emitted.Add);
        Assert.Equal(0, encounter.SlackTicks);
        Assert.True(encounter.Depleted);
        Assert.Equal(new[] { EncounterTestKit.Ev("{\"tick\":3,\"type\":\"depleted\"}") }, EncounterTestKit.Json(emitted));

        encounter = Timer.SpendSlackOnDeath(encounter, 4, emitted.Add);
        Assert.Equal(0, encounter.SlackTicks);
        Assert.Single(emitted);
    }

    [Fact]
    public void A_wipe_costs_the_documented_bank()
    {
        var encounter = Timer.SpendSlackOnWipe(EncounterAtKey(10), 5, _ => { });
        Assert.Equal(780 - Timer.WipeSlackCostTicks, encounter.SlackTicks);
        Assert.Equal(280, encounter.SlackTicks);
    }

    [Fact]
    public void Finish_reports_depleted_while_still_allowing_the_run_to_complete()
    {
        var events = new List<SimEvent>();
        var encounter = EncounterAtKey(10);
        encounter = Timer.SpendSlackOnDeath(encounter, 1, events.Add);
        encounter = Timer.SpendSlackOnDeath(encounter, 2, events.Add);
        encounter = Timer.SpendSlackOnDeath(encounter, 3, events.Add);
        var result = Timer.FinishRun(encounter, 100, completed: true, events.Add);
        Assert.Equal(RunResult.Depleted, result);
        Assert.Equal(EncounterTestKit.Ev("{\"tick\":100,\"type\":\"finish\",\"result\":\"depleted\"}"), events[^1].ToCanonicalJson());
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
