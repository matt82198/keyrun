using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;
using Xunit.Abstractions;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// PULL-PACKS on run C (finding f-82d9390e; Matt 2026-10-20 "the room design is just super lame"; the core dungeon
/// loop defers to WoW Mythic+). Run C's default setup built headless as SimDriver builds it (<see cref="HostRunSetup.Build"/>
/// at key 2, seed 1, bots on, <see cref="BossRules.Arm"/>), seat 1 the autorun <see cref="ScriptedWarden"/> and seats 2
/// and 3 the host's own <see cref="BotCommandSource"/> with run C's careless seat (<c>--careless-seat 3@300:pair</c>),
/// stepped in SimDriver.StepOnce's order, no game launch. The Choir and the Pair each engage as a full pack on one tick (the
/// Choir at least six bodies with its Cantor's Guard, the Pair its five warriors), the default party kills every body of both
/// packs, and it times key 2 inside run C's 7000 ticks.
/// </summary>
public sealed class RunCPullPacksTests
{
    private const int Key = 2;
    private const long Seed = 1;
    private const int MaxTicks = 7000; // run C's --ticks (HudSceneTest $PartyTicks)
    private const int CarelessSeat = 3;
    private const int CarelessFromTick = 300; // HudSceneTest $CarelessSeat '3@300:pair'

    private sealed record Drive(Dictionary<string, List<int>> Engaged, Run Final);

    private static readonly Lazy<Drive> Driven = new(DriveRunC);

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test with xUnit's output sink.</summary>
    /// <param name="output">The sink.</param>
    public RunCPullPacksTests(ITestOutputHelper output) => _output = output;

    private static IEnumerable<int> Ids(SimEvent e) => ((JsArray)e.Get("actorIds")!).Items.Select(v => (int)((JsInt)v).Value);

    private static Drive DriveRunC()
    {
        var registry = ContentLoader.Load();
        var setup = HostRunSetup.Build(registry, "hollow-vault", Key, "warden", withBots: true);
        var start = setup.StartFor(null, null, registry, "warden");
        var created = RunRoot.CreateRun(Seed, setup.PartyFor(setup.Dungeon), setup.Dungeon, setup.Content, start.Options);
        var run = created with { Encounter = BossRules.Arm(created.Encounter, setup.Boss) };
        var pairIds = setup.Dungeon.Pulls.Single(p => p.Id == "pair").Spawns.Select(s => s.ActorId).ToList();
        var careless = new Dictionary<int, CarelessPlan> { [CarelessSeat] = new CarelessPlan(CarelessFromTick, pairIds) };
        var warden = new ScriptedWarden();
        var bots = new BotCommandSource(setup.BotActorIds, KitRegistry.From(registry), Seed, BotCommandSource.DefaultSkillDial, careless);

        // Each teaching pull's pack: every actor engaged on the tick that pull's pull_start fires.
        var engaged = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        while (run.State.Tick < MaxTicks && run.RunEnd.ResultTick is null)
        {
            var tick = run.State.Tick;
            var commands = new List<Command>();
            warden.Sample(tick, run, commands);
            bots.Sample(tick, run, commands);
            var stepped = RunRoot.StepRun(run, commands);
            run = stepped.Run;
            var starts = stepped.Events.Where(e => e.Type == "pull_start").ToList();
            foreach (var s in starts)
            {
                engaged[((JsString)s.Get("pullId")!).Value] = stepped.Events
                    .Where(e => e.Tick == s.Tick && e.Type is "pull_start" or "pack_engage")
                    .SelectMany(Ids)
                    .ToList();
            }
        }
        return new Drive(engaged, run);
    }

    [Theory(DisplayName = "Run C pull packs: the Choir and the Pair each engage as a full pack and the default party kills every body of it")]
    [InlineData("choir", 6)]
    [InlineData("pair", 5)]
    public void Each_teaching_pull_is_a_full_pack_the_party_kills(string pullId, int minBodies)
    {
        var d = Driven.Value;
        Assert.True(d.Engaged.TryGetValue(pullId, out var bodies), $"{pullId} never engaged (run ended {d.Final.RunEnd.Result?.ToString() ?? "unfinished"} at tick {d.Final.State.Tick})");
        _output.WriteLine($"{pullId}: {bodies!.Count} bodies engaged ({string.Join(", ", bodies.Select(id => d.Final.State.Actors[id].ClassId))})");
        Assert.True(bodies.Count >= minBodies, $"{pullId} engaged {bodies.Count} bodies: under the {minBodies}-body pack");
        Assert.All(bodies, id => Assert.False(d.Final.State.Actors[id].Alive, $"{pullId} body {id} ({d.Final.State.Actors[id].ClassId}) survived run C"));
    }

    [Fact(DisplayName = "Run C pull packs: the default party times key 2 inside run C's 7000 ticks")]
    public void The_default_party_times_key_two()
    {
        var d = Driven.Value;
        _output.WriteLine($"result={d.Final.RunEnd.Result?.ToString() ?? "none"} endTick={d.Final.State.Tick}");
        Assert.Equal(RunResult.Timed, d.Final.RunEnd.Result);
    }
}
