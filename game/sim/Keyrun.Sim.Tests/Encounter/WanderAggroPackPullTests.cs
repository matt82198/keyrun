using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Nav;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Sim.Tests.Encounter;

/// <summary>
/// R-UNITAGGRO-1 (G7, top priority; Matt verbatim: "each unit in a pack needs its own aggro and then the leash effect
/// occurs"). A pack walking under IdleWander (f-c1044aa4) whose one member comes within its OWN aggro radius of a
/// seated player must engage, whole: that member aggros, and the rest of its pack comes with it (one pull_start naming
/// every member). The wanderer's sight is measured from its wander anchor (EnemyAggro.SeesPlayer), so the test places
/// the player relative to the anchor, outside the pack ring (default 4000 mm, Pulls.DefaultAggroMm), so only the
/// wanderer's own radius can fire. The two pack mates have no radius of their own and can only join through the pack.
/// </summary>
public sealed class WanderAggroPackPullTests
{
    private const int Player = 1;
    private const int Wanderer = 101;
    private const int PackMateA = 102;
    private const int PackMateB = 103;
    private const int WalkBudgetTicks = 400;

    // A long hall, the same test-only fixture as AggroChaseTests; no obstacles.
    private static readonly Bounds Hall = new(-20000, 40000, -6000, 6000);

    // Every pull needs a room trigger (NavLayout.RequireTriggers); the aggro rules engage, so the trigger sits in a far
    // corner no actor reaches.
    private static NavLayout Layout(IEnumerable<string> pullIds) => new(
        new[] { new NavBox("hall", Hall, true, null) },
        pullIds.Select(id => new NavTrigger(id, new Bounds(39500, 40000, 5500, 6000))).ToList(),
        new[] { new Vec3(39000, 0, 0) });

    private static PullSpawn Enemy(int id, long x, long z, long? aggro) =>
        RunTestKit.Spawn(id, "dummy", x, z, 100) with { AggroMm = aggro };

    // One seated Warden at (playerX, 0). IdleWander is on: the pack walks while it is idle.
    private static Run Create(DungeonDef dungeon, long playerX)
    {
        var layout = Layout(dungeon.Pulls.Select(p => p.Id));
        var content = RunTestKit.Content(new Dictionary<string, AbilityDef>(StringComparer.Ordinal), bounds: layout.Extent) with
        {
            Layout = layout,
            EnemyScripts = new Dictionary<int, IReadOnlyList<ScriptStep>>(),
            IdleWander = true,
        };
        return RunRoot.CreateRun(1, new[] { RunTestKit.Member(Player, "warden", ActorRole.Tank, playerX, 0, 4000, 100) }, dungeon, content);
    }

    private static (Run Run, List<SimEvent> Events) StepOnce(Run run)
    {
        var stepped = RunRoot.StepRun(run, Array.Empty<Command>());
        return (stepped.Run, stepped.Events.ToList());
    }

    [Fact(DisplayName = "Wander aggro: a wandering pack member that enters its own aggro radius of a player engages, and its whole pack comes with it in one pull_start")]
    public void Wandering_member_that_aggros_pulls_its_whole_pack()
    {
        // The wanderer has its own 6000 mm radius; the pack ring is the default 4000 mm around the pack centre (10000, 0).
        // The mates have no radius of their own.
        var dungeon = RunTestKit.OnePull("wander-pack", "p1",
            Enemy(Wanderer, 10000, 0, aggro: 6000),
            Enemy(PackMateA, 10000, 1000, aggro: null),
            Enemy(PackMateB, 10000, -1000, aggro: null));
        var run = Create(dungeon, playerX: -15000);
        var events = new List<SimEvent>();

        // Let the wander begin and walk the wanderer off its post (its anchor is where it stood at tick 0).
        var walked = false;
        for (var i = 0; i < WalkBudgetTicks && !walked; i++)
        {
            var step = StepOnce(run);
            run = step.Run;
            events.AddRange(step.Events);
            var w = run.State.Actors[Wanderer];
            walked = w.WanderAnchor is { } anchor && w.Pos != anchor;
        }
        Assert.True(walked, $"the wanderer never walked off its anchor within {WalkBudgetTicks} ticks (wander is not running)");
        Assert.DoesNotContain(events, e => e.Type == "pull_start");
        Assert.All(new[] { Wanderer, PackMateA, PackMateB }, id => Assert.True(run.State.Actors[id].Idle, $"actor {id} engaged before any player was in range"));

        // Put the player 6000 mm west of the wanderer's ANCHOR: inside its own radius (measured from the anchor), outside
        // the pack ring (the pack centre is at least 6000 mm away, so the ring cannot fire).
        var anchor0 = run.State.Actors[Wanderer].WanderAnchor!.Value;
        run.State.Actors[Player].Pos = new Vec3(anchor0.X - 6000, 0, anchor0.Z);

        var step1 = StepOnce(run);
        var pullStart = step1.Events.Where(e => e.Type == "pull_start").ToList();
        Assert.True(pullStart.Count == 1, $"expected exactly one pull_start on the tick the wanderer aggroed, got {pullStart.Count}");

        var engaged = ((JsArray)pullStart[0].Get("actorIds")!).Items.Select(v => ((JsInt)v).Value).OrderBy(v => v).ToArray();
        Assert.True(
            engaged.SequenceEqual(new long[] { Wanderer, PackMateA, PackMateB }),
            $"the pull_start did not name the whole pack: [{string.Join(",", engaged)}] (expected [{Wanderer},{PackMateA},{PackMateB}])");

        Assert.All(new[] { Wanderer, PackMateA, PackMateB }, id => Assert.False(step1.Run.State.Actors[id].Idle, $"pack member {id} did not stop being idle when its pack engaged"));
    }

    // Squared XZ distance, the same integer metric Aggro.InRing uses, widened to long before squaring.
    private static long Dist2(Vec3 a, Vec3 b)
    {
        long dx = (long)a.X - b.X;
        long dz = (long)a.Z - b.Z;
        return dx * dx + dz * dz;
    }

    [Fact(DisplayName = "Wander aggro: a wandering member standing next to a player, far from its own anchor, still aggros on that player")]
    public void Wanderer_near_player_but_far_from_anchor_aggros()
    {
        // IdleWander keeps every wander point within ReachMm (2000 mm) of its anchor, so the wanderer can never be 14000 mm
        // from it; the bug window is narrower. The wanderer walks to a point about 1500 mm off its anchor, then the player
        // stands 5000 mm beyond it on the far side: within the wanderer's 6000 mm radius from where it stands, but more than
        // 6000 mm from its ANCHOR. The pack ring (centred on the pull's spawn point, 20000, default 4000 mm) cannot fire.
        var dungeon = RunTestKit.OnePull("wander-near-far",
            "p1",
            Enemy(Wanderer, 20000, 0, aggro: 6000),
            Enemy(PackMateA, 20000, 1000, aggro: null),
            Enemy(PackMateB, 20000, -1000, aggro: null));
        var run = Create(dungeon, playerX: 5000);
        var events = new List<SimEvent>();
        var anchor = new Vec3(20000, 0, 0);

        // Walk the wanderer off its anchor (at least 1500 mm, so the far-side placement below clears the 6000 mm radius).
        var farEnough = false;
        for (var i = 0; i < WalkBudgetTicks * 5 && !farEnough; i++)
        {
            var step = StepOnce(run);
            run = step.Run;
            events.AddRange(step.Events);
            var w = run.State.Actors[Wanderer];
            farEnough = w.WanderAnchor is { } a && Dist2(w.Pos, a) >= 1500L * 1500L;
        }
        Assert.True(farEnough, $"the wanderer never walked 1500 mm off its anchor within {WalkBudgetTicks * 5} ticks (wander is not running)");
        Assert.DoesNotContain(events, e => e.Type == "pull_start");
        Assert.All(new[] { Wanderer, PackMateA, PackMateB }, id => Assert.True(run.State.Actors[id].Idle, $"actor {id} engaged before any player was in range"));

        // Stand the player 5000 mm beyond the wanderer, along its dominant offset axis from the anchor (away from the anchor).
        var w0 = run.State.Actors[Wanderer];
        var off = new Vec3(w0.Pos.X - anchor.X, 0, w0.Pos.Z - anchor.Z);
        var playerPos = Math.Abs(off.X) >= Math.Abs(off.Z)
            ? new Vec3(w0.Pos.X + (off.X < 0 ? -5000 : 5000), 0, w0.Pos.Z)
            : new Vec3(w0.Pos.X, 0, w0.Pos.Z + (off.Z < 0 ? -5000 : 5000));
        run.State.Actors[Player].Pos = playerPos;

        // Preconditions that make the test meaningful: the player is outside the wanderer's radius measured from the anchor,
        // inside it measured from the wanderer, and outside the pack ring.
        Assert.True(Dist2(playerPos, anchor) > 6000L * 6000L, "precondition: the player must be outside the wanderer's radius from its anchor");
        Assert.True(Dist2(playerPos, w0.Pos) <= 6000L * 6000L, "precondition: the player must be inside the wanderer's radius from where it stands");
        Assert.True(Dist2(playerPos, anchor) > 4000L * 4000L, "precondition: the player must be outside the pack ring");

        var step1 = StepOnce(run);
        var pullStart = step1.Events.Where(e => e.Type == "pull_start").ToList();
        Assert.True(
            pullStart.Count == 1,
            $"the wanderer stood within its own 6000 mm radius of the player (measured from where it stands) but did not aggro: expected exactly one pull_start, got {pullStart.Count}");

        var engaged = ((JsArray)pullStart[0].Get("actorIds")!).Items.Select(v => ((JsInt)v).Value).OrderBy(v => v).ToArray();
        Assert.True(
            engaged.SequenceEqual(new long[] { Wanderer, PackMateA, PackMateB }),
            $"the pull_start did not name the whole pack: [{string.Join(",", engaged)}] (expected [{Wanderer},{PackMateA},{PackMateB}])");
    }
}
