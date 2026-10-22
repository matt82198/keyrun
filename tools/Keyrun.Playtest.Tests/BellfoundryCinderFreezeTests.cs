using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Core;
using Keyrun.Sim.Runs;
using Xunit;
using Xunit.Abstractions;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// f-fa9da3ca: the cinder bot froze after the first Sunken Bellfoundry boss died (orchestrator capture bell-70fef57d,
/// <c>--autorun --ticks 30000 --key 2 --seed 1 --dungeon sunken-bellfoundry</c>: boss_death of Foundry-Master Ommet at tick
/// 7114, then no cinder cast or event again while the party walked on). This replica runs the arena's own seats the way
/// SimDriver does (ScriptedWarden on seat 1, the host's BotCommandSource on seats 2 and 3, the dungeon seeded by the run
/// seed), so the freeze shows here in dotnet test without Godot.
/// </summary>
public sealed class BellfoundryCinderFreezeTests
{
    private const string DungeonId = "sunken-bellfoundry";
    private const int KeyLevel = 2;
    private const long Seed = 1;
    private const int Ticks = 30000;
    private const string CinderClass = "cinder";

    private readonly ITestOutputHelper _out;

    /// <summary>Creates the test with xUnit's output sink.</summary>
    /// <param name="output">The sink.</param>
    public BellfoundryCinderFreezeTests(ITestOutputHelper output) => _out = output;

    /// <summary>One replica run: every emitted event with its step tick, and the seat-3 cinder's actor id.</summary>
    private sealed record Replica(IReadOnlyList<(int Tick, SimEvent Event)> Events, int CinderId, RunEndState RunEnd, int FinalTick);

    private static Replica Play()
    {
        var registry = ContentLoader.Load();
        var setup = HostRunSetup.Build(registry, DungeonId, KeyLevel, "warden", withBots: true, dungeonSeed: Seed);
        var run = HostRunFactory.Create(setup, new RunRequest(Seed, registry, "warden")).Run;
        var warden = new ScriptedWarden();
        var bots = new BotCommandSource(setup.BotActorIds, KitRegistry.From(registry), Seed);
        var cinderId = setup.Party.First(p => p.ClassId == CinderClass).Id;
        var events = new List<(int Tick, SimEvent Event)>();
        for (var n = 0; n < Ticks && run.RunEnd.Result is null; n++)
        {
            var preTick = run.State.Tick;
            var commands = new List<Command>();
            warden.Sample(preTick, run, commands, new List<int>());
            bots.Sample(preTick, run, commands);
            bots.TakeReactions();
            var stepped = RunRoot.StepRun(run, commands);
            run = stepped.Run;
            events.AddRange(stepped.Events.Select(e => (preTick, e)));
        }
        return new Replica(events, cinderId, run.RunEnd, run.State.Tick);
    }

    private static int? ActorOf(SimEvent e) => e.Get("actorId") is JsInt { } a ? (int)a.Value : null;

    [Fact(DisplayName = "Sunken Bellfoundry (key 2, seed 1, autorun seats): the cinder acts again after the first boss dies (f-fa9da3ca)")]
    public void Cinder_keeps_casting_after_a_non_final_boss_death()
    {
        var replica = Play();
        var firstBossDeath = replica.Events.FirstOrDefault(x => x.Event.Type == "boss_death" && x.Event.Get("final") is JsBool { Value: false });
        Assert.True(firstBossDeath.Event is not null, $"no non-final boss_death in {Ticks} ticks (run ended {replica.RunEnd.Result} at {replica.RunEnd.ResultTick})");

        var cinderCasts = replica.Events.Where(x => x.Tick > firstBossDeath.Tick
            && x.Event.Type == "cast_complete" && ActorOf(x.Event) == replica.CinderId).Select(x => x.Tick).ToList();
        _out.WriteLine($"first boss death at {firstBossDeath.Tick}; cinder casts after it: {cinderCasts.Count}; final tick {replica.FinalTick}");
        Assert.True(cinderCasts.Count > 0,
            $"the cinder (actor {replica.CinderId}) never cast after the boss {firstBossDeath.Tick} died; run reached tick {replica.FinalTick}");
    }

    [Fact(DisplayName = "Sunken Bellfoundry (key 2, seed 1, autorun seats): the party starts the second boss's pull after the first boss dies (f-fa9da3ca)")]
    public void Party_starts_the_second_boss_pull()
    {
        var replica = Play();
        var secondPull = replica.Events.FirstOrDefault(x => x.Event.Type == "pull_start" && x.Event.Get("pullIndex") is JsInt { Value: 1 });
        _out.WriteLine($"second pull_start at {secondPull.Tick}; run reached tick {replica.FinalTick}");
        Assert.True(secondPull.Event is not null,
            $"the party never started the second boss's pull in {Ticks} ticks (run ended {replica.RunEnd.Result} at {replica.RunEnd.ResultTick})");
    }
}
