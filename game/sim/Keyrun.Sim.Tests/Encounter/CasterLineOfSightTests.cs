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
/// L5 caster-los (design/DUNGEON-SYNERGY.md section 5, R-SYNERGY-2, G2.5): an enemy caster whose straight integer ray
/// to its target crosses an obstacle starts no cast; <see cref="Chase"/> walks it along its A* path until the ray is
/// clear, then it casts. This is what lets a tank pull a pack around a pillar so the casters have to walk in, as in
/// Mythic+. The fixture is a test-only hall with one pillar between the party and the caster; the rule is integer-only.
/// </summary>
public sealed class CasterLineOfSightTests
{
    private const int Player = 1;
    private const int Caster = 101;

    private static readonly Bounds Hall = new(-20000, 40000, -6000, 6000);
    private static readonly Bounds Pillar = new(8000, 12000, -4000, 4000);

    private static NavLayout Layout(bool pillar) => new(
        new[] { new NavBox("hall", Hall, true, null) },
        new[] { new NavTrigger("p1", new Bounds(39500, 40000, 5500, 6000)) },
        new[] { new Vec3(39000, 0, 0) })
    {
        Obstacles = pillar ? new[] { Pillar } : Array.Empty<Bounds>(),
    };

    // The caster stands 12000 mm east of the party behind the pillar, well inside its 20000 mm bolt range, and aggros at 12000.
    private static Run Create(bool pillar)
    {
        var layout = Layout(pillar);
        var bolt = RunTestKit.Ability("bolt", new EffectOp[] { new DamageOp(1, "arcane") }, range: 20000) with { CastTicks = 10 };
        var spawn = RunTestKit.Spawn(Caster, "dummy", 16000, 0, 100) with { SpeedMmPerTick = 200, AggroMm = 12000 };
        var dungeon = RunTestKit.OnePull("los", "p1", spawn);
        var content = RunTestKit.Content(new Dictionary<string, AbilityDef>(StringComparer.Ordinal) { ["bolt"] = bolt }, bounds: layout.Extent) with
        {
            Layout = layout,
            EnemyScripts = new Dictionary<int, IReadOnlyList<ScriptStep>> { [Caster] = new[] { new ScriptStep(StepTrigger.Loop, "bolt") { Every = 20 } } },
        };
        return RunRoot.CreateRun(1, new[] { RunTestKit.Member(Player, "warden", ActorRole.Tank, 4000, 0, 4000, 100) }, dungeon, content);
    }

    private sealed record Trace(List<SimEvent> Events, int? FirstCastTick, List<(int Tick, Vec3 CasterAt, Vec3 TargetAt)> Starts, Vec3 End);

    private static Trace Run600(bool pillar)
    {
        var run = Create(pillar);
        var events = new List<SimEvent>();
        var starts = new List<(int, Vec3, Vec3)>();
        int? first = null;
        for (var i = 0; i < 600; i++)
        {
            var before = run.State.Actors[Caster];
            var wasCasting = before.CastId is not null;
            var stepped = RunRoot.StepRun(run, Array.Empty<Command>());
            events.AddRange(stepped.Events);
            run = stepped.Run;
            if (!wasCasting && run.State.Actors[Caster].CastId is not null)
            {
                // The cast started this tick, before the caster's move: the ray is read from where it stood.
                first ??= run.State.Tick;
                starts.Add((run.State.Tick, before.Pos, run.State.Actors[Player].Pos));
            }
        }
        return new Trace(events, first, starts, run.State.Actors[Caster].Pos);
    }

    [Fact(DisplayName = "Caster LoS: no cast starts while the straight ray to the target crosses the pillar, and the caster walks until one does")]
    public void A_caster_behind_a_pillar_starts_no_cast_until_the_ray_is_clear()
    {
        var t = Run600(pillar: true);
        Assert.NotEmpty(t.Starts);
        Assert.All(t.Starts, s => Assert.False(
            NavGrid.SegmentCrosses(s.CasterAt, s.TargetAt, Pillar),
            $"tick {s.Tick}: the caster began a cast from ({s.CasterAt.X},{s.CasterAt.Z}) at ({s.TargetAt.X},{s.TargetAt.Z}) through the pillar"));
        Assert.True(t.FirstCastTick > 20, $"the first cast came at tick {t.FirstCastTick}: the caster cast before it walked out from behind the pillar");
    }

    [Fact(DisplayName = "Caster LoS: a cast withheld for sight emits one cast_los_withheld event naming the caster, ability and target")]
    public void A_withheld_cast_emits_cast_los_withheld()
    {
        var t = Run600(pillar: true);
        var withheld = t.Events.Where(e => e.Type == "cast_los_withheld").ToList();
        Assert.NotEmpty(withheld);
        var e0 = withheld[0];
        Assert.Equal(Caster, ((JsInt)e0.Get("actorId")!).Value);
        Assert.Equal("bolt", ((JsString)e0.Get("abilityId")!).Value);
        Assert.Equal(Player, ((JsInt)e0.Get("targetId")!).Value);
        Assert.DoesNotContain(t.Events, e => e.Type == "cast_refused" && e.Get("reason") is JsString { Value: "no_line_of_sight" });
    }

    [Fact(DisplayName = "Caster LoS control: with no pillar the caster casts at its first fire and no cast is withheld")]
    public void A_caster_with_a_clear_ray_casts_at_once()
    {
        var t = Run600(pillar: false);
        Assert.True(t.FirstCastTick <= 22, $"the first cast came at tick {t.FirstCastTick}");
        Assert.DoesNotContain(t.Events, e => e.Type == "cast_los_withheld");
    }

    [Fact(DisplayName = "Caster LoS: the sight helper is exact and integer, and a layout with no obstacle is always clear")]
    public void The_sight_helper_reads_the_obstacle_boxes()
    {
        var a = new Vec3(16000, 0, 0);
        var b = new Vec3(4000, 0, 0);
        Assert.False(CastSight.Clear(new[] { Pillar }, a, b));
        Assert.True(CastSight.Clear(Array.Empty<Bounds>(), a, b));
        Assert.True(CastSight.Clear(new[] { Pillar }, new Vec3(16000, 0, 5000), new Vec3(16000, 0, -5000)));
        Assert.True(CastSight.Clear(new[] { Pillar }, new Vec3(16000, 0, 4001), new Vec3(4000, 0, 4001)));
    }

    [Fact(DisplayName = "Caster LoS: a player's cast is never withheld, only an enemy's")]
    public void A_players_cast_is_never_withheld_for_sight()
    {
        var caster = CombatCastTestKit.Make(1, ActorKind.Player, "warrior", ActorRole.Tank, x: 16000, maxHealth: 500);
        var target = CombatCastTestKit.Make(2, ActorKind.Enemy, "seed.elite", x: 4000, maxHealth: 500);
        var state = CombatCastTestKit.At(0, caster, target);
        var bolt = RunTestKit.Ability("bolt", new EffectOp[] { new DamageOp(1, "arcane") }, range: 20000);
        var ctx = new CastContext(new Dictionary<string, AbilityDef> { ["bolt"] = bolt }, new Dictionary<string, EffectDef>(), 40)
        {
            Sight = (_, _) => false,
        };
        Assert.Null(Casting.StartCast(state, caster, bolt, 2, null, 0, null, ctx));
    }
}
