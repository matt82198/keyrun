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

    // BOSS-DOOR (a), (b), (c) were deleted (R-DOOR-3): they asserted that a shut door idles the boss pull, which
    // STATE.md D44 forbids ("no more gates, just path to boss with packs"). The regression guards are (d), (e) and (f).
    [Fact(DisplayName = "BOSS-DOOR (f): the forward route engages the boss with the Seed alive and the bar short (R-DOOR-3 / D44: no door gates the boss pull)")]
    public void Forward_route_engages_the_boss_behind_no_door()
    {
        var gate = Gate(new Dictionary<string, int>(StringComparer.Ordinal) { ["seed.elite"] = 0 });
        var (run, events) = RunTestKit.StepUntil(CreateRun(gate), Steps, Array.Empty<Command>());

        Assert.True(run.State.Actors[SeedActorId].Alive, "the Seed enemy should still be alive");
        Assert.True(Pulls.IsEngaged(run.Encounter, "boss"), "the forward route did not engage the boss with the door's gate removed");
        Assert.True(BossEngaged(events), "no pull_start for the boss pull");
    }

    // R-DOOR-2 (merge of main's #446 wipe-is-release): the Hollow Door gates only the forward route, never a run that
    // begins in the boss pull. A --pull start (EngageStartPull) of the boss with the Seed alive and the bar short must engage.
    [Fact(DisplayName = "BOSS-DOOR (d): a run that begins in the boss pull engages it through a shut door (R-DOOR-2: the door never gates a start)")]
    public void A_run_starting_in_the_boss_pull_engages_through_a_shut_door()
    {
        var gate = Gate(new Dictionary<string, int>(StringComparer.Ordinal) { ["seed.elite"] = 0 });
        var created = CreateRun(gate);
        var atBoss = created with { Encounter = created.Encounter with { PullIndex = 2 } };
        var started = RunRoot.EngageStartPull(atBoss);

        Assert.False(BossDoor.IsOpen(started.State, started.Dungeon, gate), "the door should read shut: the Seed is alive");
        Assert.True(Pulls.IsEngaged(started.Encounter, "boss"), "the start-in-boss-pull run did not engage its pull through the shut door");
        Assert.False(started.State.Actors[BossActorId].Idle, "the boss stands idle after a fight start");
    }

    // R-DOOR-2 latch: once the boss has engaged (the door opened on the forward route, or the run began there), a shut door
    // never idles it again, so a run-back after a wipe release cannot re-lock the pull.
    [Fact(DisplayName = "BOSS-DOOR (e): once the boss has engaged, a shut door does not re-idle it on later ticks (R-DOOR-2 latch)")]
    public void Engaged_boss_stays_engaged_behind_a_shut_door()
    {
        var gate = Gate(new Dictionary<string, int>(StringComparer.Ordinal) { ["seed.elite"] = 0 });
        var created = CreateRun(gate);
        var atBoss = created with { Encounter = created.Encounter with { PullIndex = 2 } };
        var started = RunRoot.EngageStartPull(atBoss);
        var (run, _) = RunTestKit.StepUntil(started, Steps, Array.Empty<Command>());

        Assert.True(Pulls.IsEngaged(run.Encounter, "boss"), "the engaged boss pull was dropped behind a shut door");
        Assert.False(run.State.Actors[BossActorId].Idle, "the engaged boss went idle behind a shut door");
    }
}
