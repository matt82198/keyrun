using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Xunit;

namespace Keyrun.Sim.Tests;

/// <summary>
/// Ports of tests/sim/encounter/telegraphs.test.ts and telegraph-facing.test.ts (geometry, resolution and
/// facing), the shared facing table (sim/combat/targeting.ts faceDir), the registry lifecycle that
/// sim/encounter/index.ts stepEncounter runs, and registration of every telegraph event in the oracle traces.
/// </summary>
public sealed class TelegraphTests
{
    private static readonly Vec3 Origin = Vec3.Zero;

    private static ActorState Player(int id, Vec3 pos) => new()
    {
        Id = id, Kind = ActorKind.Player, ClassId = "warden", Role = ActorRole.Tank, Pos = pos, Health = 100, MaxHealth = 100,
    };

    // ---- facing table ----

    [Fact]
    public void FaceDir_axes_and_rounding_match_targeting_ts()
    {
        Assert.Equal(new Dir2(4096, 0), Facing.FaceDir(0));
        Assert.Equal(new Dir2(0, 4096), Facing.FaceDir(256));
        Assert.Equal(new Dir2(-4096, 0), Facing.FaceDir(512));
        Assert.Equal(new Dir2(0, -4096), Facing.FaceDir(768));
        Assert.Equal(new Dir2(4091, 201), Facing.FaceDir(4));      // Math.round(0.5) = 1
        Assert.Equal(new Dir2(4096, 0), Facing.FaceDir(3));        // Math.round(0.375) = 0
        Assert.Equal(new Dir2(4096, 0), Facing.FaceDir(-4));       // Math.round(-0.5) = -0 -> index 0
        Assert.Equal(new Dir2(4091, -201), Facing.FaceDir(-5));    // Math.round(-0.625) = -1 -> index 127
        Assert.Equal(new Dir2(4096, 0), Facing.FaceDir(1020));     // Math.round(127.5) = 128 -> wraps to 0
        Assert.Equal(new Dir2(4096, 0), Facing.FaceDir(1024 * 3));
    }

    [Fact]
    public void Facing_index_of_a_half_angle_rounds_half_up()
    {
        // targeting.ts inCone passes angle1024 / 2, which can be x.5: tableIndex(100.5) = round(12.5625) = 13.
        Assert.Equal(13, Facing.TableIndexOfHalf(201));
        Assert.Equal(13, Facing.TableIndexOfHalf(200)); // 100 / 8 = 12.5 -> 13
        Assert.Equal(12, Facing.TableIndexOfHalf(199)); // 99.5 / 8 = 12.4375 -> 12
    }

    // ---- telegraphs.test.ts: integer geometry ----

    [Fact]
    public void Point_in_circle_includes_the_rim()
    {
        Assert.True(Telegraphs.PointInCircle(Origin, 5000, new Vec3(3000, 0, 4000)));
        Assert.False(Telegraphs.PointInCircle(Origin, 5000, new Vec3(3000, 0, 4001)));
    }

    [Fact]
    public void Point_in_cone_front_is_hit_and_behind_is_not()
    {
        Assert.True(Telegraphs.PointInCone(Origin, 0, 200, 6000, new Vec3(3000, 0, 0)));
        Assert.False(Telegraphs.PointInCone(Origin, 0, 200, 6000, new Vec3(-3000, 0, 0)));
    }

    [Fact]
    public void Point_in_cone_respects_the_facing_yaw()
    {
        Assert.True(Telegraphs.PointInCone(Origin, 256, 200, 6000, new Vec3(0, 0, 3000)));
        Assert.False(Telegraphs.PointInCone(Origin, 256, 40, 6000, new Vec3(3000, 0, 0)));
    }

    [Fact]
    public void Point_in_cone_outside_the_radius_is_never_a_hit()
    {
        Assert.False(Telegraphs.PointInCone(Origin, 0, 200, 2000, new Vec3(3000, 0, 0)));
    }

    [Fact]
    public void Point_in_cone_at_the_origin_is_a_hit()
    {
        Assert.True(Telegraphs.PointInCone(Origin, 0, 10, 100, Origin));
    }

    [Fact]
    public void Cone_cos_half_uses_round_half_up_on_the_half_angle()
    {
        Assert.Equal(3290, Telegraphs.CosHalf(200)); // half 100 -> 12.5 -> 13
        Assert.Equal(3406, Telegraphs.CosHalf(199)); // half 99.5 -> 12.4375 -> 12
        Assert.Equal(4096, Telegraphs.CosHalf(-50)); // clamped to 0
        Assert.Equal(0, Telegraphs.CosHalf(5000));   // clamped to 511: half 255.5, 255.5 / 8 = 31.9375 rounds to 32 -> 0
    }

    [Fact]
    public void Cone_edge_follows_the_squared_dot_test_exactly()
    {
        // yaw 0, angle 200: cosHalf 3290. A point at (x, z) is in iff (4096 x)^2 >= (x^2 + z^2) * 3290^2.
        for (long z = 0; z <= 3000; z += 1)
        {
            var p = new Vec3(3000, 0, z);
            long dot = 4096 * 3000;
            var expected = dot * dot >= ((3000L * 3000) + (z * z)) * 3290L * 3290L;
            Assert.Equal(expected, Telegraphs.PointInCone(Origin, 0, 200, 10000, p));
        }
    }

    [Fact]
    public void Point_in_line_width_and_length()
    {
        Assert.True(Telegraphs.PointInLine(Origin, 0, 5000, 1000, new Vec3(2500, 0, 0)));
        Assert.True(Telegraphs.PointInLine(Origin, 0, 5000, 1000, new Vec3(2500, 0, 400)));
        Assert.False(Telegraphs.PointInLine(Origin, 0, 5000, 1000, new Vec3(2500, 0, 600)));
        Assert.True(Telegraphs.PointInLine(Origin, 0, 5000, 1000, new Vec3(2500, 0, -500)));
        Assert.False(Telegraphs.PointInLine(Origin, 0, 5000, 1000, new Vec3(5001, 0, 0)));
        Assert.False(Telegraphs.PointInLine(Origin, 0, 5000, 1000, new Vec3(-100, 0, 0)));
    }

    // ---- telegraphs.test.ts: resolveTelegraphs ----

    private static SimState StateAt(int tick, params ActorState[] actors)
    {
        var s = SimState.Create(1);
        s.Tick = tick;
        foreach (var a in actors)
        {
            s.Actors[a.Id] = a;
        }
        return s;
    }

    [Fact]
    public void Resolves_a_due_cone_hitting_the_front_sparing_behind_and_the_source()
    {
        var tg = new Telegraph(1, 99, "boss_cleave", "cone", Origin, 0, 10) { Angle1024 = 200, Radius = 6000 };
        var state = StateAt(10, Player(3, new Vec3(-3000, 0, 0)), Player(99, Origin), Player(2, new Vec3(3000, 0, 0)));
        var hits = Telegraphs.Resolve(state, new[] { tg });
        var hit = Assert.Single(hits);
        Assert.Equal(1, hit.TelegraphId);
        Assert.Equal(new[] { 2 }, hit.HitActorIds);
    }

    [Fact]
    public void Does_not_resolve_before_resolves_tick()
    {
        var tg = new Telegraph(1, 99, "boss_cleave", "circle", Origin, 0, 10) { Radius = 6000 };
        Assert.Empty(Telegraphs.Resolve(StateAt(9, Player(2, Origin)), new[] { tg }));
    }

    [Fact]
    public void Resolution_skips_the_dead_lists_hits_ascending_and_reports_empty_hits()
    {
        var dead = Player(5, Origin);
        dead.Alive = false;
        var tgs = new[]
        {
            new Telegraph(7, 99, "a", "circle", Origin, 0, 3) { Radius = 1000 },
            new Telegraph(8, 99, "b", "circle", new Vec3(50000, 0, 0), 0, 2) { Radius = 10 },
            new Telegraph(9, 99, "c", "circle", Origin, 0, 4) { Radius = 1000 },
        };
        var state = StateAt(3, Player(4, new Vec3(10, 0, 0)), dead, Player(1, new Vec3(0, 0, 10)));
        var hits = Telegraphs.Resolve(state, tgs);
        Assert.Equal(new[] { 7, 8 }, hits.Select(h => h.TelegraphId));
        Assert.Equal(new[] { 1, 4 }, hits[0].HitActorIds);
        Assert.Empty(hits[1].HitActorIds);
    }

    [Fact]
    public void An_unknown_shape_resolves_as_a_line_and_missing_dimensions_default_to_zero()
    {
        // telegraphs.ts: any shape other than circle/cone takes the line branch; radius ?? 0 etc.
        var odd = new Telegraph(1, 99, "x", "ring", Origin, 0, 0) { Length = 1000, Width = 100 };
        var bare = new Telegraph(2, 99, "x", "circle", Origin, 0, 0);
        var state = StateAt(0, Player(1, new Vec3(500, 0, 0)), Player(2, Origin));
        var hits = Telegraphs.Resolve(state, new[] { odd, bare });
        Assert.Equal(new[] { 1, 2 }, hits[0].HitActorIds);
        Assert.Equal(new[] { 2 }, hits[1].HitActorIds);
    }

    // ---- lifecycle (sim/encounter/index.ts stepEncounter's telegraph part) ----

    private static SimEvent TelegraphEvent(int tick, int actorId, string shape, Vec3 origin, int yaw, int resolvesTick, params (string, JsValue)[] dims)
    {
        var fields = new List<(string, JsValue)>
        {
            ("actorId", JsValue.Int(actorId)), ("cast", JsValue.Str("boss_cleave")), ("shape", JsValue.Str(shape)),
            ("origin", JsValue.Vec(origin)), ("yaw", JsValue.Int(yaw)), ("resolvesTick", JsValue.Int(resolvesTick)),
            ("leadTicks", JsValue.Int(resolvesTick - tick)),
        };
        fields.AddRange(dims);
        return new SimEvent(tick, "telegraph", fields.ToArray());
    }

    [Fact]
    public void Advance_mints_ids_resolves_due_telegraphs_emits_hits_and_keeps_the_rest()
    {
        var state = StateAt(10, Player(2, new Vec3(1000, 0, 0)));
        var pending = new Telegraph(1, 99, "old", "circle", Origin, 0, 10) { Radius = 5000 };
        var fresh = new[]
        {
            TelegraphEvent(10, 99, "circle", Origin, 0, 10, ("radius", JsValue.Int(10))),
            TelegraphEvent(10, 99, "cone", Origin, 0, 50, ("angle1024", JsValue.Int(200)), ("radius", JsValue.Int(6000))),
        };
        var emitted = new List<SimEvent>();
        var result = Telegraphs.Advance(state, new[] { pending }, fresh, 2, emitted.Add);
        Assert.Equal(4, result.NextTelegraphId);
        Assert.Equal(new[] { 3 }, result.Telegraphs.Select(t => t.Id));
        Assert.Equal(50, result.Telegraphs[0].ResolvesTick);
        Assert.Equal(200, result.Telegraphs[0].Angle1024);
        Assert.Equal(new[]
        {
            "{\"actorIds\":[2],\"telegraphId\":1,\"tick\":10,\"type\":\"telegraph_hit\"}",
            "{\"actorIds\":[],\"telegraphId\":2,\"tick\":10,\"type\":\"telegraph_hit\"}",
        }, emitted.Select(e => e.ToCanonicalJson()));
    }

    [Fact]
    public void Advance_applies_the_drift_hook_before_resolving()
    {
        var state = StateAt(5, Player(2, new Vec3(3000, 0, 0)));
        var tg = new Telegraph(1, 99, "x", "circle", Origin, 0, 5) { Radius = 500 };
        var emitted = new List<SimEvent>();
        Telegraphs.Advance(state, new[] { tg }, Array.Empty<SimEvent>(), 2, emitted.Add,
            drift: ts => ts.Select(t => t with { Origin = new Vec3(t.Origin.X + 3000, t.Origin.Y, t.Origin.Z) }).ToList());
        Assert.Equal("{\"actorIds\":[2],\"telegraphId\":1,\"tick\":5,\"type\":\"telegraph_hit\"}", Assert.Single(emitted).ToCanonicalJson());
    }

    [Fact]
    public void Register_copies_event_fields_and_omits_absent_dimensions()
    {
        var ev = TelegraphEvent(60, 1, "line", new Vec3(2000, 0, -1000), 37, 100, ("length", JsValue.Int(8000)), ("width", JsValue.Int(1000)));
        var tg = Telegraphs.Register(ev, 9);
        Assert.Equal(new Telegraph(9, 1, "boss_cleave", "line", new Vec3(2000, 0, -1000), 37, 100) { Length = 8000, Width = 1000 }, tg);
        Assert.Null(tg.Radius);
        Assert.Null(tg.Angle1024);
    }

    [Fact]
    public void Register_fails_closed_on_a_malformed_event()
    {
        var ev = new SimEvent(1, "telegraph", ("actorId", JsValue.Int(1)), ("cast", JsValue.Str("x")));
        Assert.Throws<ArgumentException>(() => Telegraphs.Register(ev, 1));
    }

    [Fact]
    public void Every_telegraph_event_in_the_oracle_traces_registers()
    {
        var count = 0;
        var shapes = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var name in OracleFixtures.TraceNames)
        {
            var trace = OracleFixtures.LoadTrace(name);
            foreach (var line in trace.RecordLines)
            {
                var rec = JsonDocument.Parse(line).RootElement;
                if (!rec.TryGetProperty("e", out var e))
                {
                    continue;
                }
                foreach (var el in e.EnumerateArray())
                {
                    var ev = SimEvent.FromJson(el);
                    if (ev.Type != "telegraph")
                    {
                        continue;
                    }
                    var tg = Telegraphs.Register(ev, count + 1);
                    Assert.Equal(((JsInt)ev.Get("resolvesTick")!).Value, tg.ResolvesTick);
                    Assert.Equal(((JsInt)ev.Get("yaw")!).Value, tg.Yaw);
                    Assert.Equal(((JsString)ev.Get("cast")!).Value, tg.AbilityId);
                    Assert.Equal(ev.Get("radius") is JsInt r ? (long?)r.Value : null, tg.Radius);
                    shapes.Add(tg.Shape);
                    count++;
                }
            }
        }
        Assert.Equal(46, count); // INTEGRATE-PLAYABLE #325 re-fold re-record: was 47 (k10-s7 17 -> 16, the run ends at 2071 before the 2126 cleave). LOCK-TELEGRAPH re-record: was 46 (k10-s7 16 -> 17, the Lock circle). DEAD-CASTER-TELEGRAPH re-record: was 42 (k2-s7 11 -> 13, k2-s11 11 -> 13); scripted-pull 6 + bots-hv k2-s7 11, k2-s11 11, k10-s7 14 (KF3-SEED-ENGAGE seed.lash; 37 after SLAM-TELEGRAPH B1, 34 after D14)
        Assert.Subset(new SortedSet<string>(new[] { "circle", "cone", "line" }), shapes);
    }

    // ---- telegraph-facing.test.ts ----

    private static readonly Vec3 BossPos = new(2000, 0, -1000);

    public static IEnumerable<object[]> Offsets() => new[]
    {
        new object[] { 4000L, 0L }, new object[] { 0L, 4000L }, new object[] { 3000L, 1000L },
        new object[] { -2000L, 3000L }, new object[] { -3000L, -2500L },
    };

    private static (Vec3 Front, Vec3 Side) FrontAndSide(int yaw)
    {
        var f = Facing.FaceDir(yaw);
        const long d = 3000;
        // The TS test uses Math.round((f * d) / 4096) here (test-side geometry, not sim code).
        long R(long n) => IntMath.RoundHalfUp(n, 4096);
        return (new Vec3(BossPos.X + R(f.X * d), 0, BossPos.Z + R(f.Z * d)),
                new Vec3(BossPos.X + R(-f.Z * d), 0, BossPos.Z + R(f.X * d)));
    }

    [Theory]
    [MemberData(nameof(Offsets))]
    public void Cone_toward_a_target_covers_the_front_and_spares_the_side(long ox, long oz)
    {
        var target = new Vec3(BossPos.X + ox, 0, BossPos.Z + oz);
        var yaw = Fixed.YawBetween(BossPos, target);
        var (front, side) = FrontAndSide(yaw);
        Assert.True(Telegraphs.PointInCone(BossPos, yaw, 170, 6000, front));
        Assert.False(Telegraphs.PointInCone(BossPos, yaw, 170, 6000, side));
        Assert.True(Telegraphs.PointInCone(BossPos, yaw, 170, 6000, target));
    }

    [Theory]
    [MemberData(nameof(Offsets))]
    public void Line_toward_a_target_covers_the_front_and_spares_the_side(long ox, long oz)
    {
        var target = new Vec3(BossPos.X + ox, 0, BossPos.Z + oz);
        var yaw = Fixed.YawBetween(BossPos, target);
        var (front, side) = FrontAndSide(yaw);
        Assert.True(Telegraphs.PointInLine(BossPos, yaw, 8000, 1000, front));
        Assert.False(Telegraphs.PointInLine(BossPos, yaw, 8000, 1000, side));
        Assert.True(Telegraphs.PointInLine(BossPos, yaw, 8000, 1000, target));
    }
}
