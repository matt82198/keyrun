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
/// Review R7 (slice S9): <see cref="HostRunFactory.Create"/> is the one run-assembly path. Each case builds the same run
/// twice, once through the factory and once through the sequence its caller hand-wrote before the factory existed
/// (SimDriver.StartRun on main bc6630d3, PlaytestRunner.Play on the same commit), then steps both for
/// <see cref="Ticks"/> ticks the way SimDriver.StepOnce does (seat 1 the autorun <see cref="ScriptedWarden"/>, the bot
/// seats <see cref="BotCommandSource"/>, each step through <see cref="Backfill.Step"/> with an empty queue) and compares
/// a run state digest: the boss config at creation plus every step's projection line and canonical events, chained
/// with <see cref="Projection.TraceHash"/>.
/// </summary>
public sealed class HostRunFactoryTests
{
    private const long Seed = 1;            // ArenaOptions.DefaultSeed
    private const int Key = 2;              // ArenaOptions.DefaultKeyLevel
    private const int Ticks = 400;          // ArenaOptions.DefaultTicks
    private const string DungeonId = "hollow-vault";

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test with xUnit's output sink (the digests are printed for the PR).</summary>
    /// <param name="output">The sink.</param>
    public HostRunFactoryTests(ITestOutputHelper output) => _output = output;

    // SimDriver.StartRun's assembly as it stood before the factory (bc6630d3 SimDriver.cs:1119-1147), request and
    // progression null as on a CLI / --autorun start.
    private static Run HandSequencedHost(HostRunSetup setup, ContentRegistry registry, string classId, int startPull)
    {
        var dungeon = setup.Dungeon;
        if (startPull > 1)
        {
            // Mirrors HostRunSetup.FromPull: a slice that drops earlier pulls carries no forces goal (FINISH-NEEDS-FORCES).
            dungeon = dungeon with { Pulls = dungeon.Pulls.Skip(startPull - 1).ToList(), ForcesGoal = null };
        }
        var start = setup.StartFor(null, null, registry, classId);
        var options = (start.Options ?? new RunOptions()) with { RotationIndex = setup.RotationIndex };
        var party = setup.PartyFor(dungeon);
        options = setup.WithBotLoadouts(options, party) ?? options;
        var created = RunRoot.CreateRun(Seed, party, dungeon, setup.Content, options);
        if (startPull > 1)
        {
            created = RunRoot.EngageStartPull(created);
        }
        return created with { Encounter = BossRules.Arm(created.Encounter, setup.Boss) };
    }

    // PlaytestRunner.Play's assembly as it stood before the factory (bc6630d3 PlaytestRunner.cs:112-113).
    private static Run HandSequencedPlaytest(HostRunSetup setup)
    {
        var created = RunRoot.CreateRun(Seed, setup.Party, setup.Dungeon, setup.Content, null);
        return created with { Encounter = BossRules.Arm(created.Encounter, setup.Boss) };
    }

    private static uint Digest(Run run, HostRunSetup setup, ContentRegistry registry, List<string> lines)
    {
        var boss = run.Encounter.Boss?.Config ?? throw new InvalidOperationException("boss not armed");
        lines.Add(FormattableString.Invariant(
            $"BOSS twins={boss.Twins.Count} lockAt={boss.LockThresholdBp} window={boss.LockWindowTicks} bar={boss.TwinsBar.Id} boss={boss.BossActorId}"));
        lines.Add(Projection.Line(run.State, run.Encounter.PullIndex, 0));
        var warden = new ScriptedWarden();
        var bots = new BotCommandSource(setup.BotActorIds, KitRegistry.From(registry), Seed, BotCommandSource.DefaultSkillDial, null);
        bots.Reset();
        var backfill = BackfillState.Empty;
        for (var i = 0; i < Ticks; i++)
        {
            var tick = run.State.Tick;
            var commands = new List<Command>();
            warden.Sample(tick, run, commands, backfill.Open.Select(s => s.ActorId).ToList());
            bots.Sample(tick, run, commands);
            bots.TakeReactions();
            var step = Backfill.Step(backfill, run, commands, Array.Empty<QueueRequest>());
            backfill = step.Backfill;
            run = step.Step.Run;
            lines.Add(Projection.Line(run.State, run.Encounter.PullIndex, 0) + " | " + string.Join(",", step.Step.Events.Select(e => e.ToString())));
        }
        return Projection.TraceHash(lines);
    }

    private (uint Hash, List<string> Lines) Measure(string label, Run run, HostRunSetup setup, ContentRegistry registry)
    {
        var lines = new List<string>();
        var hash = Digest(run, setup, registry, lines);
        _output.WriteLine(FormattableString.Invariant($"{label}: digest={hash:x8} lines={lines.Count} end_tick={run.State.Tick + Ticks}"));
        return (hash, lines);
    }

    private static void SameRun(List<string> expected, List<string> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.True(expected[i] == actual[i], FormattableString.Invariant($"first divergence at digest line {i}:\n hand:    {expected[i]}\n factory: {actual[i]}"));
        }
    }

    /// <summary>The default host run (<c>--autorun</c>: key 2, seed 1, the three-seat party, pull 1, no request).</summary>
    [Fact(DisplayName = "HostRunFactory.Create equals SimDriver.StartRun's hand-sequenced assembly for the default run (same run state digest)")]
    public void Factory_matches_the_hand_sequenced_default_host_run()
    {
        var registry = ContentLoader.Load();
        var classId = Lobby.DefaultClassId;
        var setup = HostRunSetup.Build(registry, DungeonId, Key, classId, withBots: true, rotationIndex: 0);
        var hand = Measure("hand-sequenced default", HandSequencedHost(setup, registry, classId, 1), setup, registry);
        var made = HostRunFactory.Create(setup, new RunRequest(Seed, registry, classId));
        var factory = Measure("factory default", made.Run, setup, registry);
        SameRun(hand.Lines, factory.Lines);
        Assert.Equal(hand.Hash, factory.Hash);
        Assert.Equal(setup.PartyFor(setup.Dungeon).Select(p => p.Id), made.Party.Select(p => p.Id));
        Assert.Same(setup.Dungeon, made.Dungeon);
    }

    /// <summary>A <c>--pull 2</c> host run: the dungeon slice and <see cref="RunRoot.EngageStartPull"/> step.</summary>
    [Fact(DisplayName = "HostRunFactory.Create equals SimDriver.StartRun's hand-sequenced assembly for a --pull 2 run (same run state digest)")]
    public void Factory_matches_the_hand_sequenced_pull_two_host_run()
    {
        var registry = ContentLoader.Load();
        var classId = Lobby.DefaultClassId;
        var setup = HostRunSetup.Build(registry, DungeonId, Key, classId, withBots: true, rotationIndex: 0);
        var hand = Measure("hand-sequenced pull 2", HandSequencedHost(setup, registry, classId, 2), setup, registry);
        var made = HostRunFactory.Create(setup, new RunRequest(Seed, registry, classId) { StartPull = 2 });
        var factory = Measure("factory pull 2", made.Run, setup, registry);
        SameRun(hand.Lines, factory.Lines);
        Assert.Equal(hand.Hash, factory.Hash);
        Assert.Equal(setup.Dungeon.Pulls.Count - 1, made.Dungeon.Pulls.Count);
    }

    /// <summary>PlaytestRunner's shape: no bot loadouts and no options record, as Play built its runs before.</summary>
    [Fact(DisplayName = "HostRunFactory.Create without bot loadouts equals PlaytestRunner's hand-sequenced assembly (same run state digest)")]
    public void Factory_matches_the_hand_sequenced_playtest_run()
    {
        var registry = ContentLoader.Load();
        var classId = HostRunSetup.Roster[0].ClassId;
        var setup = HostRunSetup.Build(registry, DungeonId, Key, classId, withBots: true);
        var hand = Measure("hand-sequenced playtest", HandSequencedPlaytest(setup), setup, registry);
        var made = HostRunFactory.Create(setup, new RunRequest(Seed, registry, classId) { BotLoadouts = false, RotationOptions = false });
        var factory = Measure("factory playtest", made.Run, setup, registry);
        SameRun(hand.Lines, factory.Lines);
        Assert.Equal(hand.Hash, factory.Hash);
    }

    /// <summary>The digest is not blind to assembly: the host run (bot loadouts on) and the playtest shape (off) differ,
    /// so the equalities above would catch a factory that dropped or added that step.</summary>
    [Fact(DisplayName = "The run state digest tells the bot-loadout host run from the playtest shape")]
    public void Digest_sees_the_bot_loadout_step()
    {
        var registry = ContentLoader.Load();
        var classId = Lobby.DefaultClassId;
        var setup = HostRunSetup.Build(registry, DungeonId, Key, classId, withBots: true, rotationIndex: 0);
        var host = Measure("factory host", HostRunFactory.Create(setup, new RunRequest(Seed, registry, classId)).Run, setup, registry);
        var bare = Measure("factory bare", HostRunFactory.Create(setup, new RunRequest(Seed, registry, classId) { BotLoadouts = false, RotationOptions = false }).Run, setup, registry);
        Assert.NotEqual(host.Hash, bare.Hash);
    }

    /// <summary>
    /// <c>--pull N</c> is out of range below 1 or above the dungeon's pulls; the message names the valid range (route
    /// order, segment boss pulls included).
    /// </summary>
    [Fact(DisplayName = "HostRunFactory.Create refuses a --pull outside 1..pulls and names the valid range")]
    public void Factory_refuses_a_pull_out_of_range()
    {
        var registry = ContentLoader.Load();
        var classId = Lobby.DefaultClassId;
        var setup = HostRunSetup.Build(registry, DungeonId, Key, classId, withBots: true, rotationIndex: 0);
        var count = setup.Dungeon.Pulls.Count;
        foreach (var bad in new[] { count + 1, 0 })
        {
            var e = Assert.Throws<ArgumentException>(() => HostRunFactory.Create(setup, new RunRequest(Seed, registry, classId) { StartPull = bad }));
            Assert.Equal(
                FormattableString.Invariant($"--pull {bad} is out of range: the dungeon's pulls are numbered 1..{count} in route order (segment boss pulls included)"),
                e.Message);
        }
    }

    [Fact(DisplayName = "HostRunFactory.Create starts a multi-boss run at --pull N in route order, boss pulls included")]
    public void Factory_starts_a_multi_boss_run_at_a_route_order_pull()
    {
        var registry = ContentLoader.Load();
        var classId = Lobby.DefaultClassId;
        var setup = HostRunSetup.Build(registry, "sunken-bellfoundry", Key, classId, withBots: true, rotationIndex: 0, dungeonSeed: Seed);
        Assert.Equal(new[] { "boss-s1", "boss-s2", "boss" }, setup.Dungeon.Pulls.Select(p => p.Id));
        var run = HostRunFactory.Create(setup, new RunRequest(Seed, registry, classId) { StartPull = 2 }).Run;
        Assert.Equal(new[] { "boss-s2", "boss" }, run.Dungeon.Pulls.Select(p => p.Id));
    }
}
