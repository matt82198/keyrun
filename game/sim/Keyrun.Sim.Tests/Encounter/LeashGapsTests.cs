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
/// G2.5 leash gaps (finding f-1c94a912; WoW Mythic+ evade, memory R-BOTTANK-LEASH-1 and R-SYNERGY-2): an evader is immune
/// from the moment it starts walking home (no cast may start on it or by it, and it sheds its effects), and a reset patrol
/// member walks back onto its loop at its speed instead of teleporting onto it. Helpers mirror LeashEvadeTests.
/// </summary>
public sealed class LeashGapsTests
{
    private const int Player = 1;
    private const long EnemySpeed = 200;
    private const long LeashMm = 30000;

    private static readonly Bounds Hall = new(-40000, 60000, -6000, 6000);

    private static NavLayout Layout(IEnumerable<string> pullIds) => new(
        new[] { new NavBox("hall", Hall, true, null) },
        pullIds.Select(id => new NavTrigger(id, new Bounds(59500, 60000, 5500, 6000))).ToList(),
        new[] { new Vec3(59000, 0, 0) });

    private static PullSpawn Enemy(int id, long x, long z, long? aggro, long? leash, long health = 1000) =>
        RunTestKit.Spawn(id, "dummy", x, z, health) with { SpeedMmPerTick = EnemySpeed, AggroMm = aggro, LeashMm = leash };

    private static readonly IReadOnlyList<ScriptStep> Swing = new[] { new ScriptStep(StepTrigger.Loop, "swing") { Every = 20 } };

    private static Run Create(DungeonDef dungeon, long playerX, IReadOnlyList<PatrolPack>? patrols = null, Dictionary<string, EffectDef>? effects = null)
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
            Effects = effects ?? new Dictionary<string, EffectDef>(StringComparer.Ordinal),
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

    [Fact(DisplayName = "G2.5 gap 1: no cast starts on an evading target, and an evading caster starts none")]
    public void Cast_cannot_start_on_or_by_an_evader()
    {
        var dungeon = RunTestKit.OnePull("gap1", "p1", Enemy(101, 10000, 0, aggro: 6000, leash: 7000));
        var run = Create(dungeon, 4000);
        var bolt = RunTestKit.Ability("bolt", new EffectOp[] { new DamageOp(10, "physical") });
        var caster = run.State.Actors[Player];
        var enemy = run.State.Actors[101];

        Assert.Null(Casting.StartError(run.State, caster, bolt, 101, run.State.Tick));
        enemy.Evading = true;

        Assert.NotNull(Casting.StartError(run.State, caster, bolt, 101, run.State.Tick));
        Assert.NotNull(Casting.StartError(run.State, enemy, bolt, Player, run.State.Tick));
    }

    [Fact(DisplayName = "G2.5 gap 2: an evader sheds its effects (a DoT or debuff) the tick it starts walking home, not at reset")]
    public void Evader_sheds_effects_when_it_starts_home()
    {
        var debuff = new EffectDef("dot", 1, null, new EffectModifiers(null, -2000, null, null, null));
        var dungeon = RunTestKit.OnePull("gap2", "p1", Enemy(101, 10000, 0, aggro: 6000, leash: 7000));
        var run = Create(dungeon, 4000, effects: new Dictionary<string, EffectDef>(StringComparer.Ordinal) { ["dot"] = debuff });
        Effects.ApplyEffectInstance(run.State.Actors[101], debuff, Player, 5000, 1, run.State.Tick);
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
            Assert.Empty(e.Effects);
        });
        Assert.True(sawEvading, "the enemy never evaded, so nothing was asserted");
    }

    [Fact(DisplayName = "G2.5 gap 3: a reset patrol member walks back onto its loop at its speed, never teleports (max 200 mm per tick)")]
    public void Reset_patrol_member_walks_back_onto_its_loop()
    {
        var patrol = new PatrolPack(
            "pat",
            new[] { Enemy(201, 0, 0, aggro: null, leash: LeashMm) },
            new[] { new Vec3(40000, 0, 0), new Vec3(38000, 0, 0) },
            50)
        { AggroMm = 6000 };
        var dungeon = RunTestKit.OnePull("patrol", "p1", Enemy(101, 55000, 0, aggro: null, leash: null));
        var run = Create(dungeon, 36000, new[] { patrol });
        var trail = new List<(int Tick, Vec3 Pos)>();
        var (_, events) = Steps(run, 900, r =>
        {
            if (r.State.Tick == 1)
            {
                r.State.Actors[Player].Pos = new Vec3(-30000, 0, 0);
            }
            trail.Add((r.State.Tick, r.State.Actors[201].Pos));
        });
        var reset = Assert.Single(events, e => e.Type == "pack_reset" && ((JsString)e.Get("groupId")!).Value == "pat");
        // The reset tick may carry the evade's arrival snap (<= one step) plus the first walk-back step (<= one step).
        var worst = 0L;
        for (var i = 1; i < trail.Count; i++)
        {
            var step = Dist(trail[i - 1].Pos, trail[i].Pos);
            var cap = trail[i].Tick == reset.Tick ? 2 * EnemySpeed : EnemySpeed;
            Assert.True(step <= cap, $"a patrol member moved {step} mm at tick {trail[i].Tick} (cap {cap}): it teleported");
            worst = Math.Max(worst, step);
        }
        Assert.True(worst > 0, "the patrol member never moved, so nothing was asserted");
    }
}
