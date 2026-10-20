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
/// (PRESPAWN-AGGRO, BIBLE section 9.1: the first tick one of them stands in its aggro ring), so <c>pull_start</c> and
/// <c>gate_closed seed-chamber</c> fire; then they stand still on the ring's edge and never cast. Within
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

    private static string LayoutPath()
    {
        // Fixtures/oracle -> Keyrun.Sim.Tests -> sim -> game, then content/data/layout.json.
        var game = Directory.GetParent(OracleFixtures.Directory)!.Parent!.Parent!.Parent!.FullName;
        return Path.Combine(game, "content", "data", "layout.json");
    }

    private static long L(JsObject o, string key) => ((JsInt)o.Get(key)!).Value;

    private static Bounds BoundsOf(JsObject o) => new(L(o, "minX"), L(o, "maxX"), L(o, "minZ"), L(o, "maxZ"));

    // The same mapping as Keyrun.Bots NavLayouts.ToSim (game/bots/Keyrun.Bots/shared/Follow.cs), read straight from the
    // shipped layout file so the room the test stands in is the room the game builds.
    internal static (NavLayout Nav, List<LayoutRoom> Rooms) Layout()
    {
        var root = (JsObject)JsValue.Parse(File.ReadAllText(LayoutPath()));
        var rooms = ((JsArray)root.Get("boxes")!).Items.Cast<JsObject>().Select(b => new LayoutRoom(
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

    [Fact(DisplayName = "KF3 Seed engage: the gate closes on the tick the party reaches the Seed's aggro ring")]
    public void The_gate_closes_on_the_parked_party()
    {
        var played = StandAtTheDoor();
        var gate = Assert.Single(played.Events, e => e.Type == "gate_closed");
        Assert.Equal("seed-chamber", ((JsString)gate.Get("room")!).Value);
        Assert.Equal(played.StartTick, gate.Tick);
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
}
