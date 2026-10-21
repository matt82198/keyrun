using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Sim.Tests.Encounter;

/// <summary>
/// BOSS-DOOR (BIBLE s9.5 "The boss door", ruling R3 of 2026-10-21): the Warden's pull may engage only once the Seed pull
/// is cleared AND the Forces bar is at or above the door's requirement. Driven through <see cref="RunRoot.CreateRun"/> /
/// <see cref="RunRoot.StepRun"/> on a layout run: the Warden stands inside the boss's aggro ring from tick 0, so the boss
/// would engage on tick 1 with no gate; the Seed and the other enemies die to real casts, nothing is set by hand.
/// The bar is 1000 points and the door needs 6000 basis points (600 points), so 599 points is short by one point and
/// 600 is exactly at the requirement.
/// </summary>
public sealed class BossDoorGateTests
{
    private const int WardenId = 1;
    private const int SeedActorId = 300;
    private const int ChoirActorId = 400;
    private const int BossActorId = 900;
    private const int Required = 1000;
    private const int RequiredBp = 6000;
    private const int Steps = 60;

    private static readonly Bounds SeedRoom = new(10000, 25000, -5000, 5000);
    private static readonly Bounds ChoirRoom = new(30000, 45000, -5000, 5000);
    private static readonly Bounds BossRoom = new(50000, 70000, -8000, 8000);

    private static NavLayout Layout() => new(
        new[]
        {
            new NavBox("seed-room", SeedRoom, true, null),
            new NavBox("choir-room", ChoirRoom, true, null),
            new NavBox("boss-room", BossRoom, true, null),
        },
        new[]
        {
            new NavTrigger("seed", new Bounds(12000, 25000, -5000, 5000)),
            new NavTrigger("choir", new Bounds(32000, 45000, -5000, 5000)),
            new NavTrigger("boss", new Bounds(52000, 70000, -8000, 8000)),
        },
        new[] { new Vec3(12000, 0, 0), new Vec3(32000, 0, 0), new Vec3(52000, 0, 0) });

    private static DungeonDef Dungeon() => new("boss-door", new[]
    {
        new PullDef("seed", new[] { RunTestKit.Spawn(SeedActorId, "seed.elite", 15000, 2000, 100) }),
        new PullDef("choir", new[] { RunTestKit.Spawn(ChoirActorId, "choir.big", 38000, 2000, 100) }),
        new PullDef("boss", new[] { RunTestKit.Spawn(BossActorId, "boss", 60000, 0, 100000) }),
    });

    private static BossDoorGate Gate(IReadOnlyDictionary<string, int> points) =>
        new("seed", "boss", points, Required, RequiredBp);

    private static RunContent Content(BossDoorGate gate)
    {
        var layout = Layout();
        var kill = RunTestKit.Ability("test.kill", new EffectOp[] { new DamageOp(1000, "physical") });
        var content = RunTestKit.Content(new Dictionary<string, AbilityDef>(StringComparer.Ordinal) { ["test.kill"] = kill }, bounds: layout.Extent);
        return content with { Layout = layout, BossDoor = gate };
    }

    // The Warden stands 3 m from the boss, inside its 4 m aggro ring: with no gate the boss engages on the first tick.
    private static Run CreateRun(BossDoorGate gate) => RunRoot.CreateRun(
        7070,
        new[] { RunTestKit.Member(WardenId, "warden", ActorRole.Tank, 57000, 0, 100000, 100) },
        Dungeon(),
        Content(gate));

    // Kills each named actor with a real cast on tick 0, then runs Steps ticks.
    private static (Run Run, List<SimEvent> Events) KillThenStep(BossDoorGate gate, params int[] targets)
    {
        var commands = targets.Select(t => (Command)new CastCommand(0, WardenId, "test.kill", t)).ToList();
        return RunTestKit.StepUntil(CreateRun(gate), Steps, commands);
    }

    private static string? PullIdOf(SimEvent e) => (e.Get("pullId") as JsString)?.Value;

    private static bool BossEngaged(IReadOnlyList<SimEvent> events) =>
        events.Any(e => e.Type == "pull_start" && PullIdOf(e) == "boss");

    [Fact(DisplayName = "BOSS-DOOR (a): Seed dead but forces short of the requirement keeps the Warden un-engaged and the door shut")]
    public void Seed_dead_forces_short_keeps_the_door_shut()
    {
        var gate = Gate(new Dictionary<string, int>(StringComparer.Ordinal) { ["seed.elite"] = 599 });
        var (run, events) = KillThenStep(gate, SeedActorId);

        Assert.False(run.State.Actors[SeedActorId].Alive, "the Seed enemy should be dead");
        Assert.Equal(5990, Forces.Read(run.State, gate.PointsByEnemy, Required).PercentBp);
        Assert.False(BossDoor.IsOpen(run.State, run.Dungeon, gate), "the door should read closed at 5990 bp");
        Assert.True(run.State.Actors[BossActorId].Idle, "the Warden should still stand idle");
        Assert.False(Pulls.IsEngaged(run.Encounter, "boss"));
        Assert.False(BossEngaged(events), "the boss pull engaged behind a shut door");
    }

    [Fact(DisplayName = "BOSS-DOOR (b): Seed dead and forces at the requirement opens the door and the Warden engages")]
    public void Seed_dead_forces_at_requirement_engages_the_warden()
    {
        var gate = Gate(new Dictionary<string, int>(StringComparer.Ordinal) { ["seed.elite"] = 600 });
        var (run, events) = KillThenStep(gate, SeedActorId);

        Assert.False(run.State.Actors[SeedActorId].Alive, "the Seed enemy should be dead");
        Assert.Equal(RequiredBp, Forces.Read(run.State, gate.PointsByEnemy, Required).PercentBp);
        Assert.True(BossDoor.IsOpen(run.State, run.Dungeon, gate), "the door should read open at exactly the requirement");
        Assert.True(Pulls.IsEngaged(run.Encounter, "boss"), "the Warden should engage once the door is open");
        Assert.True(BossEngaged(events), "no pull_start for the boss pull");
    }

    [Fact(DisplayName = "BOSS-DOOR (c): forces at the requirement but the Seed still alive keeps the door shut")]
    public void Forces_at_requirement_with_seed_alive_keeps_the_door_shut()
    {
        var gate = Gate(new Dictionary<string, int>(StringComparer.Ordinal) { ["seed.elite"] = 180, ["choir.big"] = 600 });
        var (run, events) = KillThenStep(gate, ChoirActorId);

        Assert.True(run.State.Actors[SeedActorId].Alive, "the Seed enemy should still be alive");
        Assert.False(run.State.Actors[ChoirActorId].Alive, "the choir enemy should be dead");
        Assert.Equal(RequiredBp, Forces.Read(run.State, gate.PointsByEnemy, Required).PercentBp);
        Assert.False(BossDoor.IsOpen(run.State, run.Dungeon, gate), "the door should read closed with the Seed alive");
        Assert.False(Pulls.IsEngaged(run.Encounter, "boss"));
        Assert.False(BossEngaged(events), "the boss pull engaged with the Seed alive");
    }
}
