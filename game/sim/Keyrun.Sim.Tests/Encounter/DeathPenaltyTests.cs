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
/// killing two players costs two deaths), and the run counts its deaths for the timer readout.
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
    public void Two_players_dying_on_the_same_tick_cost_two_deaths_of_Slack_and_count_two()
    {
        var encounter = EncounterTestKit.BaseEncounter(slack: Timer.InitialSlackTicks(10), par: 10000, keyLevel: 10);
        var events = new List<SimEvent> { Death(5, 1), Death(5, 2) };

        var next = RunWiring.ApplyDeathsAndWipe(Party(1, 2), encounter, events, 5, _ => { });

        Assert.Equal(780 - (2 * Timer.DeathSlackCostTicks), next.SlackTicks);
        Assert.Equal(2, next.Deaths);
        Assert.Equal(2 * Timer.DeathSlackCostTicks, Timer.DeathPenaltyTicks(next));
    }

    [Fact]
    public void An_enemy_death_costs_nothing_and_a_repeated_death_event_for_one_player_counts_once()
    {
        var encounter = EncounterTestKit.BaseEncounter(slack: Timer.InitialSlackTicks(10), par: 10000, keyLevel: 10);
        var events = new List<SimEvent> { Death(5, 99), Death(5, 3), Death(5, 3) };

        var next = RunWiring.ApplyDeathsAndWipe(Party(3), encounter, events, 5, _ => { });

        Assert.Equal(780 - Timer.DeathSlackCostTicks, next.SlackTicks);
        Assert.Equal(1, next.Deaths);
    }

    [Fact]
    public void Deaths_accumulate_across_ticks_and_a_wipe_still_costs_its_own_bank_on_top()
    {
        var encounter = EncounterTestKit.BaseEncounter(slack: 5000, par: 10000, keyLevel: 10);
        encounter = RunWiring.ApplyDeathsAndWipe(Party(1), encounter, new List<SimEvent> { Death(5, 1) }, 5, _ => { });
        encounter = RunWiring.ApplyDeathsAndWipe(Party(1, 2, 3), encounter, new List<SimEvent> { Death(9, 2), Death(9, 3) }, 9, _ => { });

        Assert.Equal(3, encounter.Deaths);
        Assert.Equal(5000 - (3 * Timer.DeathSlackCostTicks) - Timer.WipeSlackCostTicks, encounter.SlackTicks);
    }
}
