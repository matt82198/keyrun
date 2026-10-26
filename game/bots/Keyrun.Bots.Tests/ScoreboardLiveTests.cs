using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Hud;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;
using Xunit.Abstractions;

namespace Keyrun.Bots.Tests;

/// <summary>
/// HUD-SCOREBOARD-LIVE (Matt 2026-10-21: "We need the timer, the kill count, and everything in the POC dungeon too";
/// wrong data shown to a user is P0). Drives the default autorun composition (key 2, seed 1, ScriptedWarden plus two
/// <see cref="BotCommandSource"/> bots, as <see cref="ChoirEngageTests"/> does) to its end and reads the scoreboard
/// (<see cref="ScoreboardReading"/>, the same models the drawn nodes read) every 60 ticks, then checks every value
/// against the sim's own events: the timer is game ticks at 20 Hz, the pull header moves on <c>pull_clear</c>, Enemy
/// Forces and the kill count follow the <c>death</c> events, and the Deaths counter moves on a player death.
/// </summary>
public sealed class ScoreboardLiveTests
{
    private const long Seed = 1;
    private const int Key = 2;
    private const string DungeonId = "hollow-vault";
    private const string HumanClassId = "warden";
    private const int MaxTicks = 7000;

    private static readonly Lazy<ContentRegistry> Registry = new(() => ContentLoader.Load());
    private static readonly Lazy<Drive> Default = new(() => DriveToEnd(null));

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test with xUnit's output sink.</summary>
    /// <param name="output">The sink.</param>
    public ScoreboardLiveTests(ITestOutputHelper output) => _output = output;

    private sealed record Drive(
        IReadOnlyList<ScoreboardReading> Readings,
        IReadOnlyDictionary<string, int> PullClearTick,
        IReadOnlyList<(int Tick, int ActorId, string ClassId, ActorKind Kind)> Deaths,
        IReadOnlyDictionary<string, IReadOnlyList<int>> PullActors,
        string? Result);

    private sealed class Party
    {
        public Party()
        {
            var registry = Registry.Value;
            var setup = HostRunSetup.Build(registry, DungeonId, Key, HumanClassId, withBots: true);
            var start = setup.StartFor(null, null, registry, HumanClassId);
            var options = (start.Options ?? new RunOptions()) with { RotationIndex = setup.RotationIndex };
            var party = setup.PartyFor(setup.Dungeon);
            options = setup.WithBotLoadouts(options, party) ?? options;
            var created = RunRoot.CreateRun(Seed, party, setup.Dungeon, setup.Content, options);
            Run = created with { Encounter = BossRules.Arm(created.Encounter, setup.Boss) };
            Bots = new BotCommandSource(setup.BotActorIds, KitRegistry.From(registry), Seed, BotCommandSource.DefaultSkillDial, null);
            Bots.Reset();
        }

        public Run Run { get; private set; }

        public BotCommandSource Bots { get; }

        public ScriptedWarden Warden { get; } = new();

        public BackfillState Book { get; private set; } = BackfillState.Empty;

        public IReadOnlyList<SimEvent> Step()
        {
            var commands = new List<Command>();
            Warden.Sample(Run.State.Tick, Run, commands, Book.Open.Select(s => s.ActorId).ToList());
            Bots.Sample(Run.State.Tick, Run, commands);
            Bots.TakeReactions();
            var step = Backfill.Step(Book, Run, commands, Array.Empty<QueueRequest>());
            Book = step.Backfill;
            Run = step.Step.Run;
            return step.Step.Events;
        }
    }

    private static (IReadOnlyDictionary<string, int> Table, int Required) ForcesTable() => ScoreboardReading.ForcesOf(Registry.Value);

    /// <summary>
    /// Counted enemies in the dungeon, derived from content by KillCount's rule (an enemy with a forces.json entry is
    /// counted): every member of the dungeon's pulls, the layout's packs and the layout's patrols whose enemy id has a
    /// forces entry. The boss and the Twins carry no forces entry. Derived, not pinned, so new forces-bearing content
    /// (e.g. the corridor-2 patrol) moves the denominator with it.
    /// </summary>
    private static int CountedEnemies(IReadOnlyDictionary<string, int> table)
    {
        var registry = Registry.Value;
        var groups = registry.Dungeons[DungeonId].Pulls.SelectMany(p => p.Enemies)
            .Concat(registry.Layout.Packs.SelectMany(p => p.Enemies))
            .Concat(registry.Layout.Patrols.SelectMany(p => p.Enemies));
        return groups.Where(g => table.ContainsKey(g.EnemyId)).Sum(g => g.Count);
    }

    private static ScoreboardReading Read(Run run)
    {
        var (table, required) = ForcesTable();
        return ScoreboardReading.Of(run, Registry.Value, table, required);
    }

    private static Drive DriveToEnd(Action<Party>? onTick)
    {
        var p = new Party();
        var readings = new List<ScoreboardReading> { Read(p.Run) };
        var clears = new Dictionary<string, int>(StringComparer.Ordinal);
        var deaths = new List<(int, int, string, ActorKind)>();
        var pullActors = p.Run.Dungeon.Pulls.ToDictionary(
            pull => pull.Id,
            pull => (IReadOnlyList<int>)pull.Spawns.Select(s => s.ActorId).ToList(),
            StringComparer.Ordinal);
        while (p.Run.State.Tick < MaxTicks && p.Run.RunEnd.Result is null)
        {
            onTick?.Invoke(p);
            foreach (var e in p.Step())
            {
                if (e.Type == "pull_clear")
                {
                    clears[((JsString)e.Get("pullId")!).Value] = e.Tick;
                }
                else if (e.Type == "death")
                {
                    var id = (int)((JsInt)e.Get("actorId")!).Value;
                    if (p.Run.State.Actors.TryGetValue(id, out var a))
                    {
                        deaths.Add((e.Tick, id, a.ClassId, a.Kind));
                    }
                }
            }
            if (ScoreboardReading.Due(p.Run.State.Tick))
            {
                readings.Add(Read(p.Run));
            }
        }
        return new Drive(readings, clears, deaths, pullActors, p.Run.RunEnd.Result?.ToString());
    }

    private void Print(Drive d)
    {
        _output.WriteLine($"result={d.Result} clears=[{string.Join(",", d.PullClearTick.Select(kv => $"{kv.Key}@{kv.Value}"))}]");
        foreach (var r in d.Readings)
        {
            _output.WriteLine(r.LogLine());
        }
    }

    [Fact(DisplayName = "Scoreboard timer: the run clock is game ticks at 20 Hz (tick 780 reads 39 s, not 13 s)")]
    public void Timer_counts_game_ticks_at_twenty_hertz()
    {
        var d = Default.Value;
        Print(d);
        Assert.Equal(20, Tick.TickHz);
        var at780 = Assert.Single(d.Readings, r => r.Tick == 780);
        Assert.Equal(39, at780.TimerSeconds);
        // No player died on the default run, so the clock is the tick count over 20 on every reading.
        Assert.All(d.Readings, r => Assert.Equal(r.Tick / Tick.TickHz, r.TimerSeconds));
    }

    [Fact(DisplayName = "Scoreboard header: the pull named and its remain count follow pull_clear and death events")]
    public void Header_follows_pull_clear_and_deaths()
    {
        var d = Default.Value;
        // FINISH-NEEDS-FORCES (f-8c50a787, WoW Mythic+): the key completes only at 100% forces with the boss dead. On
        // main's layout the default party walks the line and skips every optional pack, so the boss dies short of the
        // count and the run stays open. The layout lane (feat/dungeon-branches) routes the party into packs and flips
        // this to "Timed".
        Assert.Null(d.Result);
        Assert.True(d.PullClearTick.ContainsKey("boss"), "the boss never died");
        Assert.True(d.Readings[^1].ForcesBp < 10000, $"forces reached {d.Readings[^1].ForcesBp} bp yet the run did not finish");
        Assert.True(d.PullClearTick.ContainsKey("choir"), "the Choir never cleared");
        var order = new[] { "choir", "pair", "seed", "boss" };
        foreach (var r in d.Readings)
        {
            var current = order.FirstOrDefault(id => !d.PullClearTick.TryGetValue(id, out var t) || t > r.Tick) ?? "done";
            Assert.True(current == r.PullId, $"tick {r.Tick}: header names {r.PullId}, the events say {current}");
            if (current == "done")
            {
                continue;
            }
            // A reading at state.Tick T is the state before the step tagged T, so events tagged T are not yet visible.
            var dead = d.Deaths.Count(x => x.Tick < r.Tick && d.PullActors[current].Contains(x.ActorId));
            Assert.True(d.PullActors[current].Count - dead == r.Remain,
                $"tick {r.Tick}: header says {r.Remain} remain of {current}, the death events say {d.PullActors[current].Count - dead}");
        }
        // The capture's frame at 0:39 (tick 780): the Choir is up with nothing dead yet, so "3 of 3 remain" is true.
        var at780 = Assert.Single(d.Readings, r => r.Tick == 780);
        Assert.Equal(("choir", 3), (at780.PullId, at780.Remain));
    }

    [Fact(DisplayName = "Scoreboard forces and kills: both follow the enemy death events; the Choir clear reads 16.00% and 3 kills of the content-counted total")]
    public void Forces_and_kills_follow_death_events()
    {
        var d = Default.Value;
        var (table, required) = ForcesTable();
        Assert.Equal(1000, required);
        var total = CountedEnemies(table);
        foreach (var r in d.Readings)
        {
            // A reading at state.Tick T is the state before the step tagged T, so events tagged T are not yet visible.
            var counted = d.Deaths.Where(x => x.Tick < r.Tick && x.Kind == ActorKind.Enemy && table.ContainsKey(x.ClassId)).ToList();
            var points = counted.Sum(x => table[x.ClassId]);
            Assert.True(points * 10000 / required == r.ForcesBp, $"tick {r.Tick}: forces {r.ForcesBp} bp, the death events bank {points} points");
            Assert.True(counted.Count == r.Kills.Killed, $"tick {r.Tick}: kills {r.Kills.Killed}, the death events say {counted.Count}");
            Assert.Equal(total, r.Kills.Total);
        }
        var afterChoir = d.Readings.First(r => r.Tick >= d.PullClearTick["choir"]);
        Assert.Equal((1600, 3), (afterChoir.ForcesBp, afterChoir.Kills.Killed));
        Assert.Equal("Kills 3/" + total, afterChoir.Kills.Label);
        Assert.Equal(
            "HUD scoreboard tick=" + afterChoir.Tick + " timer=" + afterChoir.TimerSeconds + " forces=16.00 kills=3/" + total + " deaths=0 pull=pair remain=5",
            afterChoir.LogLine());
    }

    [Fact(DisplayName = "Scoreboard deaths: a player death event moves the Deaths counter to 1 and adds 15 s to the timer")]
    public void Deaths_counter_increments_on_a_player_death_event()
    {
        var p = new Party();
        Assert.Equal(0, Read(p.Run).Deaths);
        int? deathTick = null;
        while (p.Run.State.Tick < MaxTicks && deathTick is null)
        {
            if (p.Run.Encounter.PullIndex == 0 && p.Run.State.Actors.Values.Any(a => a.Kind == ActorKind.Enemy && a.Alive && a.Health < a.MaxHealth))
            {
                // The Choir is engaged: leave the Warden one hit from death so an enemy hit lands the death event.
                p.Run.State.Actors[1].Health = Math.Min(p.Run.State.Actors[1].Health, 1);
            }
            var before = Read(p.Run);
            var events = p.Step();
            if (events.Any(e => e.Type == "death" && (int)((JsInt)e.Get("actorId")!).Value == 1))
            {
                deathTick = p.Run.State.Tick;
                Assert.Equal(0, before.Deaths);
            }
        }
        Assert.True(deathTick is not null, "the Warden never died");
        var after = Read(p.Run);
        _output.WriteLine(after.LogLine());
        Assert.Equal(1, after.Deaths);
        Assert.Equal((after.Tick + Keyrun.Sim.Encounter.Timer.DeathSlackCostTicks) / Tick.TickHz, after.TimerSeconds);
        Assert.Equal("Deaths 1 (+15s)", SlackReading.Of(p.Run).Label);
    }
}
