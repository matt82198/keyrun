using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Nav;
using Keyrun.Sim.Runs;
using Xunit;
using Xunit.Abstractions;

namespace Keyrun.Sim.Tests.Encounter;

/// <summary>
/// AGGRO-CHASE (f-f0c886eb; Matt 2026-10-22: "why is there still no pathing? and the first couple packs just stand still?
/// doesnt really make positioning important at all and you can just go one at a time lol"). The WoW Mythic+ dungeon
/// loop, on a layout run, in integers at 20 Hz: every enemy has its own aggro radius (it engages its whole pack when a
/// living seated player comes within that distance of the enemy itself, and the pack's <c>pull_start</c> fires on that
/// tick), an assist radius (an idle enemy that an engaged enemy comes near joins with its pack: the body pull, so a chase
/// or a fight dragged past a second pack chains it), a chase along the R-PATH-1 A* path that stops at melee range, and a
/// leash (an enemy pulled too far from home makes its pack evade: it walks home and resets at full health, idle again).
/// The fixture is a test-only hall with one pillar; the content values for the shipped dungeon live in run.json
/// <c>enemyAggro</c>.
/// </summary>
public sealed class AggroChaseTests
{
    private const int Player = 1;
    private const long EnemySpeed = 200;

    // A long hall, and a pillar 4000 x 8000 mm with 2000 mm gaps to the north and south walls.
    private static readonly Bounds Hall = new(-20000, 40000, -6000, 6000);
    private static readonly Bounds Pillar = new(8000, 12000, -4000, 4000);

    private readonly ITestOutputHelper _out;

    public AggroChaseTests(ITestOutputHelper output) => _out = output;

    // Every pull needs a room trigger (NavLayout.RequireTriggers); the aggro rules, not triggers, engage, so each sits in
    // a far corner of the hall that no actor of these runs reaches.
    private static NavLayout Layout(bool pillar, IEnumerable<string> pullIds) => new(
        new[] { new NavBox("hall", Hall, true, null) },
        pullIds.Select(id => new NavTrigger(id, new Bounds(39500, 40000, 5500, 6000))).ToList(),
        new[] { new Vec3(39000, 0, 0) })
    {
        Obstacles = pillar ? new[] { Pillar } : Array.Empty<Bounds>(),
    };

    private static PullSpawn Enemy(int id, long x, long z, long? aggro = null, long? assist = null, long? leash = null, long health = 100) =>
        RunTestKit.Spawn(id, "dummy", x, z, health) with { SpeedMmPerTick = EnemySpeed, AggroMm = aggro, AssistMm = assist, LeashMm = leash };

    private static readonly IReadOnlyList<ScriptStep> Swing = new[] { new ScriptStep(StepTrigger.Loop, "swing") { Every = 20 } };

    // One seated Warden at (playerX, playerZ); every listed enemy swings in melee (range 2000, so the chase stops 1500 mm out).
    private static Run Create(DungeonDef dungeon, long playerX, long playerZ = 0, bool pillar = false)
    {
        var layout = Layout(pillar, dungeon.Pulls.Select(p => p.Id));
        var swing = RunTestKit.Ability("swing", new EffectOp[] { new DamageOp(1, "physical") }, range: 2000);
        var scripts = dungeon.Pulls.SelectMany(p => p.Spawns).ToDictionary(s => s.ActorId, _ => Swing);
        var content = RunTestKit.Content(new Dictionary<string, AbilityDef>(StringComparer.Ordinal) { ["swing"] = swing }, bounds: layout.Extent) with
        {
            Layout = layout,
            EnemyScripts = scripts,
        };
        return RunRoot.CreateRun(1, new[] { RunTestKit.Member(Player, "warden", ActorRole.Tank, playerX, playerZ, 4000, 100) }, dungeon, content);
    }

    private static (Run Run, List<SimEvent> Events) Steps(Run run, int ticks, Action<Run>? each = null)
    {
        var events = new List<SimEvent>();
        for (var i = 0; i < ticks; i++)
        {
            var stepped = RunRoot.StepRun(run, Array.Empty<Command>());
            events.AddRange(stepped.Events);
            run = stepped.Run;
            each?.Invoke(run);
        }
        return (run, events);
    }

    private static int? EngageTick(IEnumerable<SimEvent> events, string groupId) =>
        events.FirstOrDefault(e => (e.Type == "pull_start" && ((JsString)e.Get("pullId")!).Value == groupId) ||
                                   (e.Type == "pack_engage" && ((JsString)e.Get("packId")!).Value == groupId))?.Tick;

    private static long Dist(Vec3 a, Vec3 b) => Movement.Isqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Z - b.Z) * (a.Z - b.Z)));

    [Fact(DisplayName = "Aggro chase: an enemy engages when a seated player stands exactly at its own aggro radius, and not 1 mm farther (outside the pack ring)")]
    public void Enemy_engages_at_its_own_aggro_radius()
    {
        // The pack ring is the default 4000 mm around (10000, 0); the enemy's own radius is 6000 mm.
        var dungeon = RunTestKit.OnePull("aggro", "p1", Enemy(101, 10000, 0, aggro: 6000));

        var (outside, outsideEvents) = Steps(Create(dungeon, 3999), 100);
        Assert.Null(EngageTick(outsideEvents, "p1"));
        Assert.True(outside.State.Actors[101].Idle);
        Assert.Equal(new Vec3(10000, 0, 0), outside.State.Actors[101].Pos);

        var (inside, insideEvents) = Steps(Create(dungeon, 4000), 1);
        var tick = EngageTick(insideEvents, "p1");
        _out.WriteLine($"engage at radius: tick {tick}");
        Assert.Equal(1, tick);
        Assert.False(inside.State.Actors[101].Idle);
        Assert.Contains("p1", inside.Encounter.Engaged);
    }

    [Fact(DisplayName = "Aggro chase: the first enemy of a pack to aggro engages the whole pack with one pull_start naming every member, and the far member runs in")]
    public void First_aggro_engages_the_whole_pack()
    {
        // 101 is 6000 mm from the player (its radius); 102 is 12000 mm away and would never aggro alone.
        var dungeon = RunTestKit.OnePull("aggro", "p1", Enemy(101, 10000, 0, aggro: 6000), Enemy(102, 16000, 0, aggro: 6000));
        var (run, events) = Steps(Create(dungeon, 4000), 1);
        var starts = events.Where(e => e.Type == "pull_start").ToList();
        Assert.Single(starts);
        var ids = ((JsArray)starts[0].Get("actorIds")!).Items.Select(v => ((JsInt)v).Value).ToList();
        Assert.Equal(new long[] { 101, 102 }, ids);
        Assert.False(run.State.Actors[102].Idle);

        var before = run.State.Actors[102].Pos;
        (run, _) = Steps(run, 20);
        var after = run.State.Actors[102].Pos;
        _out.WriteLine($"far member: {before} -> {after}");
        Assert.True(Dist(after, run.State.Actors[Player].Pos) < Dist(before, run.State.Actors[Player].Pos) - 2000, $"the far member did not run at the party: {before} -> {after}");
    }

    [Fact(DisplayName = "Aggro chase: an idle pack within its assist radius of an engaged enemy joins the fight; 1 mm beyond, it stays idle")]
    public void An_engaged_enemy_pulls_a_pack_within_its_assist_radius()
    {
        // p1 (101 at (10000, 0)) engages on the player at 3000 mm. p2's enemy stands 4243 mm from the player (outside its
        // own 2000 mm radius and its 4000 mm ring), and 3000 mm from 101: its assist radius decides.
        DungeonDef Dungeon(long assist) => new("assist", new[]
        {
            new PullDef("p1", new[] { Enemy(101, 10000, 0, aggro: 3000) }),
            new PullDef("p2", new[] { Enemy(201, 10000, 3000, aggro: 2000, assist: assist) }),
        });

        // Social aggro is immediate: the tick p1 engages, its member already stands inside p2's assist radius.
        var (joined, joinedEvents) = Steps(Create(Dungeon(3000), 7000), 1);
        Assert.Equal(1, EngageTick(joinedEvents, "p1"));
        var assistTick = EngageTick(joinedEvents, "p2");
        _out.WriteLine($"assist engage tick {assistTick}");
        Assert.Equal(1, assistTick);
        Assert.False(joined.State.Actors[201].Idle);

        // 101 chases west (toward the player) from tick 1, so the gap only grows: a 2999 mm assist radius never reaches it.
        var (alone, aloneEvents) = Steps(Create(Dungeon(2999), 7000), 200);
        Assert.Equal(1, EngageTick(aloneEvents, "p1"));
        Assert.Null(EngageTick(aloneEvents, "p2"));
        Assert.True(alone.State.Actors[201].Idle);
    }

    [Fact(DisplayName = "Aggro chase: an enemy aggroed behind a pillar chases along its A* path around it and stops at melee range")]
    public void Chase_paths_around_the_pillar_and_stops_at_melee_range()
    {
        // The enemy stands east of the pillar, the player west of it: the straight line runs through the pillar.
        var dungeon = RunTestKit.OnePull("chase", "p1", Enemy(101, 16000, 0, aggro: 13000));
        var run = Create(dungeon, 4000, pillar: true);
        var layout = run.Content.Layout!;
        var detour = false;
        int? reached = null;
        var (final, events) = Steps(run, 300, r =>
        {
            var e = r.State.Actors[101];
            Assert.False(NavGrid.Blocked(layout.Obstacles, e.Pos), $"the enemy stood inside the pillar at {e.Pos}");
            detour |= Math.Abs(e.Pos.Z) > 4000;
            if (reached is null && Dist(e.Pos, r.State.Actors[Player].Pos) <= 2000)
            {
                reached = r.State.Tick;
            }
        });
        Assert.Equal(1, EngageTick(events, "p1"));
        var gap = Dist(final.State.Actors[101].Pos, final.State.Actors[Player].Pos);
        _out.WriteLine($"reached melee tick={reached} final gap={gap} at={final.State.Actors[101].Pos}");
        Assert.True(detour, "the enemy never left the pillar's z band: it did not walk around the pillar");
        Assert.NotNull(reached);
        Assert.InRange(gap, 1000, 1600);
        Assert.Contains(events, e => e.Type == "damage" && ((JsInt)e.Get("sourceId")!).Value == 101);
    }

    [Fact(DisplayName = "Aggro chase: an enemy pulled past its leash evades home, resets at full health, idle and re-pullable; nothing engages it on the way")]
    public void Leash_walks_home_and_resets_at_full_health()
    {
        // The player pulls at the enemy's 6000 mm radius, then runs far west (moved there after the pull tick); the leash
        // is 7000 mm, so the chase crosses it on the way.
        var dungeon = RunTestKit.OnePull("leash", "p1", Enemy(101, 10000, 0, aggro: 6000, leash: 7000, health: 1000));
        var run = Create(dungeon, 4000);
        run.State.Actors[101].Health = 400;
        var home = run.State.Actors[101].Pos;
        var evadingSeen = false;
        var farthest = 0L;
        var (final, events) = Steps(run, 200, r =>
        {
            if (r.State.Tick == 1)
            {
                r.State.Actors[Player].Pos = new Vec3(-15000, 0, 0);
            }
            var e = r.State.Actors[101];
            farthest = Math.Max(farthest, Dist(e.Pos, home));
            if (e.Evading)
            {
                evadingSeen = true;
                Assert.True(e.Idle, "an evading enemy is idle (no script, no chase)");
                Assert.DoesNotContain("p1", r.Encounter.Engaged);
            }
        });
        var evade = events.FirstOrDefault(e => e.Type == "pack_evade");
        var reset = events.FirstOrDefault(e => e.Type == "pack_reset");
        _out.WriteLine($"engage {EngageTick(events, "p1")}, evade {evade?.Tick}, reset {reset?.Tick}, farthest {farthest} mm from home");
        Assert.Equal(1, EngageTick(events, "p1"));
        Assert.NotNull(evade);
        Assert.Equal("p1", ((JsString)evade!.Get("groupId")!).Value);
        Assert.True(evadingSeen);
        Assert.NotNull(reset);
        Assert.True(reset!.Tick > evade.Tick);
        Assert.InRange(farthest, 7001, 7000 + (2 * EnemySpeed));
        var actor = final.State.Actors[101];
        Assert.Equal(home, actor.Pos);
        Assert.Equal(actor.MaxHealth, actor.Health);
        Assert.True(actor.Idle);
        Assert.False(actor.Evading);
        Assert.DoesNotContain("p1", final.Encounter.Engaged);
        Assert.Single(events, e => e.Type == "pull_start");

        // Reset, it is a fresh pack: a player who walks back into its radius pulls it again.
        final.State.Actors[Player].Pos = new Vec3(4000, 0, 0);
        var (_, again) = Steps(final, 1);
        Assert.Single(again, e => e.Type == "pull_start");
    }

    [Fact(DisplayName = "Aggro chase: a chase dragged past a second pack chains it (body pull), and the run is identical twice")]
    public void A_chase_dragged_past_a_pack_chains_it()
    {
        // p1 at (16000, 0) aggroes on the player at (0, 0) and chases west. p2 at (8000, 3000) never sees the player
        // (8544 mm, its own radius 2000), but the chaser passes 3000 mm from it, inside its 3500 mm assist radius.
        var dungeon = new DungeonDef("chain", new[]
        {
            new PullDef("p1", new[] { Enemy(101, 16000, 0, aggro: 16000) }),
            new PullDef("p2", new[] { Enemy(201, 8000, 3000, aggro: 2000, assist: 3500) }),
        });
        string Trace()
        {
            var (run, events) = Steps(Create(dungeon, 0), 120);
            var p2 = EngageTick(events, "p2");
            Assert.Equal(1, EngageTick(events, "p1"));
            Assert.NotNull(p2);
            Assert.True(p2 > 1, $"p2 engaged on tick {p2}, before the chase reached it");
            Assert.False(run.State.Actors[201].Idle);
            Assert.True(Dist(run.State.Actors[201].Pos, run.State.Actors[Player].Pos) < 8000, "the chained pack did not join the fight");
            return string.Join("|", events.Select(e => e.Tick + e.Type)) + run.State.Actors[201].Pos + run.State.Actors[101].Pos;
        }
        var first = Trace();
        Assert.Equal(first, Trace());
    }
}
