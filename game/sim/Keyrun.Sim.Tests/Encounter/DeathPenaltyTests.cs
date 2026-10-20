using System.Collections.Generic;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Sim.Tests.Encounter;

/// <summary>
/// MPLUS-DEATH-PENALTY (Matt 2026-10-20: "lean into m+ design heavily", BIBLE s8): as on a Mythic+ timer, every player
/// death costs its own <see cref="Timer.DeathSlackCostTicks"/>, including deaths that land on the same tick (one slam
/// killing two players costs two deaths), and the run counts its deaths for the timer readout. STATE.md D35 (Matt
/// 2026-10-20 "almost exact to wow"): deaths only add time to the run clock; a key is depleted only when elapsed plus
/// that time passes par, and a Clause's death cost (Clauses.cs:60) replaces the 15 s constant when set.
/// </summary>
public sealed class DeathPenaltyTests
{
    private static SimEvent Death(int tick, int actorId) =>
        new(tick, "death", ("actorId", JsValue.Int(actorId)), ("sourceId", JsValue.Int(99)));

    private static SimState Party(params int[] deadIds)
    {
        var state = CombatTestKit.State(
            CombatTestKit.Actor(1, ActorKind.Player),
            CombatTestKit.Actor(2, ActorKind.Player),
            CombatTestKit.Actor(3, ActorKind.Player),
            CombatTestKit.Actor(99, ActorKind.Enemy));
        foreach (var id in deadIds)
        {
            state.Actors[id].Alive = false;
        }
        return state;
    }

    [Fact]
    public void Two_players_dying_on_the_same_tick_add_two_deaths_to_the_clock_and_count_two()
    {
        var encounter = EncounterTestKit.BaseEncounter(slack: Timer.InitialSlackTicks(10), par: 10000, keyLevel: 10);
        var events = new List<SimEvent> { Death(5, 1), Death(5, 2) };

        var next = RunWiring.ApplyDeathsAndWipe(Party(1, 2), encounter, events, 5, _ => { });

        Assert.Equal(2 * Timer.DeathSlackCostTicks, next.PenaltyTicks);
        Assert.Equal(2, next.Deaths);
        Assert.Equal(2 * Timer.DeathSlackCostTicks, Timer.DeathPenaltyTicks(next));
    }

    [Fact]
    public void An_enemy_death_costs_nothing_and_a_repeated_death_event_for_one_player_counts_once()
    {
        var encounter = EncounterTestKit.BaseEncounter(slack: Timer.InitialSlackTicks(10), par: 10000, keyLevel: 10);
        var events = new List<SimEvent> { Death(5, 99), Death(5, 3), Death(5, 3) };

        var next = RunWiring.ApplyDeathsAndWipe(Party(3), encounter, events, 5, _ => { });

        Assert.Equal(Timer.DeathSlackCostTicks, next.PenaltyTicks);
        Assert.Equal(1, next.Deaths);
    }

    [Fact]
    public void Deaths_accumulate_across_ticks_and_a_wipe_still_adds_its_own_cost_on_top()
    {
        var encounter = EncounterTestKit.BaseEncounter(slack: 5000, par: 10000, keyLevel: 10);
        encounter = RunWiring.ApplyDeathsAndWipe(Party(1), encounter, new List<SimEvent> { Death(5, 1) }, 5, _ => { });
        encounter = RunWiring.ApplyDeathsAndWipe(Party(1, 2, 3), encounter, new List<SimEvent> { Death(9, 2), Death(9, 3) }, 9, _ => { });

        Assert.Equal(3, encounter.Deaths);
        Assert.Equal((3 * Timer.DeathSlackCostTicks) + Timer.WipeSlackCostTicks, encounter.PenaltyTicks);
    }

    private static SimState FourPlayers(params int[] deadIds)
    {
        var state = Party(deadIds);
        state.Actors[4] = CombatTestKit.Actor(4, ActorKind.Player);
        return state;
    }

    private static EncounterState ThreeDeathsAtKey10(EncounterState encounter, List<SimEvent> emitted)
    {
        encounter = RunWiring.ApplyDeathsAndWipe(FourPlayers(1), encounter, new List<SimEvent> { Death(5, 1) }, 5, emitted.Add);
        encounter = RunWiring.ApplyDeathsAndWipe(FourPlayers(1, 2), encounter, new List<SimEvent> { Death(9, 2) }, 9, emitted.Add);
        return RunWiring.ApplyDeathsAndWipe(FourPlayers(1, 2, 3), encounter, new List<SimEvent> { Death(12, 3) }, 12, emitted.Add);
    }

    [Fact]
    public void Three_deaths_under_par_at_key_10_stay_timed_when_elapsed_plus_45s_is_within_par()
    {
        // Mythic+ (Matt 2026-10-20 "almost exact to wow"): deaths only add time to the clock. 3 x 15 s = 900 ticks is
        // more than the 780-tick key-10 Slack bank, which used to deplete the key on its own.
        var emitted = new List<SimEvent>();
        var encounter = ThreeDeathsAtKey10(EncounterTestKit.BaseEncounter(slack: Timer.InitialSlackTicks(10), par: 10000, keyLevel: 10), emitted);

        Assert.Equal(3, encounter.Deaths);
        Assert.Equal(3 * Timer.DeathSlackCostTicks, Timer.DeathPenaltyTicks(encounter));
        Assert.False(encounter.Depleted);
        Assert.DoesNotContain(emitted, e => e.Type == "depleted");
        Assert.Equal(RunResult.Timed, Timer.FinishRun(encounter, 10000 - 900, completed: true, _ => { }));
        Assert.Equal(RunResult.Depleted, Timer.FinishRun(encounter, 10000 - 900 + 1, completed: true, _ => { }));
    }

    [Fact]
    public void Three_deaths_count_on_the_clock_for_the_upgrade_tier()
    {
        var encounter = ThreeDeathsAtKey10(EncounterTestKit.BaseEncounter(slack: Timer.InitialSlackTicks(10), par: 10000, keyLevel: 10), new List<SimEvent>());

        // 7100 elapsed + 900 = 8000 on the clock, exactly 80% of par: a +2. One tick later is a +1.
        Assert.Equal(2, Timer.UpgradeTier(Timer.FinishRun(encounter, 7100, true, _ => { }), Timer.ClockTicks(encounter, 7100), 10000));
        Assert.Equal(1, Timer.UpgradeTier(Timer.FinishRun(encounter, 7101, true, _ => { }), Timer.ClockTicks(encounter, 7101), 10000));
        Assert.Equal(1, KeyMove.Of(RunResult.Timed, 10, Timer.ClockTicks(encounter, 7101), 10000).Tier);
    }

    [Fact]
    public void A_Clause_death_cost_is_what_each_death_adds_to_the_clock()
    {
        var baseEncounter = EncounterTestKit.BaseEncounter(slack: Timer.InitialSlackTicks(10), par: 10000, keyLevel: 10);
        var encounter = ThreeDeathsAtKey10(baseEncounter with { Rules = baseEncounter.Rules with { DeathSlackCostTicks = 100 } }, new List<SimEvent>());

        Assert.Equal(300, Timer.DeathPenaltyTicks(encounter));
        Assert.Equal(RunResult.Timed, Timer.FinishRun(encounter, 10000 - 300, completed: true, _ => { }));
        Assert.Equal(RunResult.Depleted, Timer.FinishRun(encounter, 10000 - 300 + 1, completed: true, _ => { }));
    }

    [Fact]
    public void A_clean_run_is_timed_at_par_and_depleted_one_tick_past_it()
    {
        var encounter = EncounterTestKit.BaseEncounter(slack: Timer.InitialSlackTicks(10), par: 10000, keyLevel: 10);
        Assert.Equal(RunResult.Timed, Timer.FinishRun(encounter, 10000, completed: true, _ => { }));
        Assert.Equal(RunResult.Depleted, Timer.FinishRun(encounter, 10001, completed: true, _ => { }));
    }
}
