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
/// BOSS-SIGHTLINE behavior (R-BOSSROOM-1 (a)): the idle Hollow Warden in the hollow-vault boss arena must not engage a
/// party standing in seed-chamber. The arena and seed-chamber are separated by a wall, so with no line of sight the boss
/// pull must never start: its pull stays unengaged and the Warden never takes a holder. The hollow-vault k2 setup and the
/// shipped layout are built exactly as <see cref="SeedEngageTests"/> builds them; the three party seats stand still in
/// seed-chamber and the run is stepped with no commands.
/// </summary>
public sealed class BossNoAggroThroughWallTests
{
    /// <summary>Ticks the party stands in seed-chamber with no input (400 ticks = 20 s at 20 Hz).</summary>
    public const int IdleTicks = 400;

    private const long Seed = 1;
    private const string BossPullId = "boss";

    private readonly ITestOutputHelper _out;

    /// <summary>Creates the test with xUnit's output sink (the failing tick is printed).</summary>
    /// <param name="output">The sink.</param>
    public BossNoAggroThroughWallTests(ITestOutputHelper output) => _out = output;

    [Fact(DisplayName = "BOSS-SIGHTLINE: the idle Hollow Warden never engages a party standing in seed-chamber (no line of sight)")]
    public void The_idle_Hollow_Warden_does_not_engage_a_party_standing_in_seed_chamber()
    {
        var (nav, rooms) = SeedEngageTests.Layout();
        var chamber = rooms.Single(r => r.Pull == "seed");
        Assert.Equal("seed-chamber", chamber.Id);
        var arena = rooms.Single(r => r.Pull == BossPullId);
        var setup = RunSetup.Load(OracleFixtures.PathOf("hollow-vault-k2.setup.json"), Seed);

        // The boss pull is placed in the arena the same way SeedEngageTests places the Seed: arena origin plus the content offset.
        var bossPull = setup.Dungeon.Pulls.Single(p => p.Id == BossPullId);
        var arenaOrigin = arena.Origin!.Value;
        var placedBoss = bossPull with
        {
            Spawns = bossPull.Spawns.Select(s => s with { Pos = new Vec3(arenaOrigin.X + s.Pos.X, s.Pos.Y, arenaOrigin.Z + s.Pos.Z) }).ToList(),
        };
        var warden = Assert.Single(placedBoss.Spawns);

        // The party stands inside seed-chamber, 1 m in from its west wall, spread along Z.
        var party = setup.Party.Take(3)
            .Select((m, i) => m with { Pos = new Vec3(chamber.Box.MinX + 1000, 0, (i - 1) * 1200), Yaw = 0 })
            .ToList();
        Assert.All(party, m => Assert.True(Movement.Contains(chamber.Box, m.Pos), $"seat {m.Id} at {m.Pos} is not inside seed-chamber"));

        var dungeon = setup.Dungeon with { Pulls = setup.Dungeon.Pulls.Select(p => p.Id == BossPullId ? placedBoss : p).ToArray() };
        var content = setup.Content with { Layout = nav, Bounds = nav.Extent };
        var run = RunRoot.CreateRun(Seed, party, dungeon, content, setup.Options);

        int? engageTick = null;
        for (var i = 0; i < IdleTicks; i++)
        {
            var stepped = RunRoot.StepRun(run, Array.Empty<Command>());
            run = stepped.Run;
            var bossPullStarted = stepped.Events.FirstOrDefault(e => e.Type == "pull_start" && ((JsString)e.Get("pullId")!).Value == BossPullId);
            var holder = run.State.Actors[warden.ActorId].HolderId;
            if (bossPullStarted is not null || Pulls.IsEngaged(run.Encounter, BossPullId) || holder is not null)
            {
                engageTick = run.State.Tick;
                break;
            }
        }

        _out.WriteLine($"idle {IdleTicks} ticks in seed-chamber; Warden actor {warden.ActorId} at {run.State.Actors[warden.ActorId].Pos}; "
            + $"first engage tick: {(engageTick is null ? "none" : engageTick.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))}");

        Assert.True(engageTick is null,
            $"the idle Hollow Warden engaged a party standing in seed-chamber (no line of sight) at tick {engageTick}");
    }

    [Fact(DisplayName = "BOSS-SIGHTLINE CONTROL: a party standing inside the boss arena near the Warden engages it within 400 ticks")]
    public void CONTROL_party_inside_the_arena_engages_the_idle_Hollow_Warden()
    {
        var (nav, rooms) = SeedEngageTests.Layout();
        var arena = rooms.Single(r => r.Pull == BossPullId);
        var setup = RunSetup.Load(OracleFixtures.PathOf("hollow-vault-k2.setup.json"), Seed);

        // Identical boss placement to the seed-chamber fact: arena origin plus the content offset.
        var bossPull = setup.Dungeon.Pulls.Single(p => p.Id == BossPullId);
        var arenaOrigin = arena.Origin!.Value;
        var placedBoss = bossPull with
        {
            Spawns = bossPull.Spawns.Select(s => s with { Pos = new Vec3(arenaOrigin.X + s.Pos.X, s.Pos.Y, arenaOrigin.Z + s.Pos.Z) }).ToList(),
        };
        var warden = Assert.Single(placedBoss.Spawns);

        // Control: the party stands inside the arena close to the Warden (same X, 400 mm apart along Z), so the seats sit within the arena box.
        var party = setup.Party.Take(3)
            .Select((m, i) => m with { Pos = new Vec3(warden.Pos.X, 0, warden.Pos.Z + ((i - 1) * 400)), Yaw = 0 })
            .ToList();
        Assert.All(party, m => Assert.True(Movement.Contains(arena.Box, m.Pos), $"control seat {m.Id} at {m.Pos} is not inside the boss arena"));

        var dungeon = setup.Dungeon with { Pulls = setup.Dungeon.Pulls.Select(p => p.Id == BossPullId ? placedBoss : p).ToArray() };
        var content = setup.Content with { Layout = nav, Bounds = nav.Extent };
        var run = RunRoot.CreateRun(Seed, party, dungeon, content, setup.Options);

        int? engageTick = null;
        for (var i = 0; i < IdleTicks; i++)
        {
            var stepped = RunRoot.StepRun(run, Array.Empty<Command>());
            run = stepped.Run;
            var bossPullStarted = stepped.Events.FirstOrDefault(e => e.Type == "pull_start" && ((JsString)e.Get("pullId")!).Value == BossPullId);
            var holder = run.State.Actors[warden.ActorId].HolderId;
            if (bossPullStarted is not null || Pulls.IsEngaged(run.Encounter, BossPullId) || holder is not null)
            {
                engageTick = run.State.Tick;
                break;
            }
        }

        _out.WriteLine($"CONTROL: party inside the arena, Warden actor {warden.ActorId} at {warden.Pos}; "
            + $"first engage tick: {(engageTick is null ? "none" : engageTick.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))}");

        Assert.True(engageTick is not null,
            $"CONTROL: a party standing inside the boss arena near the Warden never engaged it within {IdleTicks} ticks; the seed-chamber fact is vacuous");
    }
}
