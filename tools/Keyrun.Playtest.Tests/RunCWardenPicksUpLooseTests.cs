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
/// LOOSE-PICKUP (PR #326 merge-forward, run C at key 2 under TEMPO-CAST-FLOOR): run C's careless Cinder (seat 3,
/// <c>--careless-seat 3@300:pair</c>) walks through the Choir to the room's far doorway from tick 300
/// (<see cref="Careless.HoldPoint"/>). A Choir melee nobody has threat on targets the nearest living player
/// (<c>ScriptTarget</c>), so it leaves the autorun Warden for the Cinder. Before this fix <see cref="ScriptedWarden"/> had
/// no taunt rule at all: the Choir melee 101 hit the 250-health Cinder every 39 ticks from tick 349 and killed it at tick
/// 583, the Warden never cast Taunt in the whole run, and with two kickers left the Cantor's 196-tick heal outpaced two
/// 480-tick kicks, so the Choir never died (run ended unfinished at tick 7000). A tank picks up a loose enemy (BIBLE
/// section 3, the bot tank's own rule <see cref="Keyrun.Bots.Roles.TankEngage.PickLooseEnemy"/>): the Warden taunts it.
/// Built headless exactly as <see cref="RunCCarelessPairTests"/> builds run C (key 2, seed 1).
/// </summary>
public sealed class RunCWardenPicksUpLooseTests
{
    private const int Key = 2;
    private const long Seed = 1;
    private const int MaxTicks = 7000; // run C's --ticks (HudSceneTest $PartyTicks)
    private const int CarelessSeat = 3;
    private const int CarelessFromTick = 300; // HudSceneTest $CarelessSeat '3@300:pair'
    private const int WardenId = 1;
    private const string Taunt = "warden.taunt";

    private sealed record Drive(int? LooseTick, int? LooseId, int? TauntTick, int? TauntTarget, int? ChoirClear, List<string> SeatDeaths, int EndTick);

    private static readonly Lazy<Drive> Driven = new(DriveRunC);

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test with xUnit's output sink.</summary>
    /// <param name="output">The sink.</param>
    public RunCWardenPicksUpLooseTests(ITestOutputHelper output) => _output = output;

    private static Drive DriveRunC()
    {
        var registry = ContentLoader.Load();
        var setup = HostRunSetup.Build(registry, "hollow-vault", Key, "warden", withBots: true);
        var start = setup.StartFor(null, null, registry, "warden");
        var created = RunRoot.CreateRun(Seed, setup.PartyFor(setup.Dungeon), setup.Dungeon, setup.Content, start.Options);
        var run = created with { Encounter = BossRules.Arm(created.Encounter, setup.Boss) };
        Assert.Equal("choir", setup.Dungeon.Pulls[0].Id);
        var choir = setup.Dungeon.Pulls[0].Spawns.Select(s => s.ActorId).ToHashSet();
        var pairIds = setup.Dungeon.Pulls.Single(p => p.Id == "pair").Spawns.Select(s => s.ActorId).ToList();
        var careless = new Dictionary<int, CarelessPlan> { [CarelessSeat] = new CarelessPlan(CarelessFromTick, pairIds) };
        var warden = new ScriptedWarden();
        var bots = new BotCommandSource(setup.BotActorIds, KitRegistry.From(registry), Seed, BotCommandSource.DefaultSkillDial, careless);
        var seats = setup.Party.Select(p => p.Id).ToHashSet();

        int? looseTick = null;
        int? looseId = null;
        int? tauntTick = null;
        int? tauntTarget = null;
        int? choirClear = null;
        var deaths = new List<string>();
        while (run.State.Tick < MaxTicks && run.RunEnd.ResultTick is null && choirClear is null)
        {
            var tick = run.State.Tick;
            if (looseTick is null && run.State.Actors.Values.FirstOrDefault(a => choir.Contains(a.Id) && a.Alive && !a.Idle &&
                    a.TargetId is { } t && t != WardenId && seats.Contains(t)) is { } loose)
            {
                looseTick = tick;
                looseId = loose.Id;
            }
            var commands = new List<Command>();
            warden.Sample(tick, run, commands);
            bots.Sample(tick, run, commands);
            var stepped = RunRoot.StepRun(run, commands);
            run = stepped.Run;
            foreach (var e in stepped.Events)
            {
                if (e.Type == "cast_complete" && ((JsInt)e.Get("actorId")!).Value == WardenId && ((JsString)e.Get("abilityId")!).Value == Taunt && tauntTick is null)
                {
                    tauntTick = e.Tick;
                    tauntTarget = run.State.Actors.Values.FirstOrDefault(a => choir.Contains(a.Id) && a.Threat.ContainsKey(WardenId) && a.Id == looseId)?.Id;
                }
                else if (e.Type == "death" && seats.Contains((int)((JsInt)e.Get("actorId")!).Value))
                {
                    deaths.Add($"seat {((JsInt)e.Get("actorId")!).Value} at {e.Tick} to {e.Get("sourceId")}");
                }
                else if (e.Type == "pull_clear" && ((JsString)e.Get("pullId")!).Value == "choir")
                {
                    choirClear = e.Tick;
                }
            }
        }
        return new Drive(looseTick, looseId, tauntTick, tauntTarget, choirClear, deaths, run.State.Tick);
    }

    [Fact(DisplayName = "Run C (LOOSE-PICKUP): a Choir enemy that leaves the Warden for another seat is taunted back, and no seat dies to the Choir")]
    public void Warden_taunts_a_loose_choir_enemy_and_the_party_clears_the_choir()
    {
        var d = Driven.Value;
        _output.WriteLine($"loose={d.LooseId?.ToString() ?? "none"}@{d.LooseTick?.ToString() ?? "-"} taunt={d.TauntTick?.ToString() ?? "none"} on={d.TauntTarget?.ToString() ?? "-"} " +
            $"choirClear={d.ChoirClear?.ToString() ?? "none"} deaths=[{string.Join("; ", d.SeatDeaths)}] endTick={d.EndTick}");
        // Non-vacuity: the careless seat must actually draw a Choir enemy off the Warden.
        Assert.True(d.LooseTick is not null, "no Choir enemy ever targeted a seat other than the Warden: the pickup check would be vacuous");
        Assert.True(d.TauntTick is not null, $"Choir enemy {d.LooseId} turned on another seat at tick {d.LooseTick} and the Warden never cast Taunt");
        Assert.Equal(d.LooseId, d.TauntTarget);
        Assert.Empty(d.SeatDeaths);
        Assert.True(d.ChoirClear is not null, $"the Choir never cleared (ended at tick {d.EndTick})");
    }
}
