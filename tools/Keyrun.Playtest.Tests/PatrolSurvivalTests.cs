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
            $"the Warden died at tick {wardenDeath} before the patrol was dead (last patrol death {lastPatrolDeath})");
    }

    [Fact(DisplayName = "Patrol survival (R-PATROL-3 guard, default three-seat party): the patrol engages and the Warden loses at least a quarter of its max health to it")]
    public void Default_party_engages_the_patrol_and_the_warden_loses_a_quarter_of_its_health()
    {
        var run = HostBackfillRunTests.Arena(HostBackfillRunTests.BackfillTicks, null, joinerKey: null);
        Assert.Contains(run.Events, x => x.Event.Type == "pack_engage" && x.Event.Get("packId") is JsString { Value: "corridor-patrol" });
        var taken = run.Events
            .Where(x => x.Event.Type == "damage" && ActorOf(x.Event, "targetId") == WardenId
                        && ActorOf(x.Event, "sourceId") is >= FirstPatrolActorId and <= LastPatrolActorId)
            .Sum(x => ActorOf(x.Event, "healthDamage"));
        Assert.True(taken >= 100, $"the Warden took {taken} health damage from the patrol, under a quarter of its 400 max");
    }
}
