using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Keyrun.Tools.Replay;
using Xunit;
using Xunit.Abstractions;

namespace Keyrun.Sim.Tests.Encounter;

/// <summary>
/// SEED-PULL-TEACHES (Matt demo verdict KF3, 2026-10-19: "the Seed elite spawns on entry and the bots cheese it";
/// design/BIBLE.md section 3, every mechanic readable before it lands, and section 9 item 3, the Seed teaches with no
/// stakes). A layout pull spawns on the tick the last seat enters its room (STATE.md D27), so that tick is set by the
/// party, not by the content. The k2 oracle Seed pull is placed into layout.json's seed-chamber as the host places it
/// and the three seats wait <c>wait</c> ticks in corridor-3 before walking in, for every wait across one full Seed
/// Lash period (<see cref="Waits"/>), which puts pull_start at every phase of the Seed's script. Whatever tick the party
/// walks in on, every cast the Seed starts must come after the party was shown that cast's own telegraph inside the pull:
/// an elite whose first slam begins a few ticks after it appears, with its warning lost before it existed, opens with
/// an unreadable hit instead of a readable setup.
/// </summary>
public sealed class SeedOpenerTests
{
    /// <summary>How long each run is watched past pull_start: the Seed's first slam and first lash both fall inside it.</summary>
    public const int WatchTicks = 220;

    /// <summary>Corridor waits swept: 0..195, one k2 Seed Lash period (content every 200, Tempo-scaled to 196).</summary>
    public const int Waits = 196;

    private const long Seed = 1;
    private const int SeedActorId = 108;
    private const long DoorwayX = 62600;

    private readonly ITestOutputHelper _out;

    /// <summary>Creates the test with xUnit's output sink.</summary>
    /// <param name="output">The sink.</param>
    public SeedOpenerTests(ITestOutputHelper output) => _out = output;

    private sealed record Opened(int Wait, int StartTick, List<(int Tick, string Cast, int Resolves)> Telegraphs, List<(int Tick, string Cast)> Casts);

    private static Opened WalkInAfter(int wait)
    {
        var (nav, rooms) = SeedEngageTests.Layout();
        var chamber = rooms.Single(r => r.Pull == "seed");
        var origin = chamber.Origin!.Value;
        var setup = RunSetup.Load(OracleFixtures.PathOf("hollow-vault-k2.setup.json"), Seed);
        var seedPull = setup.Dungeon.Pulls.Single(p => p.Id == "seed");
        var placed = seedPull with
        {
            Spawns = seedPull.Spawns.Select(s => s with { Pos = new Vec3(origin.X + s.Pos.X, s.Pos.Y, origin.Z + s.Pos.Z) }).ToList(),
        };
        var party = setup.Party.Take(3).Select((m, i) => m with { Pos = new Vec3(chamber.Box.MinX - 1000, 0, (i - 1) * 1200), Yaw = 0 }).ToList();
        var dungeon = setup.Dungeon with { Pulls = new[] { placed } };
        var content = setup.Content with { Layout = nav, Bounds = nav.Extent };
        var run = RunRoot.CreateRun(Seed, party, dungeon, content, setup.Options);

        int? start = null;
        var telegraphs = new List<(int, string, int)>();
        var casts = new List<(int, string)>();
        while (run.State.Tick < wait + 400 && (start is null || run.State.Tick <= start.Value + WatchTicks))
        {
            var tick = run.State.Tick;
            var walk = tick < wait
                ? new List<Command>()
                : party.Select(m => run.State.Actors[m.Id])
                    .Where(a => a.Alive && a.Pos.X < DoorwayX)
                    .Select(a => (Command)new MoveCommand(tick, a.Id, new Dir2(1000, 0), 0))
                    .ToList();
            var stepped = RunRoot.StepRun(run, walk);
            run = stepped.Run;
            foreach (var e in stepped.Events)
            {
                if (start is null && e.Type == "pull_start")
                {
                    start = e.Tick;
                }
                if (e.Type == "telegraph" && e.Get("actorId") is JsInt a && a.Value == SeedActorId)
                {
                    telegraphs.Add((e.Tick, ((JsString)e.Get("cast")!).Value, (int)((JsInt)e.Get("resolvesTick")!).Value));
                }
            }
            if (run.State.Actors.TryGetValue(SeedActorId, out var s) && s.Alive && s.CastId is { } castId && s.CastStartTick is { } castStart &&
                !casts.Contains((castStart, castId)))
            {
                casts.Add((castStart, castId));
            }
        }
        Assert.True(start is not null, $"wait {wait}: pull_start never fired with the party inside the seed-chamber");
        return new Opened(wait, start!.Value, telegraphs, casts);
    }

    [Fact(DisplayName = "Seed opener: whatever tick the party walks in, every Seed cast follows its own telegraph shown inside the pull")]
    public void Every_seed_cast_follows_its_own_telegraph_whenever_the_party_enters()
    {
        var failures = new List<string>();
        var castsSeen = 0;
        var idlePhases = new List<int>();
        for (var wait = 0; wait < Waits; wait++)
        {
            var o = WalkInAfter(wait);
            castsSeen += o.Casts.Count;
            if (o.Casts.Count == 0)
            {
                idlePhases.Add(wait);
            }
            if (wait % 49 == 0)
            {
                _out.WriteLine($"wait {wait}: pull_start {o.StartTick}; casts {string.Join(", ", o.Casts.Select(c => $"{c.Cast}@{c.Tick}"))}; telegraphs {string.Join(", ", o.Telegraphs.Select(t => $"{t.Cast}@{t.Tick}->{t.Resolves}"))}");
            }
            foreach (var (tick, cast) in o.Casts)
            {
                if (!o.Telegraphs.Any(t => t.Cast == cast && t.Resolves == tick && t.Tick >= o.StartTick))
                {
                    failures.Add(string.Create(CultureInfo.InvariantCulture,
                        $"wait {wait}: {cast} started at tick {tick}, {tick - o.StartTick} ticks after pull_start {o.StartTick}, with no telegraph of its own"));
                }
            }
        }
        _out.WriteLine($"{Waits} entry phases, {castsSeen} Seed casts, {failures.Count} unannounced");
        foreach (var f in failures.Take(10))
        {
            _out.WriteLine(f);
        }
        // Non-vacuity: every phase must see the Seed cast, or an idle Seed would pass by default.
        Assert.True(idlePhases.Count == 0, $"the Seed cast nothing within {WatchTicks} ticks of pull_start at waits {string.Join(",", idlePhases.Take(10))}");
        Assert.True(failures.Count == 0, $"{failures.Count} unannounced Seed casts, first: {string.Join("; ", failures.Take(3))}");
    }
}
