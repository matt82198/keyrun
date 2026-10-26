using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Nav;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// BIBLE s3 "what the player sees drawn on the floor is where the damage lands, at resolve time". A scripted cone is drawn
/// one lead before its cast starts (Scripts.cs:636-646 for a TS-replayed step, Scripts.cs:601-611 for a queued one), and
/// a caster with no sight of its target walks and turns during that lead (Chase.cs:80-102). The drawn cone is anchored at
/// the draw-tick pose; the damage must be resolved against the pose the cone was drawn for. So a player inside the drawn
/// cone but outside the cast-start facing must be hit, and a player inside the cast-start facing but outside the drawn
/// cone must not be.
///
/// Scenario (real run path: RunRoot.CreateRun, a scripted cone step, and Chase walking the caster round a pillar): the
/// caster stands east of a pillar. Chase walks it round the pillar toward the nearest player, the chased member, behind
/// the pillar; it stops once it has sight of that member. Probe is a second member placed where the cone as drawn covers
/// it but the cone at cast start does not. Probe is placed in a calibration pass: the same run with the probe far away,
/// read for the drawn and cast-start poses, since the probe is never the nearest player and so never changes the walk.
/// </summary>
public sealed class TelegraphLeadAimTests
{
    private const int Caster = 101;
    private const int Chased = 1;
    private const int Probe = 2;
    private const string Cast = "boss.coneSweep";
    private const string Hex = "boss.hexSingle";
    private const int Lead = 120; // drawn at tick 80, while the caster is still walking round the pillar
    private const int Period = 200; // the cone fires on tick 200, so it is first drawn on tick 80
    private const int CastTicks = 10;
    private const long ConeRadius = 20000;
    private const int ConeAngle1024 = 256;
    private const int MaxTicks = 600;
    private const long ChasedX = 4000;
    private static readonly Vec3 CasterStart = new(16000, 0, 0);
    private static readonly Vec3 FarProbe = new(29000, 0, 0);

    private static readonly Bounds Hall = new(-20000, 40000, -20000, 20000);
    private static readonly Bounds Pillar = new(8000, 12000, -4000, 4000);
    private static NavLayout Layout() => new(
        new[] { new NavBox("hall", Hall, true, null) },
        new[] { new NavTrigger("p1", new Bounds(39500, 40000, 5500, 6000)) },
        new[] { new Vec3(39000, 0, 0) })
    {
        Obstacles = new[] { Pillar },
    };

    private static Run Create(Vec3 probe, bool tsReplay)
    {
        var ability = RunTestKit.Ability(
            Cast,
            new EffectOp[] { new DamageOp(5, "physical") },
            shape: new AbilityShape(ShapeKind.Cone, ConeRadius, ConeAngle1024)) with { CastTicks = CastTicks };
        var step = ScriptStep.Loop(Period, Cast) with
        {
            TsCastReplace = tsReplay,
            Telegraph = new TelegraphSpec("cone", Lead) { Radius = ConeRadius, Angle1024 = ConeAngle1024 },
        };
        // Chase walks a caster only while it holds a target-shaped cast (Chase.CastsAtTarget), so the caster also carries
        // a zero-effect single-target cast. It fires once, far past the run under test.
        var hex = RunTestKit.Ability(Hex, Array.Empty<EffectOp>());
        var hexStep = ScriptStep.Loop(100000, Hex);
        var layout = Layout();
        var spawn = RunTestKit.Spawn(Caster, "dummy", CasterStart.X, 0, 1000) with { SpeedMmPerTick = 50, AggroMm = 20000 };
        var dungeon = RunTestKit.OnePull("lead-aim", "p1", spawn);
        var content = RunTestKit.Content(
            new Dictionary<string, AbilityDef>(StringComparer.Ordinal) { [Cast] = ability, [Hex] = hex },
            bounds: layout.Extent) with
        {
            Layout = layout,
            EnemyScripts = new Dictionary<int, IReadOnlyList<ScriptStep>> { [Caster] = new[] { step, hexStep } },
        };
        var members = new[]
        {
            RunTestKit.Member(Chased, "warden", ActorRole.Tank, ChasedX, 0, 1000, 100),
            RunTestKit.Member(Probe, "warden", ActorRole.Damage, probe.X, probe.Z, 1000, 100),
        };
        return RunRoot.CreateRun(1, members, dungeon, content);
    }

    private sealed record Draw(Vec3 Origin, int Yaw, int ResolvesTick);

    private sealed record Trace(Draw Drawn, Vec3 StartPos, int StartYaw, int StartTick, long ChasedHealth, long ProbeHealth);

    private static Vec3 VecOf(JsObject o) => new(((JsInt)o.Get("x")!).Value, ((JsInt)o.Get("y")!).Value, ((JsInt)o.Get("z")!).Value);

    /// <summary>Plays the run: the first cone telegraph as drawn, the caster's pose on its cast-start tick, and both members' health at cast completion.</summary>
    private static Trace Play(Vec3 probe, bool tsReplay)
    {
        var run = Create(probe, tsReplay);
        Draw? drawn = null;
        (Vec3 Pos, int Yaw, int Tick)? start = null;
        var completed = false;
        for (var i = 0; i < MaxTicks && !completed; i++)
        {
            var wasCasting = run.State.Actors[Caster].CastId is not null;
            var stepped = RunRoot.StepRun(run, Array.Empty<Command>());
            run = stepped.Run;
            foreach (var e in stepped.Events)
            {
                if (e.Type == "telegraph" && drawn is null && e.Get("cast") is JsString cast && cast.Value == Cast)
                {
                    drawn = new Draw(VecOf((JsObject)e.Get("origin")!), (int)((JsInt)e.Get("yaw")!).Value, (int)((JsInt)e.Get("resolvesTick")!).Value);
                }
                // The only cast_complete in this run is the caster's cone (the hex never completes before the run ends).
                completed |= e.Type == "cast_complete";
            }
            var now = run.State.Actors[Caster];
            if (!wasCasting && now.CastId is not null && drawn is not null && start is null)
            {
                start = (now.Pos, now.Yaw, run.State.Tick);
            }
        }
        var d = drawn ?? throw new InvalidOperationException("no cone telegraph was drawn for the scripted caster");
        var s = start ?? throw new InvalidOperationException("the scripted cone cast never started after its telegraph was drawn");
        Assert.True(completed, "the scripted cone cast never completed; the case proves nothing");
        return new Trace(d, s.Pos, s.Yaw, s.Tick, run.State.Actors[Chased].Health, run.State.Actors[Probe].Health);
    }

    /// <summary>
    /// A probe spot the drawn cone covers and the cast-start cone does not, and that stays farther from the caster than the
    /// chased member (so the caster's nearest-player pick, and therefore its walk, is unchanged by the probe).
    /// </summary>
    private static Vec3 ProbeSpot(Trace calibration)
    {
        var chased = new Vec3(ChasedX, 0, 0);
        // A polar grid around the drawn origin: every spot at each radius, every 1/32 turn (yaw units are 1024 per turn).
        for (var radius = 5000L; radius <= 15000; radius += 500)
        {
            for (var k = 0; k < 32; k++)
            {
                var angle = 2 * Math.PI * k / 32;
                var p = new Vec3(
                    calibration.Drawn.Origin.X + (long)Math.Round(radius * Math.Cos(angle)),
                    0,
                    calibration.Drawn.Origin.Z + (long)Math.Round(radius * Math.Sin(angle)));
                var inHall = p.X > -19000 && p.X < 39000 && p.Z > -19000 && p.Z < 19000;
                var inDrawn = Telegraphs.PointInCone(calibration.Drawn.Origin, calibration.Drawn.Yaw, ConeAngle1024, ConeRadius, p);
                var inStart = Telegraphs.PointInCone(calibration.StartPos, calibration.StartYaw, ConeAngle1024, ConeRadius, p);
                var farther = Dist(calibration.Drawn.Origin, p) > Dist(calibration.Drawn.Origin, chased) + 1000
                    && Dist(calibration.StartPos, p) > Dist(calibration.StartPos, chased) + 1000;
                if (inHall && inDrawn && !inStart && farther)
                {
                    return p;
                }
            }
        }
        throw new InvalidOperationException(
            "no probe spot is inside the drawn cone, outside the cast-start cone, and farther than the chased member " +
            $"(drawn {calibration.Drawn.Origin} yaw {calibration.Drawn.Yaw}; cast start {calibration.StartPos} yaw {calibration.StartYaw} on tick {calibration.StartTick}; " +
            $"chased {calibration.ChasedHealth}, probe {calibration.ProbeHealth})");
    }

    private static double Dist(Vec3 a, Vec3 b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Z - b.Z, 2));

    [Theory(DisplayName = "Lead aim: a cone drawn one lead ahead resolves on the pose it was drawn for, not the caster's cast-start facing")]
    [InlineData(false)]
    [InlineData(true)]
    public void A_cone_drawn_one_lead_ahead_resolves_on_the_drawn_pose(bool tsReplay)
    {
        var calibration = Play(FarProbe, tsReplay);
        var probe = ProbeSpot(calibration);
        var t = Play(probe, tsReplay);
        var drawn = t.Drawn;
        var chased = new Vec3(ChasedX, 0, 0);

        Assert.True(
            t.StartPos != drawn.Origin || t.StartYaw != drawn.Yaw,
            $"precondition: the caster must walk or turn during the lead (drawn at {drawn.Origin} yaw {drawn.Yaw}, " +
            $"cast started at {t.StartPos} yaw {t.StartYaw} on tick {t.StartTick})");
        Assert.True(Telegraphs.PointInCone(drawn.Origin, drawn.Yaw, ConeAngle1024, ConeRadius, chased) == false,
            "precondition: the chased member must be outside the cone as drawn");
        Assert.True(Telegraphs.PointInCone(t.StartPos, t.StartYaw, ConeAngle1024, ConeRadius, chased),
            "precondition: the chased member must be inside the cone at the cast-start pose");
        Assert.True(Telegraphs.PointInCone(drawn.Origin, drawn.Yaw, ConeAngle1024, ConeRadius, probe),
            "precondition: the probe must be inside the cone as drawn");
        Assert.False(Telegraphs.PointInCone(t.StartPos, t.StartYaw, ConeAngle1024, ConeRadius, probe),
            "precondition: the probe must be outside the cone at the cast-start pose");

        var failures = new List<string>();
        if (t.ProbeHealth >= 1000)
        {
            failures.Add($"Expected the probe (inside the cone as drawn) to be hit, Actual health {t.ProbeHealth}");
        }
        if (t.ChasedHealth < 1000)
        {
            failures.Add($"Expected the chased member (inside the cone only at cast start, outside the cone as drawn) NOT to be hit, Actual health {t.ChasedHealth}");
        }
        Assert.True(failures.Count == 0, string.Join(" | ", failures));
    }
}
