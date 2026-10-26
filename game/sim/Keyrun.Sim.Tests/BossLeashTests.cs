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
/// R-BOSS-6 (dungeon loop, defer to WoW Mythic+; implements R-BOSS-2): a boss is bound to its arena. Three facts, each
/// red on a tree where the boss evades only through the generic leash and never through the no-living-seat rule:
/// (a) an engaged boss beyond its leash walks home and resets to full; (b) an engaged boss with no living seat evades and
/// resets to full; (c) a seat released at the entrance never draws an engaged boss out of its arena. Harness shape copied
/// from <c>Encounter/LeashEvadeTests.cs</c> (the Leash evade test): one pull, a swing script, a single-step tick loop.
/// </summary>
public sealed class BossLeashTests
{
    private const int Boss = 900;
    private const int Seat = 1;
    private const int SecondSeat = 2;
    private const long BossSpeed = 200;
    private const long BossLeashMm = 30000;
    private const long BossHomeX = 40000;
    private const long BossHealth = 1000;
    private const long EntranceX = 59000;

    private static readonly Bounds Hall = new(-40000, 60000, -6000, 6000);

    private static NavLayout Layout(IEnumerable<string> pullIds) => new(
        new[] { new NavBox("hall", Hall, true, null) },
        pullIds.Select(id => new NavTrigger(id, new Bounds(59500, 60000, 5500, 6000))).ToList(),
        new[] { new Vec3(EntranceX, 0, 0) });

    private static PullSpawn BossSpawnAt(long x, long? leash) =>
        RunTestKit.Spawn(Boss, "hollowWarden.boss", x, 0, BossHealth) with { SpeedMmPerTick = BossSpeed, AggroMm = 6000, LeashMm = leash };

    private static readonly IReadOnlyList<ScriptStep> Swing = new[] { new ScriptStep(StepTrigger.Loop, "swing") { Every = 20 } };

    private static Run Create(DungeonDef dungeon, IReadOnlyList<(int Id, long X, long Hp)> seats)
    {
        var layout = Layout(dungeon.Pulls.Select(p => p.Id));
        var swing = RunTestKit.Ability("swing", new EffectOp[] { new DamageOp(1, "physical") }, range: 2000);
        var scripts = dungeon.Pulls.SelectMany(p => p.Spawns).ToDictionary(s => s.ActorId, _ => Swing);
        var content = RunTestKit.Content(new Dictionary<string, AbilityDef>(StringComparer.Ordinal) { ["swing"] = swing }, bounds: layout.Extent) with
        {
            Layout = layout,
            EnemyScripts = scripts,
        };
        var members = seats.Select(s => RunTestKit.Member(s.Id, "warden", ActorRole.Tank, s.X, 0, s.Hp, 100)).ToArray();
        return RunRoot.CreateRun(1, members, dungeon, content);
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

    private static int LivingSeats(Run run) => run.State.Actors.Values.Count(a => a.Kind == ActorKind.Player && a.Alive);

    private static string Measured(Run run, Vec3 home, long leash) =>
        $"boss x {run.State.Actors[Boss].Pos.X}, home x {home.X}, leash {leash}, hp {run.State.Actors[Boss].Health}, " +
        $"max hp {run.State.Actors[Boss].MaxHealth}, living seats {LivingSeats(run)}";

    [Fact(DisplayName = "R-BOSS-6 (a): an engaged boss farther than its leash from home evades, walks home and resets to full")]
    public void Boss_engaged_farther_than_its_leash_from_home_evades_and_resets_to_full()
    {
        var dungeon = RunTestKit.OnePull("hollow", "p1", BossSpawnAt(BossHomeX, BossLeashMm));
        var run = Create(dungeon, new[] { (Seat, 36000L, 4000L) });
        run.State.Actors[Boss].Health = 400;
        var home = run.State.Actors[Boss].Pos;
        var (final, events) = Steps(run, 600, r =>
        {
            if (r.State.Tick == 1)
            {
                r.State.Actors[Seat].Pos = new Vec3(-30000, 0, 0);
            }
        });
        var boss = final.State.Actors[Boss];
        var msg = "R-BOSS-6 (a) red: " + Measured(final, home, BossLeashMm) + ", idle " + boss.Idle + ", evading " + boss.Evading +
            ", evade events " + events.Count(e => e.Type == "pack_evade") + ", reset events " + events.Count(e => e.Type == "pack_reset");
        Assert.True(boss.Pos == home && boss.Health == boss.MaxHealth && boss.Idle && !boss.Evading, msg);
    }

    [Fact(DisplayName = "R-BOSS-6 (b): an engaged boss with no living seat evades and resets to full at home")]
    public void Boss_with_no_living_seat_resets_to_full()
    {
        // Leash null = off, the content's leashMm 0 (run.json hollowWarden.boss); a literal 0 would evade every off-home step.
        var dungeon = RunTestKit.OnePull("hollow", "p1", BossSpawnAt(BossHomeX, null));
        // The lone seat has 1 health: the boss's first swing kills it in the arena, through the sim's own damage path.
        var run = Create(dungeon, new[] { (Seat, 36000L, 1L) });
        run.State.Actors[Boss].Health = 400;
        var home = run.State.Actors[Boss].Pos;
        var (final, events) = Steps(run, 300);
        var boss = final.State.Actors[Boss];
        var msg = "R-BOSS-6 (b) red: " + Measured(final, home, 0) + ", idle " + boss.Idle + ", evading " + boss.Evading +
            ", evade events " + events.Count(e => e.Type == "pack_evade") + ", reset events " + events.Count(e => e.Type == "pack_reset");
        Assert.True(boss.Pos == home && boss.Health == boss.MaxHealth && boss.Idle && !boss.Evading, msg);
    }

    [Fact(DisplayName = "R-BOSS-6 (c): a seat released at the entrance does not draw an engaged boss out of its arena")]
    public void Released_seat_at_the_entrance_does_not_draw_the_boss()
    {
        var dungeon = RunTestKit.OnePull("hollow", "p1", BossSpawnAt(BossHomeX, BossLeashMm));
        var run = Create(dungeon, new[] { (Seat, 36000L, 4000L), (SecondSeat, 36500L, 4000L) });
        var home = run.State.Actors[Boss].Pos;
        var farthest = 0L;
        var (final, _) = Steps(run, 200, r =>
        {
            if (r.State.Tick == 1)
            {
                r.State.Actors[SecondSeat].Pos = new Vec3(EntranceX, 0, 0);
            }
            farthest = Math.Max(farthest, Dist(r.State.Actors[Boss].Pos, home));
        });
        var msg = "R-BOSS-6 (c) red: farthest boss distance from home " + farthest + ", " + Measured(final, home, BossLeashMm);
        Assert.True(farthest <= BossLeashMm + BossSpeed, msg);
    }
}
