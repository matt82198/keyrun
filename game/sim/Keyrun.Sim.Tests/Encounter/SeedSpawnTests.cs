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
/// KF3 Seed spawn gate (Matt's demo verdict 2026-10-19, finding f-70c82aca, default run C): the Seed elite was engaged
/// from range, before the party reached its room, so the bots cheesed the boss's core rule. WoW Mythic+ rule (Matt:
/// "anything you want my decision on in the core dungeon loop, defer to wow"): a pull is inert until the party reaches
/// its area. On the default key-2, seed-1 run the Seed stands dormant in the seed-chamber (idle, no casts, no threat, no
/// damage) until the first living party member stands inside the chamber's room box, and it activates on that tick.
/// These tests drive the shipped content (hollow-vault-k2 setup placed into content/data/layout.json) through the real
/// <see cref="RunRoot"/> stepper and assert only observable behaviour: events, positions, health and casts.
/// </summary>
public sealed class SeedSpawnTests
{
    private const long Seed = 1;
    private const int SeedActorId = 108;

    /// <summary>Ticks the party waits outside the chamber, and ticks the Seed is watched after its activation.</summary>
    private const int WaitTicks = 300;

    /// <summary>One metre outside the seed-chamber's room box (layout.json seed-chamber minX 62000): corridor-3, the closest
    /// point a party can stand without being inside the room.</summary>
    private const long JustOutsideX = 61999;

    private readonly ITestOutputHelper _out;

    /// <summary>Creates the test with xUnit's output sink (activation ticks are printed for the PR).</summary>
    /// <param name="output">The sink.</param>
    public SeedSpawnTests(ITestOutputHelper output) => _out = output;

    // The shipped k2 dungeon placed into the shipped layout exactly as the host places it (room origin plus content
    // offset; the Seed's chamber origin is layout.json seed-chamber origin 63500, so the Seed stands at x 73500).
    private static (Run Run, PullDef Seed, NavBox Room) SeedRunAt(Func<int, Vec3> seatAt)
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
        // R-SEED-2: the Seed is a standard prespawned enemy on its run.json enemyAggro radius (8000 mm), placed exactly as the host does.
        var registry = Keyrun.Content.ContentLoader.Load();
        placed = Keyrun.Host.HostRunSetup.WithEnemyAggro(registry.Run.EnemyAggro, registry.Layout, placed, [], []).Dungeon;
        var party = setup.Party.Take(3).Select((m, i) => m with { Pos = seatAt(i), Yaw = 0 }).ToList();
        var content = setup.Content with { Layout = nav, Bounds = nav.Extent };
        var run = RunRoot.CreateRun(Seed, party, placed, content, setup.Options);
        var room = nav.RoomFor("seed") ?? throw new InvalidOperationException("the seed pull has no room in layout.json");
        Assert.Equal("seed-chamber", room.Id);
        return (run, placed.Pulls.Single(p => p.Id == "seed"), room);
    }

    private static Vec3 Corridor(int seat) => new(JustOutsideX, 0, (seat - 1) * 1200);

    private static IEnumerable<int> PartyIds => new[] { 1, 2, 3 };

    [Fact(DisplayName = "KF3 Seed spawn: with the party in corridor-3, outside the chamber, the Seed is idle and nothing happens for 300 ticks")]
    public void The_Seed_is_inert_while_the_party_waits_outside_its_room()
    {
        var (run, seed, room) = SeedRunAt(Corridor);
        Assert.All(PartyIds, id => Assert.False(Movement.Contains(room.Box, run.State.Actors[id].Pos), $"seat {id} starts inside {room.Id}"));
        var start = run.State.Actors[SeedActorId].Pos;
        Assert.Contains(seed.Spawns, s => s.ActorId == SeedActorId);

        var events = new List<SimEvent>();
        for (var i = 0; i < WaitTicks; i++)
        {
            var tick = run.State.Tick;
            var stepped = RunRoot.StepRun(run, Array.Empty<Command>());
            events.AddRange(stepped.Events);
            run = stepped.Run;
            var a = run.State.Actors[SeedActorId];
            Assert.True(a.Idle, $"tick {tick}: the Seed engaged a party standing outside its room");
            Assert.Null(a.CastId);
            Assert.Null(a.TargetId);
            Assert.Equal(start, a.Pos);
        }
        Assert.DoesNotContain(events, e => e.Type is "pull_start" && ((JsString)e.Get("pullId")!).Value == "seed");
        Assert.DoesNotContain(events, e => e.Type is "telegraph" or "damage" && e.Get("actorId") is JsInt id && id.Value == SeedActorId);
        Assert.DoesNotContain(events, e => e.Type == "damage" && e.Get("sourceId") is JsInt src && src.Value == SeedActorId);
    }

    private static long Dist(Vec3 a, Vec3 b) => Fixed.Isqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Z - b.Z) * (a.Z - b.Z)));

    // Derivation (R-SEED-2 ruling (b), WoW "a ranged pull is a pull"): the Seed is a standard prespawned enemy. Room
    // membership is not an engage; the Seed engages on its 8000 mm aggro ring, on damage, or by assist. A party cast from
    // outside the chamber that lands damage on the Seed therefore engages it on that tick, and a cast that lands none
    // (out of range or no target) leaves it idle. The old expectation (no damage ever from outside the room) is withdrawn.
    // One fact asserts the rule that holds in both cases: idle until the first damage event on the Seed, engaged from it on.
    [Fact(DisplayName = "KF3 Seed spawn (R-SEED-2): a party cast from outside the chamber engages the Seed only when it lands damage on it")]
    public void A_party_cast_from_outside_the_room_engages_the_Seed_only_by_damage()
    {
        var (run, _, room) = SeedRunAt(Corridor);
        var health = run.State.Actors[SeedActorId].Health;
        var abilities = run.Content.Abilities.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();
        Assert.NotEmpty(abilities);
        var landed = new List<SimEvent>();
        var completed = new List<string>();
        int? firstDamageTick = null;
        for (var i = 0; i < WaitTicks; i++)
        {
            var tick = run.State.Tick;
            // One ability per member per tick, cycling the whole content list, so every hostile ability gets its turn at the
            // Seed (a press-all list lets the first off-GCD ability win each tick and starves the rest).
            var casts = PartyIds.Select(id => (Command)new CastCommand(tick, id, abilities[(i + (id * 7)) % abilities.Count], SeedActorId)).ToList();
            var stepped = RunRoot.StepRun(run, casts);
            run = stepped.Run;
            completed.AddRange(stepped.Events.Where(e => e.Type == "cast_complete").Select(e => ((JsString)e.Get("abilityId")!).Value));
            var hits = stepped.Events.Where(e => e.Type == "damage" && e.Get("targetId") is JsInt t && t.Value == SeedActorId).ToList();
            landed.AddRange(hits);
            if (firstDamageTick is null && hits.Count > 0)
            {
                firstDamageTick = tick;
            }
            var seedNow = run.State.Actors[SeedActorId];
            if (firstDamageTick is null)
            {
                Assert.True(seedNow.Idle, $"tick {tick}: the Seed engaged with no damage landed on it, from outside {room.Id}");
            }
            else
            {
                Assert.False(seedNow.Idle, $"tick {tick}: the Seed took damage from outside {room.Id} at t{firstDamageTick} and stayed idle");
            }
        }
        _out.WriteLine($"casts completed {completed.Count} ({string.Join(",", completed.Distinct().OrderBy(c => c, StringComparer.Ordinal))}); "
            + $"damage events on the Seed from outside the room: {landed.Count} (first at t{firstDamageTick?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"}); "
            + $"Seed health {health} -> {run.State.Actors[SeedActorId].Health}");
        Assert.NotEmpty(completed);
    }

    // Derivation (R-SEED-2 ruling (b)): the Seed engages on the first tick a living seat is within its own 8000 mm aggro
    // radius (run.json enemyAggro.aggroMm, applied by HostRunSetup.WithEnemyAggro). Room entry is not the engage. The old
    // expectation (activation equal to the room_enter tick) is withdrawn. The seats walk in from corridor-3; the expected
    // tick is the first tick at which a seat stands within 8000 mm of the Seed's live position, read before that tick's step.
    [Fact(DisplayName = "KF3 Seed spawn (R-SEED-2): the Seed engages on the first tick a living seat is within its 8000 mm aggro radius, not one tick earlier")]
    public void The_Seed_engages_on_the_first_tick_a_seat_is_inside_its_aggro_radius()
    {
        var (run, _, room) = SeedRunAt(Corridor);
        int? ringAt = null;
        int? activatedAt = null;
        for (var i = 0; i < 600 && activatedAt is null; i++)
        {
            var tick = run.State.Tick;
            var walk = PartyIds.Where(id => run.State.Actors[id].Alive)
                .Select(id => (Command)new MoveCommand(tick, id, new Dir2(1000, 0), 0)).ToList();
            var stepped = RunRoot.StepRun(run, walk);
            run = stepped.Run;
            // The step moves the seats before the aggro check, and its events are stamped with the post-step tick (the same
            // numbering as pull_start), so the ring is read on the post-move positions and recorded on the post-step tick.
            var seedNow = run.State.Actors[SeedActorId].Pos;
            if (ringAt is null && PartyIds.Any(id => run.State.Actors[id].Alive && Dist(run.State.Actors[id].Pos, seedNow) <= SeedEngageTests.SeedAggroMm))
            {
                ringAt = run.State.Tick;
            }
            foreach (var e in stepped.Events)
            {
                if (e.Type == "pull_start" && ((JsString)e.Get("pullId")!).Value == "seed")
                {
                    activatedAt = e.Tick;
                }
            }
        }
        _out.WriteLine($"seat entered the Seed's {SeedEngageTests.SeedAggroMm} mm ring at t{ringAt}; Seed engaged at t{activatedAt} ({room.Id})");
        Assert.NotNull(ringAt);
        Assert.Equal(ringAt, activatedAt);
        Assert.False(run.State.Actors[SeedActorId].Idle);
    }

    [Fact(DisplayName = "KF3 Seed spawn: until activation the Seed never takes damage, and its health is untouched while the party walks in")]
    public void The_Seed_takes_no_damage_before_it_activates()
    {
        var (run, _, _) = SeedRunAt(Corridor);
        var health = run.State.Actors[SeedActorId].Health;
        int? activatedAt = null;
        var damageBeforeActivation = new List<SimEvent>();
        for (var i = 0; i < 600 && activatedAt is null; i++)
        {
            var tick = run.State.Tick;
            var walk = PartyIds.Where(id => run.State.Actors[id].Alive)
                .Select(id => (Command)new MoveCommand(tick, id, new Dir2(1000, 0), 0)).ToList();
            var stepped = RunRoot.StepRun(run, walk);
            run = stepped.Run;
            damageBeforeActivation.AddRange(stepped.Events.Where(e => e.Type == "damage" && e.Get("targetId") is JsInt t && t.Value == SeedActorId));
            activatedAt = stepped.Events.FirstOrDefault(e => e.Type == "pull_start" && ((JsString)e.Get("pullId")!).Value == "seed")?.Tick ?? activatedAt;
            if (activatedAt is null)
            {
                Assert.Equal(health, run.State.Actors[SeedActorId].Health);
            }
        }
        Assert.NotNull(activatedAt);
        Assert.Empty(damageBeforeActivation);
    }

    [Fact(DisplayName = "KF3 Seed spawn: the same seeded run replays to the same events and the same activation tick")]
    public void The_seed_spawn_gate_is_deterministic()
    {
        IReadOnlyList<SimEvent> Play()
        {
            var (run, _, _) = SeedRunAt(Corridor);
            var all = new List<SimEvent>();
            for (var i = 0; i < 600; i++)
            {
                var tick = run.State.Tick;
                var walk = PartyIds.Select(id => (Command)new MoveCommand(tick, id, new Dir2(1000, 0), 0)).ToList();
                var stepped = RunRoot.StepRun(run, walk);
                all.AddRange(stepped.Events);
                run = stepped.Run;
            }
            return all;
        }
        var first = Play();
        var second = Play();
        Assert.Equal(first.Count, second.Count);
        for (var i = 0; i < first.Count; i++)
        {
            Assert.Equal(first[i].Tick, second[i].Tick);
            Assert.Equal(first[i].Type, second[i].Type);
        }
    }
}
