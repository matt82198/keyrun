using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Keyrun.Bots;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Playtest.Tests;

/// <summary>
/// Godot-free replicas of game/src/Host/HudSceneTest.ps1 runs B and K (finding f-998e3ebc, integration PR #432), built
/// the way SimDriver.StartRun builds a run: HostRunSetup.Build, a <c>--pull N</c> slice of the dungeon,
/// HostRunSetup.StartFor and WithBotLoadouts, RunRoot.CreateRun, RunRoot.EngageStartPull, BossRules.Arm, then
/// Backfill.Step every tick. Measured against CI: run K ends at the game-e2e run_end tick on both origin/main 1fa426c2
/// (1273) and the pile 55ef2051 (691); run B's Pair mark telegraph sits at the CI ping's floor point (39325, 421).
/// <para>
/// Why the pile moved both (bisected over the pile's first-parent merges, then by toggling each change on 55ef2051):
/// #362 (merge 7cbf7135). Its Warden passive armor (a6569bb8, 3500 bp since 736cf6e7; BIBLE s3.1) keeps run B's solo
/// Warden alive from tick 207 to 354, past the Pair's first mark telegraph (295); with armor 0 he dies at 207 again and no
/// telegraph is drawn. Its D-HEALER-DOWNTIME rule (0037cfd3; STATE.md D-HEALER-DOWNTIME, WoW M+: a healer with nobody to
/// heal keeps contributing) has the Chanter press Dissonance on the boss, so run K's boss pull clears at 691 instead of
/// 1261 with the rule switched off (armor makes no difference to run K).
/// </para>
/// </summary>
public sealed class HudSceneRunsBKTests
{
    private const long Seed = 1;            // HudSceneTest's default seed (ArenaOptions.DefaultSeed)
    private const int Key = 2;              // ArenaOptions.DefaultKeyLevel; run K's queued key
    private const int WardenId = 1;
    private const string ExamplePath = "game/fixtures/input/example-enter-ping.input-log.json";

    private static Run Create(HostRunSetup setup, Keyrun.Content.ContentRegistry registry, int startPull, QueueRequest? request, Progression? progression)
    {
        var dungeon = setup.Dungeon with { Pulls = setup.Dungeon.Pulls.Skip(startPull - 1).ToList() };
        var start = setup.StartFor(request, progression, registry, "warden");
        var options = (start.Options ?? new RunOptions()) with { RotationIndex = setup.RotationIndex };
        var party = setup.PartyFor(dungeon);
        options = setup.WithBotLoadouts(options, party) ?? options;
        var created = RunRoot.EngageStartPull(RunRoot.CreateRun(Seed, party, dungeon, setup.Content, options));
        return created with { Encounter = BossRules.Arm(created.Encounter, setup.Boss) };
    }

    private static string Str(SimEvent e, string field) => e.Get(field) is JsString s ? s.Value : string.Empty;

    private static int Int(SimEvent e, string field) => e.Get(field) is JsInt i ? (int)i.Value : -1;

    [Fact(DisplayName = "Run B (pull 2, solo Warden): a mark telegraph is drawn only while the Warden lives, is cancelled the tick he dies, and no mark cast starts on a dead target")]
    public void Run_b_marks_never_outlive_the_solo_warden()
    {
        // HudSceneTest run B: -- --autorun --ticks 900 --pull 2 --solo (the autorun Warden is ScriptedWarden, no bots).
        var registry = ContentLoader.Load();
        var setup = HostRunSetup.Build(registry, "hollow-vault", Key, "warden", withBots: false);
        var run = Create(setup, registry, 2, null, null);
        var warden = new ScriptedWarden();
        var backfill = BackfillState.Empty;
        var events = new List<SimEvent>();
        var castsAfterDeath = new List<string>();
        int? death = null;
        while (run.State.Tick < 900)
        {
            var tick = run.State.Tick;
            var commands = new List<Command>();
            warden.Sample(tick, run, commands, null);
            var step = Backfill.Step(backfill, run, commands, Array.Empty<QueueRequest>());
            backfill = step.Backfill;
            run = step.Step.Run;
            events.AddRange(step.Step.Events);
            death ??= step.Step.Events.FirstOrDefault(e => e.Type == "death" && Int(e, "actorId") == WardenId)?.Tick;
            if (death is not null)
            {
                castsAfterDeath.AddRange(run.State.Actors.Values
                    .Where(a => a.Kind == ActorKind.Enemy && a.CastId is not null)
                    .Select(a => FormattableString.Invariant($"{a.Id}:{a.CastId}@{run.State.Tick}")));
            }
        }

        var telegraphs = events.Where(e => e.Type == "telegraph").ToList();
        Assert.True(death is not null, "the solo Warden never died in run B");
        Assert.True(telegraphs.Count > 0,
            $"no telegraph in run B (Warden dead at {death}): the scenario no longer reaches the Pair's mark, the test is vacuous");
        var d = death!.Value;
        Assert.All(telegraphs, t => Assert.True(t.Tick < d, $"telegraph {Str(t, "cast")} by {Int(t, "actorId")} drawn at {t.Tick}, the Warden died at {d}"));
        var cancels = events.Where(e => e.Type == "telegraph_cancel").ToList();
        Assert.Equal(
            telegraphs.Select(t => (Int(t, "actorId"), Str(t, "cast"), d)).OrderBy(x => x).ToList(),
            cancels.Select(c => (Int(c, "actorId"), Str(c, "cast"), c.Tick)).OrderBy(x => x).ToList());
        Assert.DoesNotContain(events, e => e.Type == "telegraph_hit");
        Assert.True(castsAfterDeath.Count == 0, $"an enemy holds a cast after its only target died at {d}: {string.Join(", ", castsAfterDeath.Take(5))}");
    }

    [Fact(DisplayName = "Run K (pull 4, key 2 echo, idle human + bots): the committed example's post-run Enters are 3 and 6 ticks after the run's run_end")]
    public void Run_k_example_enters_follow_the_run_end()
    {
        // HudSceneTest run K1: -- --record-input <log> --press-enter 3 --press-ping 2 --pull 4. Enter at tick 0 queues
        // the screen's run (key 2, Clause echo, Progression.Start(seed)); the human then does nothing but G at tick 2
        // (a spot ping at his position, routed to the bots); the bots clear the boss over him. The scripted Enter waits
        // SimDriver.EnterWaitFrames (10) frames at --fixed-fps 60 on each prompt: 3 host ticks after run_end, 3 more after
        // the screen reopens (main 1fa426c2: run_end 1273, Enters 1276/1279; game-e2e on #432: run_end 691, Enters 694/697).
        var registry = ContentLoader.Load();
        var setup = HostRunSetup.Build(registry, "hollow-vault", Key, "warden", withBots: true);
        var run = Create(setup, registry, 4, new QueueRequest("hollow-vault", Key, "echo"), Progression.Start(Seed));
        Assert.Equal("echo", run.Encounter.Rules.ActiveClauseId);
        var bots = new BotCommandSource(setup.BotActorIds, KitRegistry.From(registry), Seed, BotCommandSource.DefaultSkillDial, null);
        var backfill = BackfillState.Empty;
        while (run.State.Tick < 7000 && run.RunEnd.ResultTick is null)
        {
            var tick = run.State.Tick;
            var commands = new List<Command>();
            if (tick == 2 && run.State.Actors.TryGetValue(WardenId, out var me))
            {
                Assert.True(bots.Ping(new PingEvent(tick, WardenId, PingKind.Spot) { Point = new PointMm(me.Pos.X, me.Pos.Z) }));
            }
            bots.Sample(tick, run, commands);
            var step = Backfill.Step(backfill, run, commands, Array.Empty<QueueRequest>());
            backfill = step.Backfill;
            run = step.Step.Run;
        }
        Assert.True(run.RunEnd.ResultTick is not null, "run K never ended (the bots did not clear the boss)");
        var end = run.RunEnd.ResultTick!.Value;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, ExamplePath)))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir!.FullName, ExamplePath)));
        var recorded = doc.RootElement.GetProperty("events").EnumerateArray()
            .Select(e => (e.GetProperty("tick").GetInt32(), e.GetProperty("key").GetString(), e.GetProperty("pressed").GetBoolean()))
            .ToList();
        var expected = new List<(int, string?, bool)> { (0, "Enter", true), (2, "G", true), (end + 3, "Enter", true), (end + 6, "Enter", true) };
        Assert.Equal(expected, recorded);
    }
}
