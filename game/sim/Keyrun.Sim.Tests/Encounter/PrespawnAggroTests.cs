using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Keyrun.Tools.Replay;
using Xunit;
using Xunit.Abstractions;

namespace Keyrun.Sim.Tests.Encounter;

/// <summary>
/// PRESPAWN-AGGRO (design/BIBLE.md section 9.1; Matt 2026-10-20, verbatim: "everything should be prespawned with an aggro
/// distance" and "when i say 0 downtime, i mean the high stakes m+ introduces, not literally spawning enemies on the
/// player"; game/design/FORCES-ROUTE-PLAN.md increment 0). On a layout run every enemy group, the boss included, exists
/// from tick 0, idle in formation in its own room: it casts nothing, telegraphs nothing and does not move. A group
/// engages, whole, on the first tick a living party member stands within its aggro radius (inclusive, integer mm in XZ;
/// <see cref="Pulls.DefaultAggroMm"/> unless the pull sets <see cref="PullDef.AggroMm"/>), and its scripts start on that
/// tick. Walking into a room engages nothing. This also closes KF3 (the Seed elite spawned on entry): the Seed stands
/// idle in its chamber until the party reaches its ring.
/// </summary>
public sealed class PrespawnAggroTests
{
    private const long Seed = 1;
    private const int SeedActorId = 108;

    /// <summary>Just inside the seed-chamber gate line (layout.json seed-chamber minX 62000).</summary>
    private const long DoorwayX = 62600;

    /// <summary>How long the party stands at the doorway, and how long the Seed is watched after its engage.</summary>
    private const int WatchTicks = 400;

    private readonly ITestOutputHelper _out;

    /// <summary>Creates the test with xUnit's output sink (engage ticks are printed for the PR).</summary>
    /// <param name="output">The sink.</param>
    public PrespawnAggroTests(ITestOutputHelper output) => _out = output;

    // The shipped k2 dungeon placed into the shipped layout as the host places it (NavLayouts.PlaceDungeon): each pull at
    // its room origin plus its content offset, the boss pull at the arena origin.
    private static (Run Run, DungeonDef Dungeon) RealRun(Func<DungeonDef, DungeonDef>? pick, Func<int, Vec3> seatAt)
    {
        var (nav, rooms) = SeedEngageTests.Layout();
        var setup = RunSetup.Load(OracleFixtures.PathOf("hollow-vault-k2.setup.json"), Seed);
        var placed = setup.Dungeon with
        {
            Pulls = setup.Dungeon.Pulls.Select(p =>
            {
                var origin = rooms.Single(r => r.Pull == p.Id).Origin!.Value;
                var boss = p.Id == "boss";
                return p with
                {
                    Spawns = p.Spawns.Select(s => s with
                    {
                        Pos = boss ? new Vec3(origin.X, s.Pos.Y, origin.Z) : new Vec3(origin.X + s.Pos.X, s.Pos.Y, origin.Z + s.Pos.Z),
                    }).ToList(),
                };
            }).ToList(),
        };
        var dungeon = pick is null ? placed : pick(placed);
        var party = setup.Party.Take(3).Select((m, i) => m with { Pos = seatAt(i), Yaw = 0 }).ToList();
        var content = setup.Content with { Layout = nav, Bounds = nav.Extent };
        return (RunRoot.CreateRun(Seed, party, dungeon, content, setup.Options), dungeon);
    }

    [Fact(DisplayName = "Prespawn aggro: at tick 0 every group, the boss included, stands alive and idle at its formation; none acts while the party waits")]
    public void Every_group_exists_idle_from_tick_zero()
    {
        var (run, dungeon) = RealRun(null, i => new Vec3(0, 0, (i - 1) * 1200));
        Assert.Contains(dungeon.Pulls, p => p.Id == "boss");
        var spawns = dungeon.Pulls.SelectMany(p => p.Spawns).ToList();
        Assert.True(spawns.Count >= 10, $"the k2 dungeon has {spawns.Count} spawns");
        foreach (var s in spawns)
        {
            Assert.True(run.State.Actors.TryGetValue(s.ActorId, out var a), $"enemy {s.ActorId} ({s.ClassId}) does not exist at tick 0");
            Assert.True(a!.Alive);
            Assert.True(a.Idle, $"enemy {s.ActorId} ({s.ClassId}) is not idle at tick 0");
            AssertPlacedOnItsPoint(s, a.Pos, spawns, run.Content.ActorRadiusMm);
            Assert.Null(a.TargetId);
        }
        Assert.Empty(run.Encounter.Engaged);
        var placed = PlacedAtTickZero(run, spawns);

        var events = new List<SimEvent>();
        for (var i = 0; i < 200; i++)
        {
            var stepped = RunRoot.StepRun(run, Array.Empty<Command>());
            events.AddRange(stepped.Events);
            run = stepped.Run;
            Assert.All(spawns, s => Assert.Null(run.State.Actors[s.ActorId].CastId));
        }
        Assert.DoesNotContain(events, e => e.Type is "pull_start" or "gate_closed" or "telegraph" or "damage" or "pack_engage");
        // No idle enemy moves (BIBLE section 9.1 "it does not move unless it is a patrol"), bodies the content stacks on one
        // spawn point (the Choir pair, the five Pair melee) included: actor spacing never shoves an idle body. Each stays
        // where it was placed at tick 0.
        Assert.All(spawns, s =>
        {
            var a = run.State.Actors[s.ActorId];
            Assert.True(a.Idle);
            Assert.Equal(placed[s.ActorId], a.Pos);
        });
    }

    [Fact(DisplayName = "Prespawn aggro (PR #308): a pull the content stacks on one spawn point stands separated from tick 0 (no two bodies of a pull overlap, every body in its room)")]
    public void A_pull_stacked_on_one_point_stands_separated_from_tick_zero()
    {
        var (run, dungeon) = RealRun(null, AtEntrance);
        var radius = run.Content.ActorRadiusMm;
        Assert.True(radius > 0, $"the k2 content has actor radius {radius}");
        var minDist = 2 * radius;
        var nav = run.Content.Layout!;
        Assert.Contains(dungeon.Pulls, p => p.Spawns.GroupBy(s => (s.Pos.X, s.Pos.Z)).Any(g => g.Count() >= 5));
        foreach (var pull in dungeon.Pulls)
        {
            var bodies = pull.Spawns.Select(s => run.State.Actors[s.ActorId]).ToList();
            for (var i = 0; i < bodies.Count; i++)
            {
                for (var j = i + 1; j < bodies.Count; j++)
                {
                    var dx = bodies[j].Pos.X - bodies[i].Pos.X;
                    var dz = bodies[j].Pos.Z - bodies[i].Pos.Z;
                    Assert.True((dx * dx) + (dz * dz) >= minDist * minDist,
                        $"pull {pull.Id}: enemies {bodies[i].Id} at {bodies[i].Pos} and {bodies[j].Id} at {bodies[j].Pos} overlap at tick 0 (push-out radius {radius} mm)");
                }
            }
            if (nav.RoomFor(pull.Id) is { } room)
            {
                Assert.All(bodies, a => Assert.True(Movement.Contains(room.Box, a.Pos), $"pull {pull.Id}: enemy {a.Id} placed at {a.Pos}, outside its room {room.Id}"));
            }
        }
    }

    // A body stands at its content spawn point at tick 0, or, when the content stacks k bodies on that point, beside it:
    // same y and z, x within (k - 1) * radius of the point (a single row of non-overlapping bodies centred on it).
    private static void AssertPlacedOnItsPoint(PullSpawn s, Vec3 at, IReadOnlyList<PullSpawn> spawns, long radiusMm)
    {
        var k = spawns.Count(o => o.Pos.X == s.Pos.X && o.Pos.Z == s.Pos.Z);
        if (k == 1)
        {
            Assert.Equal(s.Pos, at);
            return;
        }
        Assert.True(at.Y == s.Pos.Y && at.Z == s.Pos.Z && Math.Abs(at.X - s.Pos.X) <= (k - 1) * radiusMm,
            $"stacked enemy {s.ActorId} (one of {k} on {s.Pos}) placed at {at}");
    }

    private static Dictionary<int, Vec3> PlacedAtTickZero(Run run, IReadOnlyList<PullSpawn> spawns) =>
        spawns.ToDictionary(s => s.ActorId, s => run.State.Actors[s.ActorId].Pos);

    // The party at the layout start (layout.json start x 4000, the Entrance), seats 1200 mm apart on z.
    private static Vec3 AtEntrance(int seat) => new(4000, 0, (seat - 1) * 1200);

    [Fact(DisplayName = "Prespawn aggro (run C, PR #308): with the party at the Entrance no enemy has a target or moves, from tick 0 on")]
    public void No_enemy_moves_or_targets_while_the_party_stands_at_the_entrance()
    {
        var (run, dungeon) = RealRun(null, AtEntrance);
        var spawns = dungeon.Pulls.SelectMany(p => p.Spawns).ToList();
        var placed = PlacedAtTickZero(run, spawns);
        for (var i = 0; i < 200; i++)
        {
            var tick = run.State.Tick;
            var stepped = RunRoot.StepRun(run, Array.Empty<Command>());
            Assert.DoesNotContain(stepped.Events, e => e.Type is "pull_start" or "pack_engage");
            run = stepped.Run;
            foreach (var s in spawns)
            {
                var a = run.State.Actors[s.ActorId];
                Assert.True(a.Idle, $"tick {tick}: enemy {s.ActorId} ({s.ClassId}) engaged with the party at the Entrance");
                Assert.True(a.TargetId is null, $"tick {tick}: idle enemy {s.ActorId} ({s.ClassId}) has target {a.TargetId}");
                Assert.True(placed[s.ActorId] == a.Pos, $"tick {tick}: idle enemy {s.ActorId} ({s.ClassId}) moved from {placed[s.ActorId]} to {a.Pos}");
            }
        }
    }

    [Fact(DisplayName = "Prespawn aggro (run C, PR #308): walking in from the Entrance, the Choir engages whole on the tick a party member crosses its ring; nothing moves before")]
    public void The_choir_engages_whole_on_the_tick_the_party_crosses_its_ring()
    {
        var (run, dungeon) = RealRun(null, AtEntrance);
        var choir = dungeon.Pulls.Single(p => p.Id == "choir");
        var choirIds = choir.Spawns.Select(s => s.ActorId).ToHashSet();
        var center = Pulls.RingCenter(choir);
        var radius = choir.AggroMm;
        var spawns = dungeon.Pulls.SelectMany(p => p.Spawns).ToList();
        var placed = PlacedAtTickZero(run, spawns);
        bool PartyInRing(Run r) => new[] { 1, 2, 3 }.Any(id => r.State.Actors[id].Alive && Aggro.InRing(r.State.Actors[id].Pos, center, radius));

        int? engage = null;
        while (engage is null && run.State.Tick < 400)
        {
            var tick = run.State.Tick;
            var wasIn = PartyInRing(run);
            var walk = new[] { 1, 2, 3 }.Select(id => (Command)new MoveCommand(tick, id, new Dir2(1000, 0), 0)).ToList();
            var stepped = RunRoot.StepRun(run, walk);
            run = stepped.Run;
            var starts = stepped.Events.Where(e => e.Type is "pull_start" or "pack_engage").ToList();
            if (starts.Count == 0)
            {
                Assert.False(PartyInRing(run), $"tick {tick}: a party member stands in the Choir ring and nothing engaged");
                foreach (var s in spawns)
                {
                    var a = run.State.Actors[s.ActorId];
                    Assert.True(a.Idle && a.TargetId is null && a.Pos == placed[s.ActorId], $"tick {tick}: enemy {s.ActorId} ({s.ClassId}) acted before any engage: idle={a.Idle} target={a.TargetId} pos={a.Pos}");
                }
                continue;
            }
            engage = tick;
            var start = Assert.Single(starts);
            Assert.Equal("choir", ((JsString)start.Get("pullId")!).Value);
            Assert.False(wasIn, "the party was already in the Choir ring a tick before the engage");
            Assert.True(PartyInRing(run), "the Choir engaged with no party member in its ring");
            foreach (var s in spawns)
            {
                var a = run.State.Actors[s.ActorId];
                Assert.True(choirIds.Contains(s.ActorId) ? !a.Idle : a.Idle, $"tick {tick}: enemy {s.ActorId} ({s.ClassId}) idle={a.Idle} on the Choir's engage tick");
                if (!choirIds.Contains(s.ActorId))
                {
                    Assert.True(a.TargetId is null && a.Pos == placed[s.ActorId], $"tick {tick}: enemy {s.ActorId} outside the Choir acted on its engage");
                }
            }
        }
        _out.WriteLine($"Choir engage t{engage}");
        Assert.NotNull(engage);
    }

    // One room, one pull: enemy 101 at (10000, 0) (the ring's centre), the Warden standing at x on the z = 0 line.
    private static IReadOnlyList<SimEvent> OneStepWithWardenAt(long x, long? aggroMm = null)
    {
        var room = new Bounds(-20000, 20000, -20000, 20000);
        var layout = new NavLayout(new[] { new NavBox("room", room, true, null) }, new[] { new NavTrigger("p1", room) }, new[] { new Vec3(19000, 0, 0) });
        var pull = new PullDef("p1", new[] { RunTestKit.Spawn(101, "dummy", 10000, 0, 10) });
        if (aggroMm is { } r)
        {
            pull = pull with { AggroMm = r };
        }
        var content = RunTestKit.Content(bounds: layout.Extent) with { Layout = layout };
        var run = RunRoot.CreateRun(1, new[] { RunTestKit.Member(1, "warden", ActorRole.Tank, x, 0, 400, 100) }, new DungeonDef("ring", new[] { pull }), content);
        Assert.True(run.State.Actors[101].Idle);
        return RunRoot.StepRun(run, Array.Empty<Command>()).Events;
    }

    [Fact(DisplayName = "Prespawn aggro: a party member 1 mm outside the aggro radius engages nothing; on the radius the group engages")]
    public void The_aggro_radius_is_inclusive_to_the_millimetre()
    {
        Assert.Equal(4000, Pulls.DefaultAggroMm);
        Assert.DoesNotContain(OneStepWithWardenAt(10000 - 4001), e => e.Type == "pull_start");
        var start = Assert.Single(OneStepWithWardenAt(10000 - 4000), e => e.Type == "pull_start");
        Assert.Equal(1, start.Tick);
        Assert.Equal("p1", ((JsString)start.Get("pullId")!).Value);

        // A pull's own radius replaces the default.
        Assert.DoesNotContain(OneStepWithWardenAt(10000 - 2501, aggroMm: 2500), e => e.Type == "pull_start");
        Assert.Single(OneStepWithWardenAt(10000 - 2500, aggroMm: 2500), e => e.Type == "pull_start");
        Assert.DoesNotContain(OneStepWithWardenAt(10000 - 4000, aggroMm: 2500), e => e.Type == "pull_start");

        Assert.True(Aggro.InRing(new Vec3(3, 0, 4), Vec3.Zero, 5));
        Assert.False(Aggro.InRing(new Vec3(3, 0, 5), Vec3.Zero, 5));
        Assert.True(Aggro.InRing(new Vec3(3, 99999, 4), Vec3.Zero, 5));
    }

    [Fact(DisplayName = "KF3 Seed idle: the Seed elite stands idle in its chamber while the party waits at the doorway, and opens only after its engage")]
    public void The_Seed_stays_idle_until_the_party_reaches_its_ring()
    {
        var (run, dungeon) = RealRun(d => d with { Pulls = d.Pulls.Where(p => p.Id == "seed").ToList() }, i => new Vec3(DoorwayX, 0, (i - 1) * 1200));
        var seedSpawn = Assert.Single(dungeon.Pulls.Single().Spawns);
        Assert.Equal(SeedActorId, seedSpawn.ActorId);
        var (nav, _) = SeedEngageTests.Layout();
        var chamber = nav.RoomFor("seed")!;
        Assert.All(new[] { 1, 2, 3 }, id => Assert.True(Movement.Contains(chamber.Box, run.State.Actors[id].Pos), $"seat {id} is not inside {chamber.Id}"));

        // Phase 1: the whole party stands inside the seed-chamber, at the doorway, for WatchTicks. Nothing happens.
        var waiting = new List<SimEvent>();
        for (var i = 0; i < WatchTicks; i++)
        {
            var stepped = RunRoot.StepRun(run, Array.Empty<Command>());
            waiting.AddRange(stepped.Events);
            run = stepped.Run;
            var seed = run.State.Actors[SeedActorId];
            Assert.True(seed.Idle, $"tick {run.State.Tick}: the Seed engaged a party standing at the doorway");
            Assert.Null(seed.CastId);
            Assert.Equal(seedSpawn.Pos, seed.Pos);
        }
        Assert.DoesNotContain(waiting, e => e.Type is "pull_start" or "gate_closed" or "telegraph" or "damage");

        // Phase 2: the party walks on until the Seed engages; every Seed telegraph comes on or after the engage tick.
        int? engage = null;
        var telegraphs = new List<int>();
        var stop = int.MaxValue;
        while (run.State.Tick < stop && run.State.Tick < (WatchTicks * 3))
        {
            var tick = run.State.Tick;
            var walk = engage is null
                ? new[] { 1, 2, 3 }.Where(id => run.State.Actors[id].Alive).Select(id => (Command)new MoveCommand(tick, id, new Dir2(1000, 0), 0)).ToList()
                : new List<Command>();
            var stepped = RunRoot.StepRun(run, walk);
            run = stepped.Run;
            foreach (var e in stepped.Events)
            {
                if (engage is null && e.Type == "pull_start")
                {
                    engage = e.Tick;
                    stop = e.Tick + WatchTicks;
                    var center = Pulls.RingCenter(dungeon.Pulls.Single());
                    Assert.Contains(new[] { 1, 2, 3 }, id => Aggro.InRing(run.State.Actors[id].Pos, center, Pulls.DefaultAggroMm));
                    Assert.False(run.State.Actors[SeedActorId].Idle);
                }
                if (e.Type == "telegraph" && e.Get("actorId") is JsInt a && a.Value == SeedActorId)
                {
                    telegraphs.Add(e.Tick);
                }
            }
        }
        _out.WriteLine($"Seed engage t{engage}; Seed telegraphs at {string.Join(",", telegraphs)}");
        Assert.NotNull(engage);
        Assert.NotEmpty(telegraphs);
        Assert.All(telegraphs, t => Assert.True(t >= engage!.Value, $"a Seed telegraph at tick {t} came before its engage at {engage}"));
    }
}
