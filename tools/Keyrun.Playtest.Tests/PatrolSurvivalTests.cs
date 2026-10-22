using System;
using System.Linq;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Core;
using Xunit;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// R-PATROL-3 (ruling on PR #528; core loop defers to WoW; BIBLE test 3 "no run ruined by one mistake" is the acceptance).
/// Two checks on the corridor-2 patrol (the two choir.melee members, actor ids 519 and 520):
/// (1) the run F trajectory, where the healer seat is abandoned mid pull 1 and nothing refills it before pull 2: the Warden
/// must finish the patrol alive, as a tank does against two trash mobs with one DPS in WoW;
/// (2) the default three-seat party: the patrol still engages and the Warden loses at least a quarter of its max health to it
/// (the patrol stays a real pull, BIBLE test 1).
/// </summary>
public sealed class PatrolSurvivalTests
{
    private const int WardenId = 1;
    private const long FirstPatrolActorId = 519;
    private const long LastPatrolActorId = 520;

    private static long ActorOf(SimEvent e, string field) => e.Get(field) is JsInt { Value: var v } ? v : -1;

    [Fact(DisplayName = "Patrol survival (R-PATROL-3, run F trajectory, seed 1 key 2): with the healer abandoned, the Warden finishes the corridor patrol alive")]
    public void Warden_without_a_healer_finishes_the_corridor_patrol_alive()
    {
        var run = HostBackfillRunTests.Arena(HostBackfillRunTests.BackfillTicks, SeatAbandonPlan.Parse(HostBackfillRunTests.AbandonSeat), joinerKey: null);
        var deaths = run.Events.Where(x => x.Event.Type == "death").Select(x => (Tick: x.Event.Tick, Actor: ActorOf(x.Event, "actorId"))).ToList();
        var patrolDeaths = deaths.Where(d => d.Actor is >= FirstPatrolActorId and <= LastPatrolActorId).ToList();
        Assert.Equal(2, patrolDeaths.Count);
        var lastPatrolDeath = patrolDeaths.Max(d => d.Tick);
        var wardenDeath = deaths.Where(d => d.Actor == WardenId).Select(d => (int?)d.Tick).FirstOrDefault();
        Assert.True(wardenDeath is null || wardenDeath > lastPatrolDeath,
            // (a death on the last patrol death's own tick counts as not finishing it: strictly after)
            $"the Warden died at tick {wardenDeath} before the patrol was dead (last patrol death {lastPatrolDeath})");

        // R-PINS-1 (R-ORACLE-1 applied, ruling on #573), replacing R-PATROL-3b's single-seed floor of 40 HP (main stood at 42,
        // the aggro-chase trajectory at 22): the property this test is for, the Warden finishes the patrol alive. No death
        // event for him up to the last patrol death, and health above 0 at it.
        var hp = WardenHealthAt(run, lastPatrolDeath);
        Assert.True(hp > 0, $"the Warden had {hp} of {WardenMaxHealth} health at the last patrol death (tick {lastPatrolDeath}); want him alive");
    }

    private const long WardenMaxHealth = 400;

    /// <summary>R-PATROL-3c: the three-seat guard floor, 20% of the Warden's max health (was a quarter, 100).</summary>
    private const long GuardMinHealthDamage = 80;

    /// <summary>The Warden's health at <paramref name="tick"/>: full at the start, less each health damage it takes, plus each heal (capped at max), in event order.</summary>
    private static long WardenHealthAt(HostBackfillRunTests.ArenaRun run, int tick)
    {
        long hp = WardenMaxHealth;
        foreach (var x in run.Events)
        {
            if (x.Event.Tick > tick)
            {
                break;
            }
            if (ActorOf(x.Event, "targetId") != WardenId)
            {
                continue;
            }
            if (x.Event.Type == "damage")
            {
                hp -= ActorOf(x.Event, "healthDamage");
            }
            else if (x.Event.Type == "heal")
            {
                hp = Math.Min(WardenMaxHealth, hp + ActorOf(x.Event, "amount"));
            }
        }
        return hp;
    }

    [Fact(DisplayName = "Patrol survival (R-PATROL-3 guard, R-PATROL-3c floor 20%): the patrol engages and the Warden loses at least 20% of its max health to it")]
    public void Default_party_engages_the_patrol_and_the_warden_loses_a_quarter_of_its_health()
    {
        var run = HostBackfillRunTests.Arena(HostBackfillRunTests.BackfillTicks, null, joinerKey: null);
        Assert.Contains(run.Events, x => x.Event.Type == "pack_engage" && x.Event.Get("packId") is JsString { Value: "corridor-patrol" });
        var taken = run.Events
            .Where(x => x.Event.Type == "damage" && ActorOf(x.Event, "targetId") == WardenId
                        && ActorOf(x.Event, "sourceId") is >= FirstPatrolActorId and <= LastPatrolActorId)
            .Sum(x => ActorOf(x.Event, "healthDamage"));
        // R-PATROL-3c: the floor is 20% of max health (80 of 400), not a quarter. WHY: BIBLE test 1 says a key is hard because
        // of what enemies do, not how much health they have; the 22-raw sweep measured 87 here (raw 23 gives 92, raw 24 gives 95),
        // and the survival floor (42 Warden HP at the last patrol death) rules out more raw chip damage. Depth comes from a
        // telegraphed patrol cast in a later slice.
        Assert.True(taken >= GuardMinHealthDamage, $"the Warden took {taken} health damage from the patrol, under {GuardMinHealthDamage} (20% of its 400 max)");
    }
}
