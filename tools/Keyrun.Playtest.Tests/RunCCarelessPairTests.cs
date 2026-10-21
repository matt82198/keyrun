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
using DungeonDef = Keyrun.Sim.Encounter.DungeonDef;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// PR #308 run C (HudSceneTest, game-e2e run 39509151880): run C launches with <c>--careless-seat 3@300:pair</c>
/// (CARELESS-SEAT, STATE.md D43). Built headless as SimDriver builds it (<see cref="HostRunSetup.Build"/> at key 2, seed
/// 1, bots on, <see cref="BossRules.Arm"/>), seat 1 the autorun <see cref="ScriptedWarden"/> and seats 2 and 3 the host's
/// own <see cref="BotCommandSource"/> with seat 3 careless from tick 300 toward the Pair, stepped in SimDriver.StepOnce's
/// order. Before PATH-TO-BOSS (D44) the Choir room's shut gate held the careless seat until the Choir cleared; with no
/// gate it walked 22 m down the open path mid-Choir, pulled the Pair onto the Choir fight and wiped the party at tick
/// 1630. A careless player runs ahead between pulls (<see cref="Careless.StrayTarget"/>), so the Pair engages only after
/// the Choir is cleared, on the careless seat, a Pair enemy hits a bot seat, and the party still clears the dungeon.
/// </summary>
public sealed class RunCCarelessPairTests
{
    private const int Key = 2;
    private const long Seed = 1;
    private const int MaxTicks = 7000; // run C's --ticks (HudSceneTest $PartyTicks)
    private const int CarelessSeat = 3;
    private const int CarelessFromTick = 300; // HudSceneTest $CarelessSeat '3@300:pair'

    private sealed record Drive(
        int? ChoirClear,
        int? PairEngage,
        Vec3? CarelessAtPairEngage,
        bool PairHitABot,
        RunResult? Result,
        int EndTick,
        DungeonDef Dungeon);

    private static readonly Lazy<Drive> Driven = new(DriveRunC);

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test with xUnit's output sink.</summary>
    /// <param name="output">The sink.</param>
    public RunCCarelessPairTests(ITestOutputHelper output) => _output = output;

    private static long Int(SimEvent e, string key) => ((JsInt)e.Get(key)!).Value;

    private static Drive DriveRunC()
    {
        var registry = ContentLoader.Load();
        var setup = HostRunSetup.Build(registry, "hollow-vault", Key, "warden", withBots: true);
        var start = setup.StartFor(null, null, registry, "warden");
        // As SimDriver.StartRun: the rotation index and the bot seats' default gear (HostRunSetup.WithBotLoadouts). Without
        // the gear this replica cleared at 4623 while the host's run C never ended (game-e2e 39552418449): the gear's
        // chanter.weapon.wideningCadence makes Cadence a circle, which the healer's Setup must not aim at an enemy.
        var options = (start.Options ?? new RunOptions()) with { RotationIndex = setup.RotationIndex };
        var party = setup.PartyFor(setup.Dungeon);
        options = setup.WithBotLoadouts(options, party) ?? options;
        var created = RunRoot.CreateRun(Seed, party, setup.Dungeon, setup.Content, options);
        var run = created with { Encounter = BossRules.Arm(created.Encounter, setup.Boss) };
        var pair = setup.Dungeon.Pulls.Single(p => p.Id == "pair");
        var pairIds = pair.Spawns.Select(s => s.ActorId).ToList();
        // As SimDriver's CarelessSeatPlan.For builds it for "3@300:pair".
        var careless = new Dictionary<int, CarelessPlan> { [CarelessSeat] = new CarelessPlan(CarelessFromTick, pairIds) };
        var warden = new ScriptedWarden();
        var bots = new BotCommandSource(setup.BotActorIds, KitRegistry.From(registry), Seed, BotCommandSource.DefaultSkillDial, careless);
        var botSeats = setup.BotActorIds.Select(id => (long)id).ToHashSet();
        var pairSet = pairIds.Select(id => (long)id).ToHashSet();

        int? choirClear = null;
        int? pairEngage = null;
        Vec3? carelessAt = null;
        var pairHitBot = false;
        while (run.State.Tick < MaxTicks && run.RunEnd.ResultTick is null)
        {
            var tick = run.State.Tick;
            var commands = new List<Command>();
            warden.Sample(tick, run, commands);
            bots.Sample(tick, run, commands);
            var stepped = RunRoot.StepRun(run, commands);
            run = stepped.Run;
            foreach (var e in stepped.Events)
            {
                if (e.Type == "pull_clear" && ((JsString)e.Get("pullId")!).Value == "choir")
                {
                    choirClear ??= e.Tick;
                }
                else if (e.Type == "pull_start" && ((JsString)e.Get("pullId")!).Value == "pair" && pairEngage is null)
                {
                    pairEngage = e.Tick;
                    carelessAt = run.State.Actors[CarelessSeat].Pos;
                }
                else if (e.Type == "damage" && pairSet.Contains(Int(e, "sourceId")) && botSeats.Contains(Int(e, "targetId")) && Int(e, "healthDamage") > 0)
                {
                    pairHitBot = true;
                }
            }
        }
        return new Drive(choirClear, pairEngage, carelessAt, pairHitBot, run.RunEnd.Result, run.State.Tick, setup.Dungeon);
    }

    [Fact(DisplayName = "Run C careless seat (3@300:pair): the Pair engages only after the Choir clears, on the careless seat, hits a bot seat, and the party clears the run")]
    public void Careless_seat_pulls_the_pair_after_the_choir_and_the_party_clears()
    {
        var d = Driven.Value;
        var pair = d.Dungeon.Pulls.Single(p => p.Id == "pair");
        var center = Pulls.RingCenter(pair);
        _output.WriteLine($"choir pull_clear={d.ChoirClear?.ToString() ?? "none"} pair pull_start={d.PairEngage?.ToString() ?? "none"} " +
            $"careless seat at pair engage={(d.CarelessAtPairEngage is { } p ? $"({p.X},{p.Z})" : "none")} pair ring=({center.X},{center.Z}) r={pair.AggroMm} " +
            $"pairHitBot={d.PairHitABot} result={d.Result?.ToString() ?? "none"} endTick={d.EndTick}");

        Assert.True(d.ChoirClear is not null, $"the Choir never cleared (run ended {d.Result?.ToString() ?? "unfinished"} at tick {d.EndTick})");
        Assert.True(d.PairEngage is not null, "the Pair never engaged");
        Assert.True(d.PairEngage > d.ChoirClear, $"the Pair engaged at tick {d.PairEngage}, while the Choir was still up (cleared at tick {d.ChoirClear}): a double pull");
        Assert.True(Aggro.InRing(d.CarelessAtPairEngage!.Value, center, pair.AggroMm), "the careless seat was not in the Pair's ring when it engaged");
        Assert.True(d.PairHitABot, "no Pair enemy damaged a bot seat (HudSceneTest check 12's bot-damage line)");
        Assert.Equal(RunResult.Timed, d.Result);
    }
}
