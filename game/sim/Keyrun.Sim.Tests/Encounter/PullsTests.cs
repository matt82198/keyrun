using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Xunit;

namespace Keyrun.Sim.Tests.Encounter;

/// <summary>tests/sim/encounter/pulls.test.ts ported 1:1, plus the spawn tuning copy from chase-tuning.test.ts.</summary>
public sealed class PullsTests
{
    private static readonly DungeonDef Dungeon = new("hollow-vault", new[]
    {
        new PullDef("choir", new[]
        {
            new PullSpawn(101, "melee_add", Vec3.Zero, 0, 500),
            new PullSpawn(102, "caster_add", new Vec3(1000, 0, 0), 0, 300),
        }),
        new PullDef("pair", new[] { new PullSpawn(201, "melee_add", new Vec3(0, 0, 5000), 0, 400) }),
    });

    private static ActorState Enemy(int id, bool alive) => EncounterTestKit.Actor(id, ActorKind.Enemy, alive: alive, health: alive ? 1 : 0);

    [Fact]
    public void Spawns_the_pulls_enemies_at_their_defined_points_and_emits_pull_start()
    {
        var emitted = new List<SimEvent>();
        var spawned = Pulls.SpawnPull(Dungeon, 0, 0, emitted.Add);
        Assert.Equal(new[] { 101, 102 }, spawned.Select(a => a.Id));
        Assert.Equal(Vec3.Zero, spawned[0].Pos);
        Assert.All(spawned, a => Assert.True(a.Alive));
        Assert.Equal(
            new[] { EncounterTestKit.Ev("{\"tick\":0,\"type\":\"pull_start\",\"pullIndex\":0,\"pullId\":\"choir\",\"actorIds\":[101,102]}") },
            EncounterTestKit.Json(emitted));
        // pulls.ts:375 spawnActor: an enemy at full health, role none, no resource, untargeted.
        var a = spawned[1];
        Assert.Equal((ActorKind.Enemy, "caster_add", ActorRole.None, 300L, 300L, 0L, 0L), (a.Kind, a.ClassId, a.Role, a.Health, a.MaxHealth, a.Resource, a.MaxResource));
        Assert.Null(a.TargetId);
        Assert.Null(a.CastId);
        Assert.Null(a.SpeedMmPerTick);
        Assert.Null(a.ChaseMarginMm);
    }

    [Fact]
    public void Returns_nothing_for_an_out_of_range_pull_index()
    {
        var emitted = new List<SimEvent>();
        Assert.Empty(Pulls.SpawnPull(Dungeon, 99, 0, emitted.Add));
        Assert.Empty(emitted);
    }

    [Fact]
    public void Does_not_advance_while_any_of_the_pulls_enemies_are_alive()
    {
        var state = EncounterTestKit.State(0, Enemy(101, true), Enemy(102, false));
        var emitted = new List<SimEvent>();
        var next = Pulls.CheckPullAdvance(state, Dungeon, EncounterTestKit.BaseEncounter(), 50, emitted.Add);
        Assert.Equal(0, next.PullIndex);
        Assert.Empty(emitted);
    }

    [Fact]
    public void Advances_and_emits_pull_clear_once_every_enemy_in_the_pull_is_dead()
    {
        var state = EncounterTestKit.State(0, Enemy(101, false), Enemy(102, false));
        var emitted = new List<SimEvent>();
        var next = Pulls.CheckPullAdvance(state, Dungeon, EncounterTestKit.BaseEncounter(), 50, emitted.Add);
        Assert.Equal(1, next.PullIndex);
        Assert.Equal(
            new[] { EncounterTestKit.Ev("{\"tick\":50,\"type\":\"pull_clear\",\"pullIndex\":0,\"pullId\":\"choir\"}") },
            EncounterTestKit.Json(emitted));
    }

    [Fact]
    public void Treats_an_enemy_missing_from_state_actors_as_dead()
    {
        var state = EncounterTestKit.State(0, Enemy(101, false));
        Assert.Equal(1, Pulls.CheckPullAdvance(state, Dungeon, EncounterTestKit.BaseEncounter(), 50, _ => { }).PullIndex);
    }

    [Fact]
    public void An_empty_or_finished_pull_never_advances()
    {
        var empty = new DungeonDef("d", new[] { new PullDef("p", System.Array.Empty<PullSpawn>()) });
        Assert.Equal(0, Pulls.CheckPullAdvance(EncounterTestKit.State(0), empty, EncounterTestKit.BaseEncounter(), 1, _ => { }).PullIndex);
        var past = EncounterTestKit.BaseEncounter() with { PullIndex = 2 };
        Assert.Equal(2, Pulls.CheckPullAdvance(EncounterTestKit.State(0), Dungeon, past, 1, _ => { }).PullIndex);
    }

    [Fact]
    public void SpawnPull_carries_speed_and_margin_onto_the_actor_only_when_present()
    {
        var d = new DungeonDef("d", new[]
        {
            new PullDef("p", new[]
            {
                new PullSpawn(1000, "fast", Vec3.Zero, 0, 10) { SpeedMmPerTick = 207, ChaseMarginMm = 300 },
                new PullSpawn(1001, "plain", Vec3.Zero, 0, 10),
            }),
        });
        var spawned = Pulls.SpawnPull(d, 0, 0, _ => { });
        Assert.Equal(207, spawned[0].SpeedMmPerTick);
        Assert.Equal(300, spawned[0].ChaseMarginMm);
        Assert.Null(spawned[1].SpeedMmPerTick);
        Assert.Null(spawned[1].ChaseMarginMm);
    }

    /// <summary>
    /// R-BOSSROOM-1 (b): the Hollow Warden's loaded content def carries radiusMm 900, and its spawned boss actor carries
    /// it as <see cref="ActorState.RadiusMm"/> while a trash actor with no radius stays null (the run default).
    /// The spawn half reads radiusMm from the dungeon spawn JSON, the only spawn-side route the parser exposes today.
    /// </summary>
    [Fact]
    public void SpawnPull_carries_the_hollow_warden_radius_onto_the_boss_and_leaves_trash_null()
    {
        string enemyFile = ContentRoots.KindFiles(AppContext.BaseDirectory, "enemies")
            .Single(f => Path.GetFileName(f) == "hollow-warden.json");
        var defs = (JsArray)JsValue.Parse(File.ReadAllText(enemyFile));
        var boss = defs.Items.OfType<JsObject>().Single(o => o.Get("id") is JsString { Value: "hollowWarden.boss" });
        long? defRadius = (boss.Get("radiusMm") as JsInt)?.Value;
        Assert.Equal<long?>(900, defRadius);

        var dungeon = EncounterJson.ParseDungeon(JsValue.Parse(
            "{\"id\":\"radius\",\"pulls\":[{\"id\":\"boss\",\"spawns\":["
            + "{\"actorId\":1,\"classId\":\"hollowWarden.boss\",\"pos\":{\"x\":0,\"y\":0,\"z\":0},\"yaw\":512,\"health\":100,\"radiusMm\":900},"
            + "{\"actorId\":2,\"classId\":\"melee_add\",\"pos\":{\"x\":3000,\"y\":0,\"z\":0},\"yaw\":0,\"health\":100}]}]}"));
        var spawned = Pulls.SpawnPull(dungeon, 0, 0, _ => { });
        Assert.Equal<long?>(900, spawned.Single(a => a.Id == 1).RadiusMm);
        Assert.Null(spawned.Single(a => a.Id == 2).RadiusMm);
    }
}
