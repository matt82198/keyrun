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
/// ASSIST-SCOPE (R-PATH-5 (p), ruling on top of (o), defer to WoW Mythic+). Trash-to-trash assist stays (adjacent packs
/// chain, as AggroChaseTests pins). A BOSS group is never an assist source for another pack. The boss marker is the sim's
/// own: a boss group is a group whose id is the boss pull id or carries the <c>boss-</c> prefix (IdleWander.IsBossGroup,
/// R-MULTIBOSS-1); ActorState carries no boss flag. Measured in the S5 hall-return trace: at tick 9138 the engaged hollowWarden boss
/// (actor 900) stood within assistMm 3000 of hall sentry 510's anchor (2930 mm) and <c>EnemyAggro.Assisted</c> pulled the
/// idle hall-sentries pack, with no player within 13000 mm. The harness mirrors <c>AggroChaseTests</c>; the fixture
/// is a test-only hall with no pillar.
/// </summary>
public sealed class AssistScopeTests
{
    private const int Player = 1;
    private const long EnemySpeed = 200;

    // A long hall, far corners for the trigger rooms (no actor of these runs reaches them).
    private static readonly Bounds Hall = new(-20000, 40000, -6000, 6000);

    private readonly ITestOutputHelper _out;

    /// <summary>Creates the test with xUnit's output sink (engage ticks and distances are printed).</summary>
    /// <param name="output">The sink.</param>
    public AssistScopeTests(ITestOutputHelper output) => _out = output;

    private static NavLayout Layout(IEnumerable<string> pullIds) => new(
        new[] { new NavBox("hall", Hall, true, null) },
        pullIds.Select(id => new NavTrigger(id, new Bounds(39500, 40000, 5500, 6000))).ToList(),
        new[] { new Vec3(39000, 0, 0) })
    {
        Obstacles = Array.Empty<Bounds>(),
    };

    private static PullSpawn Enemy(int id, long x, long z, long? aggro = null, long? assist = null) =>
        RunTestKit.Spawn(id, "dummy", x, z, 100) with { SpeedMmPerTick = EnemySpeed, AggroMm = aggro, AssistMm = assist };

    private static readonly IReadOnlyList<ScriptStep> Swing = new[] { new ScriptStep(StepTrigger.Loop, "swing") { Every = 20 } };

    // One seated Warden at (playerX, 0); every enemy swings in melee (range 2000).
    private static Run Create(DungeonDef dungeon, long playerX)
    {
        var layout = Layout(dungeon.Pulls.Select(p => p.Id));
        var swing = RunTestKit.Ability("swing", new EffectOp[] { new DamageOp(1, "physical") }, range: 2000);
        var scripts = dungeon.Pulls.SelectMany(p => p.Spawns).ToDictionary(s => s.ActorId, _ => Swing);
        var content = RunTestKit.Content(new Dictionary<string, AbilityDef>(StringComparer.Ordinal) { ["swing"] = swing }, bounds: layout.Extent) with
        {
            Layout = layout,
            EnemyScripts = scripts,
        };
        return RunRoot.CreateRun(1, new[] { RunTestKit.Member(Player, "warden", ActorRole.Tank, playerX, 0, 4000, 100) }, dungeon, content);
    }

    private static (Run Run, List<SimEvent> Events) Steps(Run run, int ticks)
    {
        var events = new List<SimEvent>();
        for (var i = 0; i < ticks; i++)
        {
            var stepped = RunRoot.StepRun(run, Array.Empty<Command>());
            events.AddRange(stepped.Events);
            run = stepped.Run;
        }
        return (run, events);
    }

    private static int? EngageTick(IEnumerable<SimEvent> events, string groupId) =>
        events.FirstOrDefault(e => (e.Type == "pull_start" && ((JsString)e.Get("pullId")!).Value == groupId) ||
                                   (e.Type == "pack_engage" && ((JsString)e.Get("packId")!).Value == groupId))?.Tick;

    private static long Dist(Vec3 a, Vec3 b) => Movement.Isqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Z - b.Z) * (a.Z - b.Z)));

    [Fact(DisplayName = "Assist scope (R-PATH-5 (p)): an engaged BOSS does not assist-pull an idle trash pack inside its assist radius")]
    public void An_engaged_boss_does_not_assist_pull_an_idle_trash_pack()
    {
        // The boss group (pull id "boss", the sim's boss marker) engages on the player at 3000 mm (its own radius). The
        // sentries trash pack stands 4243 mm from the player (outside its own 2000 mm radius and its 4000 mm ring) and
        // 3000 mm from the boss enemy: its assist radius is the only thing that could pull it.
        var dungeon = new DungeonDef("assist-scope", new[]
        {
            new PullDef("boss", new[] { Enemy(101, 10000, 0, aggro: 3000) }),
            new PullDef("sentries", new[] { Enemy(201, 10000, 3000, aggro: 2000, assist: 3000) }),
        });
        var (run, events) = Steps(Create(dungeon, 7000), 1);
        var engaged = run.State.Actors[101];
        var member = run.State.Actors[201];
        var distance = Dist(engaged.Pos, member.WanderAnchor ?? member.Pos);
        var sentriesEngage = EngageTick(events, "sentries");
        _out.WriteLine($"boss engage t{EngageTick(events, "boss")}; sentries member 201 Idle={member.Idle}, distance {distance} mm, sentries engage {sentriesEngage}");

        // Vacuity guard: the boss pack really is engaged this tick, so the assist source exists.
        Assert.Equal(1, EngageTick(events, "boss"));
        Assert.False(engaged.Idle);
        Assert.True(
            member.Idle && sentriesEngage is null,
            $"assist scope: sentries member 201 Idle={member.Idle} at distance {distance} mm (assistMm 3000) from engaged enemy 101 of pack 'boss'; " +
            $"the engaged BOSS group 'boss' pulled trash pack 'sentries' (engage tick {sentriesEngage}), R-PATH-5 (p) forbids a boss group as an assist source for another pack");
    }

    [Fact(DisplayName = "Assist scope guard (R-PATH-5 (p)): an engaged TRASH enemy of a different pull still assist-pulls an idle pack inside its assist radius (adjacent packs chain)")]
    public void An_engaged_trash_enemy_still_assist_pulls_an_adjacent_pack()
    {
        // The same geometry as the boss fact, with the engaged enemy in a trash pull ("trash"): AggroChaseTests pins this.
        var dungeon = new DungeonDef("assist-scope-trash", new[]
        {
            new PullDef("trash", new[] { Enemy(101, 10000, 0, aggro: 3000) }),
            new PullDef("sentries", new[] { Enemy(201, 10000, 3000, aggro: 2000, assist: 3000) }),
        });
        var (run, events) = Steps(Create(dungeon, 7000), 1);
        Assert.Equal(1, EngageTick(events, "trash"));
        Assert.Equal(1, EngageTick(events, "sentries"));
        Assert.False(run.State.Actors[201].Idle, "an engaged trash enemy did not assist-pull the adjacent idle pack");
    }

    [Fact(DisplayName = "Assist scope guard: an engaged member of the same pack still pulls its packmate (social aggro within a pack)")]
    public void An_engaged_packmate_still_pulls_its_pack()
    {
        // One pack, both members in it. Only 101 sees the player (3000 mm); 202 does not (4243 mm, radius 2000). The pack
        // ring is shrunk to 1000 mm so the pack's own ring cannot engage it: the engage has to come from its packmate.
        var dungeon = new DungeonDef("assist-scope-guard", new[]
        {
            new PullDef("pack", new[] { Enemy(101, 10000, 0, aggro: 3000), Enemy(202, 10000, 3000, aggro: 2000, assist: 3000) }) with { AggroMm = 1000 },
        });
        var (run, events) = Steps(Create(dungeon, 7000), 1);
        var start = Assert.Single(events, e => e.Type == "pull_start");
        Assert.Equal(1, start.Tick);
        Assert.Equal("pack", ((JsString)start.Get("pullId")!).Value);
        var ids = ((JsArray)start.Get("actorIds")!).Items.Select(v => ((JsInt)v).Value).ToList();
        Assert.Equal(new long[] { 101, 202 }, ids);
        _out.WriteLine($"same-pack guard: packmate 202 Idle={run.State.Actors[202].Idle} after tick 1");
        Assert.False(run.State.Actors[202].Idle, "an engaged member of the same pack did not pull its packmate");
    }
}
