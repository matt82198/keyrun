using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Keyrun.Tools.Replay;
using Xunit;
using Xunit.Abstractions;
using SimDungeon = Keyrun.Sim.Encounter.DungeonDef;
using SimPull = Keyrun.Sim.Encounter.PullDef;

namespace Keyrun.Bots.Tests;

/// <summary>
/// PRESPAWN-AGGRO boss ring (design/BIBLE.md section 9.1, PR #308 run C). Every group, the boss included, engages on its
/// aggro radius, and content may set the radius per group. With the default 4000 mm ring around the arena origin the
/// boss pull started only when the Warden reached x ~89926; on main (room-entry spawn) it started at the arena entry,
/// x ~86418. The shipped layout must give the boss a ring that engages at about that same point, through
/// <see cref="NavLayouts.PlaceDungeon"/> (the path the host's HostRunSetup also takes), and its ring must not reach the
/// Seed chamber, so a party fighting the Seed never pulls the boss with it. PATH-TO-BOSS (STATE.md D44) removed the
/// Hollow Door (no corridor is gated), so the check is the ring's reach from the chamber's east wall, not a shut door.
/// </summary>
public sealed class BossAggroRingTests
{
    private const long Seed = 1;

    /// <summary>Where the boss pull_start came on main (arena entry), x mm. Literal on purpose.</summary>
    private const long MainEngageX = 86418;

    private const long ToleranceMm = 500;

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test with xUnit's output sink.</summary>
    /// <param name="output">The sink.</param>
    public BossAggroRingTests(ITestOutputHelper output) => _output = output;

    // The shipped content placed as the host places it (NavLayouts.PlaceDungeon), keeping only the pulls named.
    private static (Run Run, SimDungeon Dungeon) ShippedRun(string[] keep, long startX, Func<SimDungeon, SimDungeon>? edit = null)
    {
        var registry = ContentLoader.Load();
        var layoutDef = registry.Layout;
        var nav = NavLayouts.ToSim(layoutDef);
        var setup = RunSetup.Load(Path.Combine(RolesOracleTraceTests.OracleDir(), "hollow-vault-k2.setup.json"), Seed);
        SimDungeon placed = NavLayouts.PlaceDungeon(layoutDef, setup.Dungeon);
        SimDungeon dungeon = placed with { Pulls = placed.Pulls.Where(p => keep.Contains(p.Id)).ToList() };
        if (edit is not null)
        {
            dungeon = edit(dungeon);
        }
        var party = setup.Party.Take(3).Select((m, i) => m with { Pos = new Vec3(startX, 0, (i - 1) * 1200), Yaw = 0 }).ToList();
        var content = setup.Content with { Layout = nav, Bounds = nav.Extent };
        return (RunRoot.CreateRun(Seed, party, dungeon, content, setup.Options), dungeon);
    }

    private static List<Command> WalkEast(Run run) =>
        new[] { 1, 2, 3 }.Where(id => run.State.Actors[id].Alive)
            .Select(id => (Command)new MoveCommand(run.State.Tick, id, new Dir2(1000, 0), 0)).ToList();

    [Fact(DisplayName = "Boss aggro ring (run C, PR #308): walking in past the Hollow Door, the boss engages with the engaging member at the arena entry (x 86418 +-500), as on main")]
    public void The_boss_engages_at_the_arena_entry()
    {
        // Earlier pulls dropped, so their gates count as cleared (a --pull N start): the Hollow Door is open.
        var (run, dungeon) = ShippedRun(new[] { "boss" }, startX: 79000);
        var boss = dungeon.Pulls.Single(p => p.Id == "boss");
        var center = Pulls.RingCenter(boss);
        bool InRing(Run r, int id) => r.State.Actors[id].Alive && Aggro.InRing(r.State.Actors[id].Pos, center, boss.AggroMm);

        int? engage = null;
        while (engage is null && run.State.Tick < 600)
        {
            var tick = run.State.Tick;
            Assert.False(new[] { 1, 2, 3 }.Any(id => InRing(run, id)), $"tick {tick}: a party member stands in the boss ring before its engage");
            var stepped = RunRoot.StepRun(run, WalkEast(run));
            run = stepped.Run;
            if (stepped.Events.Any(e => e.Type == "pull_start" && e.Get("pullId") is JsString s && s.Value == "boss"))
            {
                engage = tick;
            }
        }
        Assert.NotNull(engage);
        var engaging = new[] { 1, 2, 3 }.Where(id => InRing(run, id)).Select(id => run.State.Actors[id].Pos).ToList();
        _output.WriteLine($"boss engage t{engage}, ring centre {center.X},{center.Z} radius {boss.AggroMm}; engaging members at x {string.Join(",", engaging.Select(p => p.X))}");
        Assert.NotEmpty(engaging);
        Assert.All(engaging, p => Assert.True(Math.Abs(p.X - MainEngageX) <= ToleranceMm,
            $"the boss engaged with a member at x {p.X}; main engaged at x {MainEngageX} (+-{ToleranceMm})"));
    }

    [Fact(DisplayName = "Boss aggro ring: PlaceDungeon gives the host-built boss pull (HostRunSetup.ToSimDungeon shape) the arena's aggroMm, and trash pulls keep the default")]
    public void PlaceDungeon_carries_the_arena_radius_onto_the_host_boss_pull()
    {
        var registry = ContentLoader.Load();
        var arena = registry.Layout.Boxes.Single(b => b.Pull == LayoutDef.BossPullId);
        Assert.NotNull(arena.AggroMm);
        // HostRunSetup.cs ToSimDungeon builds the boss pull at the origin with no radius, then Build calls PlaceDungeon.
        var hostShaped = new SimDungeon("hollow-vault", new[]
        {
            new SimPull("seed", new[] { new PullSpawn(108, "x", new Vec3(10000, 0, 0), 0, 1) }),
            new SimPull(LayoutDef.BossPullId, new[] { new PullSpawn(900, "x", Vec3.Zero, 0, 1) }),
        });
        var placed = NavLayouts.PlaceDungeon(registry.Layout, hostShaped);
        Assert.Equal(arena.AggroMm!.Value, placed.Pulls.Single(p => p.Id == LayoutDef.BossPullId).AggroMm);
        Assert.Equal(Pulls.DefaultAggroMm, placed.Pulls.Single(p => p.Id == "seed").AggroMm);
    }

    // The party stands at the Seed chamber's east wall (x 75800, the chamber ends at 76000) for 300 ticks with the Seed
    // alive and fighting it; true if the boss engaged. With no Hollow Door (D44) nothing holds a walker here, so the
    // party stands rather than walking east: the check is how far the boss ring reaches, not whether a door holds.
    private static bool BossEngagesFromTheSeedChamber(Func<SimDungeon, SimDungeon>? edit, ITestOutputHelper output)
    {
        var (run, dungeon) = ShippedRun(new[] { "seed", "boss" }, startX: 75800, edit);
        var bossIds = dungeon.Pulls.Single(p => p.Id == "boss").Spawns.Select(s => s.ActorId).ToList();
        var seedIds = dungeon.Pulls.Single(p => p.Id == "seed").Spawns.Select(s => s.ActorId).ToList();
        for (var i = 0; i < 300; i++)
        {
            var stepped = RunRoot.StepRun(run, Array.Empty<Command>());
            run = stepped.Run;
            Assert.True(seedIds.Any(id => run.State.Actors[id].Alive), "the Seed died, so the party is no longer fighting it in its chamber");
            Assert.All(new[] { 1, 2, 3 }, id => Assert.True(run.State.Actors[id].Pos.X <= 76000,
                $"seat {id} at x {run.State.Actors[id].Pos.X} left the Seed chamber"));
            if (stepped.Events.Any(e => e.Type == "pull_start" && e.Get("pullId") is JsString s && s.Value == "boss") ||
                bossIds.Any(id => !run.State.Actors[id].Idle))
            {
                output.WriteLine($"boss engaged at tick {run.State.Tick - 1} from the Seed chamber");
                return true;
            }
        }
        return false;
    }

    [Fact(DisplayName = "Boss aggro ring (BIBLE 9.1, D44): with the Seed alive a party at the Seed chamber's east wall never engages the boss; a ring reaching the chamber would")]
    public void The_boss_never_engages_from_the_seed_chamber()
    {
        Assert.False(BossEngagesFromTheSeedChamber(null, _output), "the shipped boss ring reaches the Seed chamber");

        // Control: a ring that reaches the Seed chamber (93500 - 18000 = 75500) does engage from it, so the assertion
        // above can see a ring that is too large.
        Assert.True(BossEngagesFromTheSeedChamber(
            d => d with { Pulls = d.Pulls.Select(p => p.Id == "boss" ? p with { AggroMm = 18000 } : p).ToList() }, _output),
            "control: an 18000 mm boss ring did not engage from the Seed chamber; the check is vacuous");
    }
}
