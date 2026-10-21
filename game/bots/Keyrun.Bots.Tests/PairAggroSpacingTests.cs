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

namespace Keyrun.Bots.Tests;

/// <summary>
/// PRESPAWN-AGGRO x PATH-TO-BOSS (PR #308; BIBLE section 9.1; orchestrator: "a pack aggros only when a party member
/// enters ITS aggro radius, it damages them, or a linked member of the same pack is pulled"). On the shipped layout a
/// party fighting the Choir anywhere in the Choir's room stands outside the Pair's ring, the two pulls are not linked,
/// and a long Choir fight never engages the Pair; the Pair engages on the first step a party member stands on its
/// ring's edge (inclusive), and not one millimetre short of it.
/// </summary>
public sealed class PairAggroSpacingTests
{
    private const long Seed = 1;
    private const int WardenId = 1;

    /// <summary>How long the party fights the Choir in place (30 s at 20 Hz; the bot party clears it in about 1830).</summary>
    private const int FightTicks = 600;

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test with xUnit's output sink (measured distances are printed for the PR).</summary>
    /// <param name="output">The sink.</param>
    public PairAggroSpacingTests(ITestOutputHelper output) => _output = output;

    private static (Run Run, NavLayout Nav) Setup()
    {
        var registry = ContentLoader.Load();
        var layoutDef = registry.Layout;
        var nav = NavLayouts.ToSim(layoutDef);
        var setup = RunSetup.Load(Path.Combine(RolesOracleTraceTests.OracleDir(), "hollow-vault-k2.setup.json"), Seed);
        SimDungeon dungeon = NavLayouts.PlaceDungeon(layoutDef, setup.Dungeon);
        var party = NavLayouts.PlaceParty(layoutDef, setup.Party.Take(3).ToList(), dungeon.Pulls[0].Id);
        var run = RunRoot.CreateRun(Seed, party, dungeon, setup.Content with { Layout = nav, Bounds = nav.Extent }, setup.Options);
        return (run, nav);
    }

    private static bool Engaged(Run run, string id) => Pulls.IsEngaged(run.Encounter, id);

    [Fact(DisplayName = "Pair spacing: every point of the Choir's room is outside the Pair's aggro ring, and the Choir and the Pair are not linked")]
    public void The_choir_room_is_outside_the_pair_ring()
    {
        var (run, nav) = Setup();
        var choir = run.Dungeon.Pulls.Single(p => p.Id == "choir");
        var pair = run.Dungeon.Pulls.Single(p => p.Id == "pair");
        var room = nav.Boxes.Single(b => b.Id == "choir-room").Box;
        var pc = Pulls.RingCenter(pair);
        var cc = Pulls.RingCenter(choir);
        // The room point nearest the Pair's ring centre (clamped into the box): if it is outside the ring, all are.
        var nearest = new Vec3(Math.Clamp(pc.X, room.MinX, room.MaxX), 0, Math.Clamp(pc.Z, room.MinZ, room.MaxZ));
        var gap = Keyrun.Sim.Combat.Movement.Isqrt(((nearest.X - pc.X) * (nearest.X - pc.X)) + ((nearest.Z - pc.Z) * (nearest.Z - pc.Z)));
        var centres = Keyrun.Sim.Combat.Movement.Isqrt(((cc.X - pc.X) * (cc.X - pc.X)) + ((cc.Z - pc.Z) * (cc.Z - pc.Z)));
        _output.WriteLine($"choir ring ({cc.X},{cc.Z}) r={choir.AggroMm}; pair ring ({pc.X},{pc.Z}) r={pair.AggroMm}; centres {centres} mm apart; " +
            $"nearest choir-room point to the pair ring ({nearest.X},{nearest.Z}) is {gap} mm from its centre ({gap - pair.AggroMm} mm outside the ring)");
        Assert.False(Aggro.InRing(nearest, pc, pair.AggroMm), $"the Choir's room point ({nearest.X},{nearest.Z}) is inside the Pair's {pair.AggroMm} mm ring");
        Assert.DoesNotContain("pair", choir.Links);
        Assert.DoesNotContain("choir", pair.Links);
    }

    [Fact(DisplayName = "Pair spacing: a party fighting the Choir in place for 600 ticks never engages the Pair; a member on the Pair's ring edge engages it, one mm short does not")]
    public void Fighting_the_choir_never_engages_the_pair_until_a_member_enters_its_ring()
    {
        var (run, _) = Setup();
        var pair = run.Dungeon.Pulls.Single(p => p.Id == "pair");
        var pc = Pulls.RingCenter(pair);
        foreach (var p in run.State.Actors.Values.Where(a => a.Kind == ActorKind.Player))
        {
            p.MaxHealth = 1_000_000;
            p.Health = 1_000_000;
        }
        // The tank steps into the Choir's ring (its origin 21500, 0): the Choir engages and the fight is on.
        run.State.Actors[WardenId].Pos = new Vec3(19000, 0, 0);
        var maxPairX = long.MinValue;
        for (var t = 0; t < FightTicks; t++)
        {
            run = RunRoot.StepRun(run, Array.Empty<Command>()).Run;
            maxPairX = Math.Max(maxPairX, run.State.Actors.Values.Where(a => a.Kind == ActorKind.Player).Max(a => a.Pos.X));
            Assert.False(Engaged(run, "pair"), $"the Pair engaged at tick {run.State.Tick} during the Choir fight");
        }
        Assert.True(Engaged(run, "choir"), "the Choir never engaged: the fight this test needs never happened");
        Assert.Contains(run.State.Actors.Values, a => a.Kind == ActorKind.Player && a.Health < a.MaxHealth);
        _output.WriteLine($"after {FightTicks} ticks of the Choir fight: the easternmost party member reached x {maxPairX}; the Pair ring starts at x {pc.X - pair.AggroMm}");

        // One mm short of the ring edge: still idle.
        var chanterId = run.State.Actors.Values.First(a => a.Kind == ActorKind.Player && a.Id != WardenId).Id;
        run.State.Actors[chanterId].Pos = new Vec3(pc.X - pair.AggroMm - 1, 0, pc.Z);
        run = RunRoot.StepRun(run, Array.Empty<Command>()).Run;
        Assert.False(Engaged(run, "pair"), "the Pair engaged on a member 1 mm outside its ring");

        // On the edge (inclusive): it engages.
        run.State.Actors[chanterId].Pos = new Vec3(pc.X - pair.AggroMm, 0, pc.Z);
        run = RunRoot.StepRun(run, Array.Empty<Command>()).Run;
        Assert.True(Engaged(run, "pair"), "a member on the Pair's ring edge did not engage it");
    }
}
