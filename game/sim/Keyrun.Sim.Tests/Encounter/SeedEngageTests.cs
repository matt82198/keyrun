using System;
using System.Collections.Generic;
using System.IO;
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
/// KF3-SEED-ENGAGE (Matt acceptance finding KF3: the Seed elite "spawns on entry and gets cheesed"; keyrun STATE.md
/// D27 room lock-in). The Seed pull of the shipped content (the hollow-vault k2 oracle setup, recorded from
/// content/data) is placed into the real seed-chamber of game/content/data/layout.json exactly as the host places it
/// (room origin plus the content spawn offset), and the three host seats walk in from corridor-3 until the Seed engages
/// (PRESPAWN-AGGRO, BIBLE section 9.1: the first tick one of them stands in its aggro ring), so <c>pull_start</c> fires
/// (no gate shuts since STATE.md D44); then they stand still on the ring's edge and never cast. Within
/// <see cref="WithinTicks"/> of <c>pull_start</c> the Seed must either close distance on a party actor or land damage
/// on one; a Seed that only slams from its spawn point lets a party parked on its ring kill it for free.
/// </summary>
public sealed class SeedEngageTests
{
    /// <summary>
    /// The Seed's own cast budget (content/data/enemies/seed.json): its slam loops <c>every 100</c> with
    /// <c>castTicks 20</c> (content/data/abilities/enemies.json seed.slam), so by 120 ticks after <c>pull_start</c> the
    /// first slam has landed. An engaging Seed has moved or hit someone by then.
    /// </summary>
    public const int WithinTicks = 120;

    private const long Seed = 1;
    private const int SeedActorId = 108;

    /// <summary>Just inside the seed-chamber gate line (layout.json seed-chamber minX 62000): the reference the Seed closes on.</summary>
    private const long DoorwayX = 62600;

    private readonly ITestOutputHelper _out;

    /// <summary>Creates the test with xUnit's output sink (the engage tick is printed for the PR).</summary>
    /// <param name="output">The sink.</param>
    public SeedEngageTests(ITestOutputHelper output) => _out = output;

    internal sealed record LayoutRoom(string Id, Bounds Box, bool IsRoom, string? Gate, string? Pull, Bounds? Trigger, Vec3? Origin);

    private static string LayoutDir()
    {
        // Fixtures/oracle -> Keyrun.Sim.Tests -> sim -> game, then content/data/layout/hollow-vault (R-LOADER-1: dungeon.json
        // plus one file per room in rooms/, read in ordinal path order like the loader).
        var game = Directory.GetParent(OracleFixtures.Directory)!.Parent!.Parent!.Parent!.FullName;
        return Path.Combine(game, "content", "data", "layout", "hollow-vault");
    }

    private static IEnumerable<JsObject> RoomFiles() =>
        Directory.GetFiles(Path.Combine(LayoutDir(), "rooms"), "*.json", SearchOption.AllDirectories)
            .OrderBy(p => Path.GetRelativePath(LayoutDir(), p).Replace('\\', '/'), StringComparer.Ordinal)
            .Select(p => (JsObject)JsValue.Parse(File.ReadAllText(p)));

    private static long L(JsObject o, string key) => ((JsInt)o.Get(key)!).Value;

    private static Bounds BoundsOf(JsObject o) => new(L(o, "minX"), L(o, "maxX"), L(o, "minZ"), L(o, "maxZ"));

    // The same mapping as Keyrun.Bots NavLayouts.ToSim (game/bots/Keyrun.Bots/shared/Follow.cs), read straight from the
    // shipped layout file so the room the test stands in is the room the game builds.
    internal static (NavLayout Nav, List<LayoutRoom> Rooms) Layout()
    {
        var root = (JsObject)JsValue.Parse(File.ReadAllText(Path.Combine(LayoutDir(), "dungeon.json")));
        var rooms = RoomFiles().Select(b => new LayoutRoom(
            ((JsString)b.Get("id")!).Value,
            BoundsOf(b),
            ((JsString)b.Get("kind")!).Value == "room",
            (b.Get("gate") as JsString)?.Value,
            (b.Get("pull") as JsString)?.Value,
            b.Get("trigger") is JsObject t ? BoundsOf(t) : null,
            b.Get("origin") is JsObject o ? new Vec3(L(o, "x"), 0, L(o, "z")) : null)).ToList();
        var route = ((JsArray)root.Get("route")!).Items.Cast<JsObject>().Select(p => new Vec3(L(p, "x"), 0, L(p, "z"))).ToList();
        var nav = new NavLayout(
            rooms.Select(r => new NavBox(r.Id, r.Box, r.IsRoom, r.Gate)).ToList(),
            rooms.Where(r => r.Pull is not null && r.Trigger is not null).Select(r => new NavTrigger(r.Pull!, r.Trigger!.Value)).ToList(),
            route);
        return (nav, rooms);
    }

    private sealed record Played(List<SimEvent> Events, int StartTick, Vec3 SeedSpawn, List<(int Tick, Vec3 Pos)> SeedPath, Run Final);

    private static Played StandAtTheDoor()
    {
        var (nav, rooms) = Layout();
        var chamber = rooms.Single(r => r.Pull == "seed");
        Assert.Equal("seed-chamber", chamber.Id);
        var origin = chamber.Origin!.Value;
        var setup = RunSetup.Load(OracleFixtures.PathOf("hollow-vault-k2.setup.json"), Seed);
        var seedPull = setup.Dungeon.Pulls.Single(p => p.Id == "seed");
        var placed = seedPull with
        {
            Spawns = seedPull.Spawns.Select(s => s with { Pos = new Vec3(origin.X + s.Pos.X, s.Pos.Y, origin.Z + s.Pos.Z) }).ToList(),
        };
        var spawn = Assert.Single(placed.Spawns);
        Assert.Equal(SeedActorId, spawn.ActorId);
        // The three seats wait in corridor-3, 1 m short of the chamber, then walk in and stop on the Seed's engage tick.
        var party = setup.Party.Take(3).Select((m, i) => m with { Pos = new Vec3(chamber.Box.MinX - 1000, 0, (i - 1) * 1200), Yaw = 0 }).ToList();
        Assert.All(party, m => Assert.False(Movement.Contains(chamber.Box, m.Pos), $"seat {m.Id} at {m.Pos} starts inside the chamber"));
        var dungeon = setup.Dungeon with { Pulls = new[] { placed } };
        var content = setup.Content with { Layout = nav, Bounds = nav.Extent };
        var run = RunRoot.CreateRun(Seed, party, dungeon, content, setup.Options);

        var events = new List<SimEvent>();
        int? start = null;
        var path = new List<(int, Vec3)>();
        for (var i = 0; i < 400 && (start is null || run.State.Tick <= start.Value + WithinTicks); i++)
        {
            var tick = run.State.Tick;
            var walk = party.Select(m => run.State.Actors[m.Id])
                .Where(a => a.Alive && start is null)
                .Select(a => (Command)new MoveCommand(tick, a.Id, new Dir2(1000, 0), 0))
                .ToList();
            var stepped = RunRoot.StepRun(run, walk);
            events.AddRange(stepped.Events);
            run = stepped.Run;
            if (start is null && stepped.Events.FirstOrDefault(e => e.Type == "pull_start") is { } ps)
            {
                start = ps.Tick;
            }
            if (run.State.Actors.TryGetValue(SeedActorId, out var seed))
            {
                path.Add((run.State.Tick, seed.Pos));
            }
        }
        Assert.True(start is not null, "pull_start never fired with the party walking into the Seed's aggro ring");
        return new Played(events, start!.Value, spawn.Pos, path, run);
    }

    private static long Dist(Vec3 a, Vec3 b) => Fixed.Isqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Z - b.Z) * (a.Z - b.Z)));

    [Fact(DisplayName = "KF3 Seed engage: the Seed engages on the tick the party reaches its aggro ring, and no gate shuts (D44)")]
    public void The_seed_engages_on_the_parked_party_with_no_gate()
    {
        var played = StandAtTheDoor();
        var start = Assert.Single(played.Events, e => e.Type == "pull_start" && ((JsString)e.Get("pullId")!).Value == "seed");
        Assert.Equal(played.StartTick, start.Tick);
        Assert.DoesNotContain(played.Events, e => e.Type is "gate_closed" or "gate_open");
    }

    [Fact(DisplayName = "KF3 Seed engage: within its cast budget the Seed closes on or hits a party parked on its aggro ring")]
    public void The_Seed_engages_a_party_parked_at_the_doorway()
    {
        var played = StandAtTheDoor();
        var deadline = played.StartTick + WithinTicks;
        var partyIds = new HashSet<int> { 1, 2, 3 };
        var doorway = new Vec3(DoorwayX, 0, 0);
        var spawnGap = Dist(played.SeedSpawn, doorway);

        var moved = played.SeedPath.FirstOrDefault(p => p.Tick <= deadline && Dist(p.Pos, doorway) < spawnGap);
        var hit = played.Events.FirstOrDefault(e => e.Type == "damage" && e.Tick <= deadline
            && ((JsInt)e.Get("sourceId")!).Value == SeedActorId && partyIds.Contains((int)((JsInt)e.Get("targetId")!).Value));
        var last = played.SeedPath.LastOrDefault(p => p.Tick <= deadline);
        _out.WriteLine($"pull_start t{played.StartTick}; Seed spawn {played.SeedSpawn} ({spawnGap} mm from the doorway); "
            + $"first closing move t{(moved == default ? "-" : moved.Tick.ToString(System.Globalization.CultureInfo.InvariantCulture))}; "
            + $"first hit on the party t{(hit is null ? "-" : hit.Tick.ToString(System.Globalization.CultureInfo.InvariantCulture))}; "
            + $"Seed at t{last.Tick} {last.Pos}");

        Assert.True(moved != default || hit is not null,
            $"the Seed never engaged: within {WithinTicks} ticks of pull_start t{played.StartTick} it stayed at {last.Pos} "
            + $"({Dist(last.Pos, doorway)} mm from the doorway, spawn {played.SeedSpawn}) and landed no damage on the party");
    }

    /// <summary>
    /// R-SEED-2: the Seed's own aggro ring (run.json enemyAggro.aggroMm, 8000 mm) is the only engage rule for a room-
    /// activated pull; standing in seed-chamber is not an engage by itself.
    /// </summary>
    internal const long SeedAggroMm = 8000;

    /// <summary>Builds the hollow-vault k2 run with the Seed placed in seed-chamber and seat 1 at <paramref name="seat"/>; seats 2 and 3 stand in corridor-3.</summary>
    private static (Run Run, Vec3 SeedPos, Bounds Box) Arrange(Vec3 seat)
    {
        var (nav, rooms) = Layout();
        var chamber = rooms.Single(r => r.Pull == "seed");
        var origin = chamber.Origin!.Value;
        var setup = RunSetup.Load(OracleFixtures.PathOf("hollow-vault-k2.setup.json"), Seed);
        var seedPull = setup.Dungeon.Pulls.Single(p => p.Id == "seed");
        var placed = seedPull with
        {
            Spawns = seedPull.Spawns.Select(s => s with { Pos = new Vec3(origin.X + s.Pos.X, s.Pos.Y, origin.Z + s.Pos.Z) }).ToList(),
        };
        var seedPos = Assert.Single(placed.Spawns).Pos;
        var corridor = chamber.Box.MinX - 1000;
        var seats = new[] { seat, new Vec3(corridor, 0, -1200), new Vec3(corridor, 0, 1200) };
        var party = setup.Party.Take(3).Select((m, i) => m with { Pos = seats[i], Yaw = 0 }).ToList();
        // R-SEED-2: the Seed carries its run.json enemyAggro radius (8000 mm) exactly as the host places it
        // (HostRunSetup.WithEnemyAggro with the shipped registry), so the ring the test walks into is the ring the game uses.
        var registry = Keyrun.Content.ContentLoader.Load();
        var dungeon = Keyrun.Host.HostRunSetup.WithEnemyAggro(registry.Run.EnemyAggro, registry.Layout, setup.Dungeon with { Pulls = new[] { placed } }, [], []).Dungeon;
        var content = setup.Content with { Layout = nav, Bounds = nav.Extent };
        return (RunRoot.CreateRun(Seed, party, dungeon, content, setup.Options), seedPos, chamber.Box);
    }

    [Fact(DisplayName = "KF3 Seed room (R-SEED-2): a seat inside seed-chamber but outside the Seed's 8000 mm aggro ring does not pull it")]
    public void A_seat_inside_the_chamber_outside_the_seed_aggro_ring_does_not_pull_the_seed()
    {
        var (run, seedPos, box) = Arrange(new Vec3(63500, 0, 0));
        var seat = run.State.Actors[1];
        Assert.True(Movement.Contains(box, seat.Pos), $"seat 1 at {seat.Pos} is not inside seed-chamber");
        var gap = Dist(seat.Pos, seedPos);
        Assert.True(gap > SeedAggroMm, $"seat 1 at {seat.Pos} is {gap} mm from the Seed, inside its {SeedAggroMm} mm ring");

        var events = new List<SimEvent>();
        for (var i = 0; i < 200; i++)
        {
            var stepped = RunRoot.StepRun(run, Array.Empty<Command>());
            events.AddRange(stepped.Events);
            run = stepped.Run;
        }
        var pull = events.FirstOrDefault(e => e.Type == "pull_start" && ((JsString)e.Get("pullId")!).Value == "seed");
        _out.WriteLine($"seat 1 at {seat.Pos} ({gap} mm from the Seed); pull_start seed: {(pull is null ? "none in 200 ticks" : "t" + pull.Tick)}");
        Assert.True(pull is null,
            $"R-SEED-2: seat 1 stood at {seat.Pos} in seed-chamber, {gap} mm from the Seed (ring {SeedAggroMm} mm), and pull_start seed fired at t{pull?.Tick}");
    }

    [Fact(DisplayName = "KF3 Seed room (R-SEED-2): a seat that walks into the Seed's 8000 mm aggro ring pulls it within 20 ticks")]
    public void A_seat_walking_into_the_seed_aggro_ring_pulls_the_seed_within_20_ticks()
    {
        var (run, seedPos, _) = Arrange(new Vec3(61000, 0, 0));
        var events = new List<SimEvent>();
        int? reach = null;
        for (var i = 0; i < 400 && (reach is null || run.State.Tick < reach.Value + 20); i++)
        {
            var seat = run.State.Actors[1];
            var walk = reach is null && seat.Alive
                ? new Command[] { new MoveCommand(run.State.Tick, seat.Id, new Dir2(1000, 0), 0) }
                : Array.Empty<Command>();
            var stepped = RunRoot.StepRun(run, walk);
            events.AddRange(stepped.Events);
            run = stepped.Run;
            // Measured against the Seed's live position, so an idle-wander step of the Seed around its anchor cannot move the target.
            if (reach is null && Dist(run.State.Actors[1].Pos, run.State.Actors[SeedActorId].Pos) <= SeedAggroMm)
            {
                reach = run.State.Tick;
            }
        }
        Assert.True(reach is not null, "seat 1 never got within the Seed's aggro ring");
        var pull = events.FirstOrDefault(e => e.Type == "pull_start" && ((JsString)e.Get("pullId")!).Value == "seed");
        _out.WriteLine($"seat 1 reached the ring at t{reach}; pull_start seed: {(pull is null ? "none" : "t" + pull.Tick)}");
        Assert.True(pull is not null && pull.Tick <= reach!.Value + 20,
            $"seat 1 reached the Seed's {SeedAggroMm} mm ring at t{reach} and pull_start seed was {(pull is null ? "never fired" : "at t" + pull.Tick)} (budget 20 ticks)");
    }
}
