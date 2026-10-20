using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Tools.Replay;
using Xunit;
using Xunit.Abstractions;
using SimDungeon = Keyrun.Sim.Encounter.DungeonDef;
using SimPull = Keyrun.Sim.Encounter.PullDef;

namespace Keyrun.Bots.Tests;

/// <summary>
/// PACK-SPAWN-SPREAD (design/BIBLE.md: the Choir is 2 melee and a caster lieutenant, the Pair is 5 melee). Content gives
/// each enemy GROUP one spawn point (content/data/dungeons/hollow-vault.json: the Pair's 5 melee all at x 5000, z 0), and
/// the host spawns <c>count</c> actors on it, so before this lane every body of a group stood on one point and the 3D run
/// showed fewer enemies than the pull has. After <see cref="NavLayouts.PlaceDungeon"/> and <see cref="Pulls.SpawnPull"/>
/// (the path the host takes), every enemy of every pull stands at least one body diameter (2 x run.json radiusMm) from
/// every other, inside its room, and the placement is a pure function of the content (no seed), so oracles stay stable.
/// </summary>
public sealed class PackFormationTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test with xUnit's output sink (the spawn positions are printed for the PR).</summary>
    /// <param name="output">The sink.</param>
    public PackFormationTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// The host's dungeon shape from content (HostRunSetup.ToSimDungeon: pull, group, count order, every actor of a group
    /// on the group's spawn point), boss appended at the layout's boss pull id.
    /// </summary>
    private static SimDungeon ContentDungeon(ContentRegistry registry)
    {
        var def = registry.Dungeons["hollow-vault"];
        var nextId = 100;
        var pulls = new List<SimPull>();
        foreach (var pull in def.Pulls)
        {
            var spawns = new List<PullSpawn>();
            foreach (var group in pull.Enemies)
            {
                for (var i = 0; i < group.Count; i++)
                {
                    spawns.Add(new PullSpawn(nextId++, group.EnemyId, new Vec3(group.Spawn.X, 0, group.Spawn.Z), 0, 1000));
                }
            }
            pulls.Add(new SimPull(pull.Id, spawns));
        }
        pulls.Add(new SimPull(LayoutDef.BossPullId, new[] { new PullSpawn(99, def.Boss.EnemyId, Vec3.Zero, 0, 1000) }));
        return new SimDungeon(def.Id, pulls);
    }

    private static long Dist(Vec3 a, Vec3 b)
    {
        var dx = a.X - b.X;
        var dz = a.Z - b.Z;
        return (long)Math.Sqrt((double)(dx * dx) + (dz * dz));
    }

    private void AssertSpread(SimDungeon placed, long diameter, LayoutDef layout)
    {
        var stacked = new List<string>();
        for (var p = 0; p < placed.Pulls.Count; p++)
        {
            var actors = Pulls.SpawnPull(placed, p, 0, _ => { });
            _output.WriteLine($"{placed.Pulls[p].Id}: " + string.Join(" ", actors.Select(a => $"{a.Id}@({a.Pos.X},{a.Pos.Z})")));
            Assert.Equal(placed.Pulls[p].Spawns.Count, actors.Count);
            var room = layout.Boxes.First(b => string.Equals(b.Pull, placed.Pulls[p].Id, StringComparison.Ordinal) && b.Origin is not null);
            foreach (var a in actors)
            {
                Assert.InRange(a.Pos.X, room.MinX, room.MaxX);
                Assert.InRange(a.Pos.Z, room.MinZ, room.MaxZ);
            }
            for (var i = 0; i < actors.Count; i++)
            {
                for (var j = i + 1; j < actors.Count; j++)
                {
                    var d = Dist(actors[i].Pos, actors[j].Pos);
                    if (d < diameter)
                    {
                        stacked.Add($"{placed.Pulls[p].Id} {actors[i].Id}/{actors[j].Id} {d}mm");
                    }
                }
            }
        }
        Assert.True(stacked.Count == 0,
            $"{stacked.Count} enemy pairs stand under one body diameter ({diameter} mm) apart: " + string.Join(", ", stacked));
    }

    /// <summary>Every enemy of every content pull, as the host places it, stands one body diameter from every other.</summary>
    [Fact]
    public void EveryPullSpawnsItsEnemiesOneBodyApart()
    {
        var registry = ContentLoader.Load();
        var diameter = 2L * registry.Run.Movement.RadiusMm;
        var placed = NavLayouts.PlaceDungeon(registry.Layout, ContentDungeon(registry));
        Assert.Equal(5, placed.Pulls.Single(p => p.Id == "pair").Spawns.Count);
        AssertSpread(placed, diameter, registry.Layout);
    }

    /// <summary>The bots' oracle setup (the dungeon the bot goldens place) spreads the same way.</summary>
    [Fact]
    public void OracleSetupDungeonSpawnsOneBodyApart()
    {
        var registry = ContentLoader.Load();
        var setup = RunSetup.Load(Path.Combine(RolesOracleTraceTests.OracleDir(), "hollow-vault-k2.setup.json"), 1);
        var placed = NavLayouts.PlaceDungeon(registry.Layout, setup.Dungeon);
        AssertSpread(placed, 2L * registry.Run.Movement.RadiusMm, registry.Layout);
    }

    /// <summary>
    /// The formation is a pure function of the content: two placements are identical, and a lone enemy (the Choir's
    /// lieutenant, the Seed) still stands exactly on its room origin plus its content offset.
    /// </summary>
    [Fact]
    public void FormationIsDeterministicAndLeavesLoneSpawnsOnTheirPoint()
    {
        var registry = ContentLoader.Load();
        var a = NavLayouts.PlaceDungeon(registry.Layout, ContentDungeon(registry));
        var b = NavLayouts.PlaceDungeon(registry.Layout, ContentDungeon(registry));
        Assert.Equal(
            a.Pulls.SelectMany(p => p.Spawns).Select(s => (s.ActorId, s.Pos.X, s.Pos.Z)),
            b.Pulls.SelectMany(p => p.Spawns).Select(s => (s.ActorId, s.Pos.X, s.Pos.Z)));

        var seedRoom = registry.Layout.Boxes.First(x => x.Pull == "seed" && x.Origin is not null).Origin!;
        var seed = a.Pulls.Single(p => p.Id == "seed").Spawns.Single();
        Assert.Equal((seedRoom.X + 10000L, 0L), (seed.Pos.X, seed.Pos.Z));

        var choirRoom = registry.Layout.Boxes.First(x => x.Pull == "choir" && x.Origin is not null).Origin!;
        var lieutenant = a.Pulls.Single(p => p.Id == "choir").Spawns.Single(s => s.ClassId == "choir.lieutenant");
        Assert.Equal((choirRoom.X + 0L, choirRoom.Z - 1000L), (lieutenant.Pos.X, lieutenant.Pos.Z));
    }
}
