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
            Assert.Equal(s.Pos, a.Pos);
            Assert.Null(a.TargetId);
        }
        Assert.Empty(run.Encounter.Engaged);

        var events = new List<SimEvent>();
        for (var i = 0; i < 200; i++)
        {
            var stepped = RunRoot.StepRun(run, Array.Empty<Command>());
            events.AddRange(stepped.Events);
            run = stepped.Run;
            Assert.All(spawns, s => Assert.Null(run.State.Actors[s.ActorId].CastId));
        }
        Assert.DoesNotContain(events, e => e.Type is "pull_start" or "gate_closed" or "telegraph" or "damage" or "pack_engage");
        // No idle enemy walks. Bodies the content stacks on one spawn point (the Choir pair, the five Pair melee) are only
        // pushed apart by actor spacing (Rules collision push-out), so they stay beside their point; a lone body never moves.
        var stacked = spawns.GroupBy(s => (s.Pos.X, s.Pos.Z)).Where(g => g.Count() > 1).SelectMany(g => g).Select(s => s.ActorId).ToHashSet();
        Assert.All(spawns, s =>
        {
            var a = run.State.Actors[s.ActorId];
            Assert.True(a.Idle);
            if (stacked.Contains(s.ActorId))
            {
                Assert.True(Aggro.InRing(a.Pos, s.Pos, StackedSpreadMm), $"stacked enemy {s.ActorId} drifted to {a.Pos} from {s.Pos}");
            }
            else
            {
                Assert.Equal(s.Pos, a.Pos);
            }
        });
    }

    /// <summary>How far actor spacing may spread a stack of up to five 400 mm bodies from their shared point, mm.</summary>
    private const long StackedSpreadMm = 2500;

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
