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
/// DUNGEON-SYNERGY lane L1 (design/DUNGEON-SYNERGY.md section 2.2, R-SYNERGY-2 calls 1 and 5; WoW evade): a pack walking
/// home is immune (no target, no area hit), a pack dragged 30000 mm in a straight line from its home resets mid-fight at
/// full health with one <c>pack_reset</c> carrying <c>cause: "leash"</c>, and a patrol leashes from the node it was pulled at.
/// </summary>
public sealed class LeashEvadeTests
{
    private const int Player = 1;
    private const long EnemySpeed = 200;
    private const long LeashMm = 30000;

    private static readonly Bounds Hall = new(-40000, 60000, -6000, 6000);

    private readonly ITestOutputHelper _out;

    public LeashEvadeTests(ITestOutputHelper output) => _out = output;

    private static NavLayout Layout(IEnumerable<string> pullIds) => new(
        new[] { new NavBox("hall", Hall, true, null) },
        pullIds.Select(id => new NavTrigger(id, new Bounds(59500, 60000, 5500, 6000))).ToList(),
        new[] { new Vec3(59000, 0, 0) });

    private static PullSpawn Enemy(int id, long x, long z, long? aggro, long? leash, long health = 1000) =>
        RunTestKit.Spawn(id, "dummy", x, z, health) with { SpeedMmPerTick = EnemySpeed, AggroMm = aggro, LeashMm = leash };

    private static readonly IReadOnlyList<ScriptStep> Swing = new[] { new ScriptStep(StepTrigger.Loop, "swing") { Every = 20 } };

    private static Run Create(DungeonDef dungeon, long playerX, IReadOnlyList<PatrolPack>? patrols = null)
    {
        var layout = Layout(dungeon.Pulls.Select(p => p.Id));
        var swing = RunTestKit.Ability("swing", new EffectOp[] { new DamageOp(1, "physical") }, range: 2000);
        var spawns = dungeon.Pulls.SelectMany(p => p.Spawns).Concat((patrols ?? Array.Empty<PatrolPack>()).SelectMany(k => k.Spawns));
        var scripts = spawns.ToDictionary(s => s.ActorId, _ => Swing);
        var content = RunTestKit.Content(new Dictionary<string, AbilityDef>(StringComparer.Ordinal) { ["swing"] = swing }, bounds: layout.Extent) with
        {
            Layout = layout,
            EnemyScripts = scripts,
            Patrols = patrols,
        };
        return RunRoot.CreateRun(1, new[] { RunTestKit.Member(Player, "warden", ActorRole.Tank, playerX, 0, 4000, 100) }, dungeon, content);
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

    private static long Dist(Vec3 a, Vec3 b)
    {
        var dx = a.X - b.X;
        var dz = a.Z - b.Z;
        return (long)Math.Sqrt((double)((dx * dx) + (dz * dz)));
    }

    [Fact(DisplayName = "Leash evade: an enemy walking home is untargetable by a single-target and an area ability, and an idle one is not (control)")]
    public void Evader_is_not_a_target()
    {
        var dungeon = RunTestKit.OnePull("evade", "p1", Enemy(101, 10000, 0, aggro: 6000, leash: 7000));
        var run = Create(dungeon, 4000);
        var bolt = RunTestKit.Ability("bolt", new EffectOp[] { new DamageOp(10, "physical") });
        var burst = RunTestKit.Ability("burst", new EffectOp[] { new DamageOp(10, "physical") }, new AbilityShape(ShapeKind.Circle) { Radius = 90000 });
        var control = Targeting.ResolveTargetsOn(run.State, run.State.Actors[Player], bolt, Affinity.Hostile, 101);
        Assert.Single(control);
        var sawEvading = false;
        Steps(run, 120, r =>
        {
            if (r.State.Tick == 1)
            {
                r.State.Actors[Player].Pos = new Vec3(-15000, 0, 0);
            }
            var e = r.State.Actors[101];
            if (!e.Evading)
            {
                return;
            }
            sawEvading = true;
            var caster = r.State.Actors[Player];
            Assert.Empty(Targeting.ResolveTargetsOn(r.State, caster, bolt, Affinity.Hostile, 101));
            Assert.DoesNotContain(Targeting.ResolveTargetsOn(r.State, caster, burst, Affinity.Hostile, null), a => a.Id == 101);
        });
        Assert.True(sawEvading, "the enemy never evaded, so nothing was asserted");
    }

    [Fact(DisplayName = "Leash evade: a pack dragged past 30000 mm in a straight line resets mid-fight at full health with one pack_reset, cause leash")]
    public void Pack_dragged_30000_mm_resets_with_a_leash_cause()
    {
        var dungeon = RunTestKit.OnePull("drag", "p1", Enemy(101, 40000, 0, aggro: 6000, leash: LeashMm));
        var run = Create(dungeon, 36000);
        run.State.Actors[101].Health = 400;
        var home = run.State.Actors[101].Pos;
        var farthest = 0L;
        var (final, events) = Steps(run, 600, r =>
        {
            if (r.State.Tick == 1)
            {
                r.State.Actors[Player].Pos = new Vec3(-30000, 0, 0);
            }
            farthest = Math.Max(farthest, Dist(r.State.Actors[101].Pos, home));
        });
        var evade = Assert.Single(events, e => e.Type == "pack_evade");
        var reset = Assert.Single(events, e => e.Type == "pack_reset");
        _out.WriteLine($"evade {evade.Tick} reset {reset.Tick} farthest {farthest}");
        Assert.InRange(farthest, LeashMm + 1, LeashMm + (2 * EnemySpeed));
        Assert.True(evade.Get("cause") is JsString { Value: "leash" }, "pack_evade carries no cause: leash");
        Assert.True(reset.Get("cause") is JsString { Value: "leash" }, "pack_reset carries no cause: leash");
        var actor = final.State.Actors[101];
        Assert.Equal(actor.MaxHealth, actor.Health);
        Assert.Equal(home, actor.Pos);
    }

    [Fact(DisplayName = "Leash evade: a patrol pulled at a node leashes from that node (30000 mm), walks home and resets")]
    public void Patrol_leashes_from_its_pull_node()
    {
        var patrol = new PatrolPack(
            "pat",
            new[] { Enemy(201, 0, 0, aggro: null, leash: LeashMm) },
            new[] { new Vec3(40000, 0, 0), new Vec3(38000, 0, 0) },
            50)
        { AggroMm = 6000 };
        var dungeon = RunTestKit.OnePull("patrol", "p1", Enemy(101, 55000, 0, aggro: null, leash: null));
        var run = Create(dungeon, 36000, new[] { patrol });
        var node = default(Vec3);
        var farthest = 0L;
        var (final, events) = Steps(run, 700, r =>
        {
            if (r.State.Tick == 1)
            {
                r.State.Actors[Player].Pos = new Vec3(-30000, 0, 0);
            }
            var e = r.State.Actors[201];
            if (!e.Idle && node == default)
            {
                node = e.Home ?? e.Pos;
            }
            if (node != default)
            {
                farthest = Math.Max(farthest, Dist(e.Pos, node));
            }
        });
        var evade = Assert.Single(events, e => e.Type == "pack_evade" && ((JsString)e.Get("groupId")!).Value == "pat");
        Assert.Single(events, e => e.Type == "pack_reset" && ((JsString)e.Get("groupId")!).Value == "pat");
        _out.WriteLine($"patrol evade {evade.Tick} farthest {farthest} from node {node}");
        Assert.InRange(farthest, LeashMm + 1, LeashMm + (2 * EnemySpeed));
        Assert.Equal(final.State.Actors[201].MaxHealth, final.State.Actors[201].Health);
    }
}
