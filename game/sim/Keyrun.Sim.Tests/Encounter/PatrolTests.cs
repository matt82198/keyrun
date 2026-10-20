using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Keyrun.Tools.Replay;
using Xunit;
using Xunit.Abstractions;

namespace Keyrun.Sim.Tests.Encounter;

/// <summary>
/// MPLUS-PATROLS (Matt 2026-10-20, verbatim: "honestly just lean into m+ design heavily while in the dungeon"; BIBLE
/// section 9.1 "it does not move unless it is a patrol"). One patrol pack walks a fixed waypoint loop along corridor-2,
/// between the Choir and the Pair, read from the shipped game/content/data/layout.json <c>patrols</c>. Its position is a
/// pure function of the tick (integer mm, no RNG); it engages on the same aggro rule as a prespawned idle pack (a living
/// party member within its radius, inclusive, XZ), so it engages a party standing on its loop, joins a fight it walks
/// into (a WoW accidental double pull), and never engages a party that stays off its loop. Its members are ordinary
/// enemy actors, so a dead patrol member banks forces like any pack.
/// </summary>
public sealed class PatrolTests
{
    private const long Seed = 1;
    private const int FirstPatrolActorId = 700;

    private readonly ITestOutputHelper _out;

    /// <summary>Creates the test with xUnit's output sink (engage ticks are printed for the PR).</summary>
    /// <param name="output">The sink.</param>
    public PatrolTests(ITestOutputHelper output) => _out = output;

    private static string DataPath(string file)
    {
        // Fixtures/oracle -> Keyrun.Sim.Tests -> sim -> game, then content/data/<file>.
        var game = Directory.GetParent(OracleFixtures.Directory)!.Parent!.Parent!.Parent!.FullName;
        return Path.Combine(game, "content", "data", file);
    }

    private static long L(JsObject o, string key) => ((JsInt)o.Get(key)!).Value;

    // The shipped patrol, as Keyrun.Bots NavLayouts.ToSimPatrols builds it: one actor per enemy count, ids in order, each
    // member's spawn the offset from the patrol point. Health and speed come from the k2 setup's spawn of the same enemy.
    private static PatrolDef ShippedPatrol(RunSetup setup)
    {
        var root = (JsObject)JsValue.Parse(File.ReadAllText(DataPath("layout.json")));
        var patrol = Assert.Single(((JsArray)root.Get("patrols")!).Items.Cast<JsObject>());
        var templates = setup.Dungeon.Pulls.SelectMany(p => p.Spawns).GroupBy(s => s.ClassId).ToDictionary(g => g.Key, g => g.First());
        var nextId = FirstPatrolActorId;
        var spawns = new List<PullSpawn>();
        foreach (var e in ((JsArray)patrol.Get("enemies")!).Items.Cast<JsObject>())
        {
            var t = templates[((JsString)e.Get("enemyId")!).Value];
            var at = (JsObject)e.Get("spawn")!;
            for (var i = 0; i < L(e, "count"); i++)
            {
                spawns.Add(t with { ActorId = nextId++, Pos = new Vec3(L(at, "x"), 0, L(at, "z")) });
            }
        }
        var waypoints = ((JsArray)patrol.Get("waypoints")!).Items.Cast<JsObject>().Select(p => new Vec3(L(p, "x"), 0, L(p, "z"))).ToList();
        var def = new PatrolDef(((JsString)patrol.Get("id")!).Value, spawns, waypoints, L(patrol, "speedMmPerTick"));
        return patrol.Get("aggroMm") is JsInt r ? def with { AggroMm = r.Value } : def;
    }

    // The k2 Choir placed in the real choir-room (origin plus content offset) plus the shipped patrol; the three seats at
    // `seatAt`, standing still (no commands) for the whole test.
    private static (Run Run, PatrolDef Patrol) Build(Func<int, Vec3> seatAt)
    {
        var (nav, rooms) = SeedEngageTests.Layout();
        var setup = RunSetup.Load(OracleFixtures.PathOf("hollow-vault-k2.setup.json"), Seed);
        var origin = rooms.Single(r => r.Pull == "choir").Origin!.Value;
        var choir = setup.Dungeon.Pulls.Single(p => p.Id == "choir");
        var placed = choir with { Spawns = choir.Spawns.Select(s => s with { Pos = new Vec3(origin.X + s.Pos.X, s.Pos.Y, origin.Z + s.Pos.Z) }).ToList() };
        var patrol = ShippedPatrol(setup);
        var dungeon = setup.Dungeon with { Pulls = new[] { placed }, Patrols = new[] { patrol } };
        var party = setup.Party.Take(3).Select((m, i) => m with { Pos = seatAt(i), Yaw = 0 }).ToList();
        var content = setup.Content with { Layout = nav, Bounds = nav.Extent };
        return (RunRoot.CreateRun(Seed, party, dungeon, content, setup.Options), patrol);
    }

    private static (Run Run, List<SimEvent> Events, List<Vec3> Path) Play(Run run, int ticks, int watchActorId)
    {
        var events = new List<SimEvent>();
        var path = new List<Vec3> { run.State.Actors[watchActorId].Pos };
        for (var i = 0; i < ticks; i++)
        {
            var stepped = RunRoot.StepRun(run, Array.Empty<Command>());
            events.AddRange(stepped.Events);
            run = stepped.Run;
            path.Add(run.State.Actors[watchActorId].Pos);
        }
        return (run, events, path);
    }

    [Fact(DisplayName = "Patrol: the shipped corridor-2 patrol's point at tick N is a fixed integer function of N (out 6000 mm west and back at 50 mm/tick, a 240-tick loop)")]
    public void Patrol_point_at_tick_n_is_deterministic()
    {
        var (_, patrol) = Build(i => new Vec3(4000, 0, (i - 1) * 1200));
        Assert.Equal("corridor-patrol", patrol.Id);
        Assert.Equal(Pulls.DefaultAggroMm, patrol.AggroMm);
        Assert.Equal(12000, Patrols.LoopMm(patrol));
        var expected = new (int Tick, long X)[]
        {
            (0, 32500), (1, 32450), (60, 29500), (119, 26550), (120, 26500), (121, 26550), (180, 29500), (239, 32450), (240, 32500), (250, 32000), (2400 + 61, 29450),
        };
        foreach (var (tick, x) in expected)
        {
            Assert.Equal(new Vec3(x, 0, 0), Patrols.PointAt(patrol, tick));
        }
    }

    [Fact(DisplayName = "Patrol: in a run its idle members walk the loop tick by tick (position at tick N = point at N plus the member's offset), the same in two independent runs")]
    public void Patrol_members_walk_the_loop_identically_in_two_runs()
    {
        var a = Play(Build(i => new Vec3(4000, 0, (i - 1) * 1200)).Run, 300, FirstPatrolActorId);
        var (run2, patrol) = Build(i => new Vec3(4000, 0, (i - 1) * 1200));
        var b = Play(run2, 300, FirstPatrolActorId);
        Assert.Equal(a.Path, b.Path);
        for (var t = 0; t < a.Path.Count; t++)
        {
            var p = Patrols.PointAt(patrol, t);
            Assert.Equal(new Vec3(p.X, 0, p.Z - 600), a.Path[t]);
        }
        Assert.True(a.Run.State.Actors[FirstPatrolActorId].Idle);
        Assert.True(a.Run.State.Actors[FirstPatrolActorId + 1].Idle);
        Assert.Equal(new Vec3(Patrols.PointAt(patrol, 300).X, 0, 600), a.Run.State.Actors[FirstPatrolActorId + 1].Pos);
    }

    [Fact(DisplayName = "Patrol: a party standing on the loop is engaged on the first tick the patrol's ring reaches it (tick 53 for a seat at (26000, 1000)), not before")]
    public void Party_in_the_patrol_radius_is_engaged()
    {
        // Seat 0 at (26000, 1000): outside the Choir's ring (centre (21500, 333), 4500 mm away in x). The patrol walks
        // west from x 32500 at 50 mm/tick; its ring holds the seat once (x - 26000)^2 + 1000^2 <= 4000^2, x <= 29872, so
        // 32500 - 50t <= 29872, t >= 52.56: tick 53. The other two seats wait back at the Entrance.
        var (run, _) = Build(i => i == 0 ? new Vec3(26000, 0, 1000) : new Vec3(4000, 0, (i - 1) * 1200));
        var (end, events, _) = Play(run, 120, FirstPatrolActorId);
        var engage = Assert.Single(events, e => e.Type == "pack_engage");
        _out.WriteLine($"pack_engage tick {engage.Tick} {engage.ToJsObject()}");
        Assert.Equal(53, engage.Tick);
        Assert.Equal("corridor-patrol", ((JsString)engage.Get("packId")!).Value);
        Assert.DoesNotContain(events, e => e.Type == "pull_start");
        Assert.Contains("corridor-patrol", end.Encounter.Engaged);
        Assert.False(end.State.Actors[FirstPatrolActorId].Idle);
        Assert.False(end.State.Actors[FirstPatrolActorId + 1].Idle);
    }

    [Fact(DisplayName = "Patrol: while the party fights the Choir, the patrol walks into range and joins the fight (WoW accidental double pull)")]
    public void Patrol_walking_into_an_ongoing_fight_joins_it()
    {
        // Seat 0 at (23000, 0) stands in the Choir's ring (1537 mm from its centre): the Choir engages on tick 1. The
        // patrol, starting at x 32500, is out of range of that seat until x <= 27000: tick 110.
        var (run, _) = Build(i => i == 0 ? new Vec3(23000, 0, 0) : new Vec3(4000, 0, (i - 1) * 1200));
        var (end, events, _) = Play(run, 140, FirstPatrolActorId);
        var pull = Assert.Single(events, e => e.Type == "pull_start");
        var engage = Assert.Single(events, e => e.Type == "pack_engage");
        _out.WriteLine($"pull_start tick {pull.Tick}; pack_engage tick {engage.Tick}");
        Assert.Equal(1, pull.Tick);
        Assert.Equal(110, engage.Tick);
        var choirAlive = run.Dungeon.Pulls.Single(p => p.Id == "choir").Spawns.Count(s => end.State.Actors[s.ActorId].Alive);
        Assert.True(choirAlive > 0, "the Choir fight was over before the patrol arrived");
        foreach (var id in new[] { FirstPatrolActorId, FirstPatrolActorId + 1 })
        {
            var a = end.State.Actors[id];
            Assert.False(a.Idle, $"patrol member {id} did not join the fight");
            Assert.NotNull(a.TargetId);
        }
    }

    [Fact(DisplayName = "Patrol: a party that stays off the loop is never engaged by it (three full loops), and the patrol keeps walking")]
    public void Party_avoiding_the_loop_is_never_engaged()
    {
        // Every seat waits at the choir-room start (14500, 0): 7000 mm from the Choir's ring centre, 12000 mm from the
        // patrol's nearest point (26500, 0).
        var (run, patrol) = Build(i => new Vec3(14500, 0, (i - 1) * 1200));
        var (end, events, path) = Play(run, 3 * 240, FirstPatrolActorId);
        Assert.DoesNotContain(events, e => e.Type is "pack_engage" or "pull_start");
        Assert.DoesNotContain("corridor-patrol", end.Encounter.Engaged);
        Assert.True(end.State.Actors[FirstPatrolActorId].Idle);
        Assert.Equal(path[0], path[240]);
        Assert.NotEqual(path[0], path[120]);
        Assert.Equal(new Vec3(26500, 0, -600), path[120]);
        Assert.Equal(Patrols.PointAt(patrol, 720).X, end.State.Actors[FirstPatrolActorId].Pos.X);
    }

    [Fact(DisplayName = "Patrol: its members are enemy actors of their content enemy id, so a dead patrol banks its forces (2 x choir.melee = 80) like any pack")]
    public void Patrol_counts_toward_forces()
    {
        var (run, _) = Build(i => new Vec3(14500, 0, (i - 1) * 1200));
        var root = (JsObject)JsValue.Parse(File.ReadAllText(DataPath("forces.json")));
        var values = ((JsArray)root.Get("values")!).Items.Cast<JsObject>()
            .ToDictionary(v => ((JsString)v.Get("enemyId")!).Value, v => (int)L(v, "points"));
        var before = Forces.Points(run.State, values);
        var dead = run.State.Clone();
        foreach (var id in new[] { FirstPatrolActorId, FirstPatrolActorId + 1 })
        {
            Assert.Equal(ActorKind.Enemy, dead.Actors[id].Kind);
            dead.Actors[id].Alive = false;
            dead.Actors[id].Health = 0;
        }
        Assert.Equal(before + 80, Forces.Points(dead, values));
    }
}
