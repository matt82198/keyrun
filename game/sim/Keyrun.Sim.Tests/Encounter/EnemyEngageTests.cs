using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Keyrun.Tools.Replay;
using Xunit;
using Xunit.Abstractions;

namespace Keyrun.Sim.Tests.Encounter;

/// <summary>
/// ENEMY-ENGAGE (Matt 2026-10-20: "enemies need to stack on the player they are attacking as well lmfao, they just
/// face a random direction and swing the air"; design/BIBLE.md s3: the Cleave is "readable from the boss's facing").
/// A melee enemy with a target closes until it is inside its attack range of THAT target, faces it (the sim yaw the
/// view draws and frontal cones read), and only lands its swing in range. Drives the shipped content (the hollow-vault
/// k2 oracle setup: choir.melee, speed 207, choir.meleeSwing range 3000) through <see cref="RunRoot.StepRun"/>; the
/// party never casts, so the Acolyte's target is the nearest player, the tank.
/// </summary>
public sealed class EnemyEngageTests
{
    private const long Seed = 1;
    private const int TankId = 1;
    private const int AcolyteId = 100;
    private const long SwingRangeMm = 3000;

    /// <summary>Facing tolerance in 1024ths of a turn (about 5.6 degrees).</summary>
    private const long YawTolerance = 16;

    /// <summary>6 m at 207 mm/tick closes inside 3 m in 15 ticks; 40 leaves room for the first swing.</summary>
    private const int CloseWithinTicks = 40;

    private readonly ITestOutputHelper _out;

    /// <summary>Creates the test with xUnit's output sink.</summary>
    /// <param name="output">The sink.</param>
    public EnemyEngageTests(ITestOutputHelper output) => _out = output;

    private sealed record Tick(int At, ActorState Before, ActorState TankBefore, ActorState After, ActorState TankAfter, List<SimEvent> Events);

    /// <summary>
    /// One Acolyte at <paramref name="acolyte"/> spawned facing <paramref name="acolyteYaw"/>, the tank at the origin and
    /// the other four seats parked 15 m behind it. <paramref name="tankMoves"/> maps a tick to a tank move direction.
    /// </summary>
    private static List<Tick> Play(Vec3 acolyte, int acolyteYaw, int ticks, Dictionary<int, Dir2>? tankMoves = null)
    {
        var setup = RunSetup.Load(OracleFixtures.PathOf("hollow-vault-k2.setup.json"), Seed);
        var choir = setup.Dungeon.Pulls.Single(p => p.Id == "choir");
        var spawn = choir.Spawns.Single(s => s.ActorId == AcolyteId) with { Pos = acolyte, Yaw = acolyteYaw };
        Assert.Equal("choir.melee", spawn.ClassId);
        var dungeon = setup.Dungeon with { Pulls = new[] { choir with { Spawns = new[] { spawn } } } };
        var party = setup.Party.Select(m => m.Id == TankId
            ? m with { Pos = Vec3.Zero, Yaw = 0 }
            : m with { Pos = new Vec3(-15000 - (m.Id * 1000), 0, 0), Yaw = 0 }).ToList();
        var run = RunRoot.CreateRun(Seed, party, dungeon, setup.Content, setup.Options);
        var played = new List<Tick>();
        for (var i = 0; i < ticks; i++)
        {
            var tick = run.State.Tick;
            var before = run.State.Actors[AcolyteId].Clone();
            var tankBefore = run.State.Actors[TankId].Clone();
            var commands = new List<Command>();
            if (tankMoves is not null && tankMoves.TryGetValue(tick, out var dir))
            {
                commands.Add(new MoveCommand(tick, TankId, dir, 0));
            }
            var stepped = RunRoot.StepRun(run, commands);
            run = stepped.Run;
            played.Add(new Tick(tick, before, tankBefore, run.State.Actors[AcolyteId].Clone(), run.State.Actors[TankId].Clone(), stepped.Events.ToList()));
        }
        return played;
    }

    private static long Dist(ActorState a, ActorState b) => ScriptTarget.RangeMm(a, b);

    private static long FacingError(ActorState from, ActorState to) =>
        System.Math.Abs(Fixed.AngleDiff(from.Yaw, Fixed.YawBetween(from.Pos, to.Pos)));

    private static IEnumerable<(Tick Tick, SimEvent Hit)> AcolyteHits(List<Tick> played) =>
        played.SelectMany(t => t.Events.Where(e => e.Type == "damage" && ((JsInt)e.Get("sourceId")!).Value == AcolyteId).Select(e => (t, e)));

    [Fact(DisplayName = "ENEMY-ENGAGE: a melee enemy 6 m from its target closes inside its swing range, faces it and swings")]
    public void A_melee_enemy_closes_faces_and_swings()
    {
        var played = Play(new Vec3(6000, 0, 0), 256, CloseWithinTicks + 40);
        var end = played[CloseWithinTicks - 1];
        _out.WriteLine($"t{end.At}: acolyte {end.After.Pos} yaw {end.After.Yaw}, tank {end.TankAfter.Pos}, gap {Dist(end.After, end.TankAfter)} mm, facing error {FacingError(end.After, end.TankAfter)}");
        Assert.True(Dist(end.After, end.TankAfter) <= SwingRangeMm, $"still {Dist(end.After, end.TankAfter)} mm from the tank after {CloseWithinTicks} ticks");
        Assert.True(FacingError(end.After, end.TankAfter) <= YawTolerance, $"yaw {end.After.Yaw} is {FacingError(end.After, end.TankAfter)}/1024 off the tank");
        Assert.NotEmpty(AcolyteHits(played));
    }

    [Fact(DisplayName = "ENEMY-ENGAGE: a melee enemy already in range of its target turns to face it before it keeps swinging")]
    public void An_in_range_melee_enemy_turns_to_face_its_target()
    {
        // Spawned 2 m from the tank, inside its stop line, facing directly away (+x, yaw 0; the tank is at -x).
        var played = Play(new Vec3(2000, 0, 0), 0, 60);
        var worst = played.Skip(2).Select(t => (t, Err: FacingError(t.After, t.TankAfter))).OrderByDescending(p => p.Err).First();
        _out.WriteLine($"worst facing error {worst.Err}/1024 at t{worst.t.At}: acolyte {worst.t.After.Pos} yaw {worst.t.After.Yaw}, tank {worst.t.TankAfter.Pos}");
        Assert.True(worst.Err <= YawTolerance,
            $"t{worst.t.At}: the Acolyte at {worst.t.After.Pos} faces yaw {worst.t.After.Yaw}, {worst.Err}/1024 off the tank at {worst.t.TankAfter.Pos} it is hitting");
    }

    [Fact(DisplayName = "ENEMY-ENGAGE: a melee enemy keeps facing a target that sidesteps inside its range")]
    public void A_melee_enemy_tracks_a_sidestepping_target()
    {
        // The Acolyte closes from 6 m, then the tank steps 8 ticks along +z and +x, staying inside the stop line.
        var moves = Enumerable.Range(40, 8).ToDictionary(t => t, _ => new Dir2(1000, 1000));
        var played = Play(new Vec3(6000, 0, 0), 256, 70, moves);
        var after = played.Where(t => t.At >= 50).ToList();
        var worst = after.Select(t => (t, Err: FacingError(t.After, t.TankAfter))).OrderByDescending(p => p.Err).First();
        _out.WriteLine($"worst facing error {worst.Err}/1024 at t{worst.t.At}: acolyte {worst.t.After.Pos} yaw {worst.t.After.Yaw}, tank {worst.t.TankAfter.Pos}, gap {Dist(worst.t.After, worst.t.TankAfter)}");
        Assert.All(after, t => Assert.True(Dist(t.After, t.TankAfter) <= SwingRangeMm, $"t{t.At}: gap {Dist(t.After, t.TankAfter)}"));
        Assert.True(worst.Err <= YawTolerance,
            $"t{worst.t.At}: the Acolyte at {worst.t.After.Pos} faces yaw {worst.t.After.Yaw}, {worst.Err}/1024 off the tank at {worst.t.TankAfter.Pos}");
    }

    [Fact(DisplayName = "ENEMY-ENGAGE: a melee enemy never lands a swing on a target outside its range")]
    public void No_melee_hit_lands_out_of_range()
    {
        var played = Play(new Vec3(6000, 0, 0), 256, 120);
        var hits = AcolyteHits(played).ToList();
        Assert.NotEmpty(hits);
        foreach (var (t, hit) in hits)
        {
            Assert.Equal(TankId, (int)((JsInt)hit.Get("targetId")!).Value);
            Assert.True(Dist(t.Before, t.TankBefore) <= SwingRangeMm, $"t{t.At}: swing landed from {Dist(t.Before, t.TankBefore)} mm");
        }
    }
}
