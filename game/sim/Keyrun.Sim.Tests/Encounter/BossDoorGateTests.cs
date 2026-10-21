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
/// No door idles the boss pull (STATE.md D44, Matt verbatim: "no more gates, just path to boss with packs"; R-DOOR-3 and
/// R-DOOR-4). The Hollow Door gate and the DOOR-100 detour were removed from #515; these are the regression guards that
/// the boss pull engages on the forward route and at a fight start, with the Seed alive and the Forces bar short, and stays
/// engaged afterwards. Driven through <see cref="RunRoot.CreateRun"/> / <see cref="RunRoot.StepRun"/> on a layout run.
/// </summary>
public sealed class BossDoorGateTests
{
    private const int WardenId = 1;
    private const int SeedActorId = 300;
    private const int BossActorId = 900;
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
        new PullDef("boss", new[] { RunTestKit.Spawn(BossActorId, "boss", 60000, 0, 100000) }),
    });

    private static RunContent Content()
    {
        var layout = Layout();
        var kill = RunTestKit.Ability("test.kill", new EffectOp[] { new DamageOp(1000, "physical") });
        var content = RunTestKit.Content(new Dictionary<string, AbilityDef>(StringComparer.Ordinal) { ["test.kill"] = kill }, bounds: layout.Extent);
        return content with { Layout = layout };
    }

    // The Warden stands 3 m from the boss, inside its 4 m aggro ring: the boss engages on the first tick with the Seed alive.
    private static Run CreateRun() => RunRoot.CreateRun(
        7070,
        new[] { RunTestKit.Member(WardenId, "warden", ActorRole.Tank, 57000, 0, 100000, 100) },
        Dungeon(),
        Content());

    private static bool BossEngaged(IReadOnlyList<SimEvent> events) =>
        events.Any(e => e.Type == "pull_start" && (e.Get("pullId") as JsString)?.Value == "boss");

    [Fact(DisplayName = "BOSS-DOOR (d): a run that begins in the boss pull engages it with the Seed alive and the bar short (R-DOOR-4 / D44: no door gates a start)")]
    public void A_run_starting_in_the_boss_pull_engages_the_boss()
    {
        var created = CreateRun();
        var atBoss = created with { Encounter = created.Encounter with { PullIndex = 1 } };
        var started = RunRoot.EngageStartPull(atBoss);

        Assert.True(Pulls.IsEngaged(started.Encounter, "boss"), "the start-in-boss-pull run did not engage its pull");
        Assert.False(started.State.Actors[BossActorId].Idle, "the boss stands idle after a fight start");
    }

    [Fact(DisplayName = "BOSS-DOOR (e): once the boss has engaged it stays engaged on later ticks (D44 latch: no run-back re-idles the pull)")]
    public void Engaged_boss_stays_engaged()
    {
        var created = CreateRun();
        var atBoss = created with { Encounter = created.Encounter with { PullIndex = 1 } };
        var started = RunRoot.EngageStartPull(atBoss);
        var (run, _) = RunTestKit.StepUntil(started, Steps, Array.Empty<Command>());

        Assert.True(Pulls.IsEngaged(run.Encounter, "boss"), "the engaged boss pull was dropped");
        Assert.False(run.State.Actors[BossActorId].Idle, "the engaged boss went idle");
    }

    [Fact(DisplayName = "BOSS-DOOR (f): the forward route engages the boss with the Seed alive (D44: no door gates the forward route)")]
    public void Forward_route_engages_the_boss()
    {
        var (run, events) = RunTestKit.StepUntil(CreateRun(), Steps, Array.Empty<Command>());

        Assert.True(run.State.Actors[SeedActorId].Alive, "the Seed enemy should still be alive");
        Assert.True(Pulls.IsEngaged(run.Encounter, "boss"), "the forward route did not engage the boss");
        Assert.True(BossEngaged(events), "no pull_start for the boss pull");
    }
}
