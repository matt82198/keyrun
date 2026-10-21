using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Keyrun.Bots;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Hud;
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

    /// <summary>
    /// R-PILE-10 (HudSceneTest check 8; dungeon loop decided as WoW Mythic+ does, BIBLE s3 cast bars and telegraphs): a
    /// mechanic aimed at a living player is legitimate and drops when the player dies. Pinned on run B's exact setup,
    /// the same four facts check 8 reads from the host log: (a) the Warden's death tick (his <c>death</c> event); (b) every
    /// <c>HUD ping label=SPREAD|STACK</c> call (Pings.OnStepped: a new <see cref="PingCall.Key"/> after a step, logged at
    /// that step's tick) comes strictly before it, and at least one does (a run with no call is vacuous and fails);
    /// (c) on the death tick every live telegraph cancels (<c>telegraph_cancel</c>, TELEGRAPH-CANCEL-ON-DEATH); (d) after
    /// it no cast starts on actor 1 (R-DEADCAST-1) and no further call appears while he is dead. R-BACKFILL-2 (f-47a7a6db):
    /// the solo wipe no longer ends run B after the wipe grace; the dead Warden releases and respawns at the entrance
    /// <see cref="RunWiring.ReleaseDelayTicks"/> after his death, so "after the death" is his time dead, up to and
    /// including the respawn step, and the walk stops there.
    /// </summary>
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
        var castStartsOnWarden = new List<string>();
        var seenCalls = new HashSet<string>(StringComparer.Ordinal);
        var pings = new List<(int Tick, string Line)>();
        List<int>? registryBeforeDeath = null;
        List<int>? registryAfterDeath = null;
        int? death = null;
        while (run.State.Tick < 900 && !(death is not null && run.State.Actors[WardenId].Alive))
        {
            var tick = run.State.Tick;
            var before = run.State;
            var registryBefore = run.Encounter.Telegraphs.Select(t => t.Id).ToList();
            var commands = new List<Command>();
            warden.Sample(tick, run, commands, null);
            var step = Backfill.Step(backfill, run, commands, Array.Empty<QueueRequest>());
            backfill = step.Backfill;
            run = step.Step.Run;
            events.AddRange(step.Step.Events);
            if (death is null && step.Step.Events.Any(e => e.Type == "death" && Int(e, "actorId") == WardenId))
            {
                registryBeforeDeath = registryBefore;
                registryAfterDeath = run.Encounter.Telegraphs.Select(t => t.Id).ToList();
            }
            // Pings.OnStepped: each call seen for the first time after this step prints one "HUD ping" line, after
            // the step's TICK and EVENT lines, so the line belongs to this step's tick.
            foreach (var call in PingCalls.Calls(run).Where(c => seenCalls.Add(c.Key)))
            {
                pings.Add((tick, call.LogLine()));
            }
            death ??= step.Step.Events.FirstOrDefault(e => e.Type == "death" && Int(e, "actorId") == WardenId)?.Tick;
            if (death is not null)
            {
                castsAfterDeath.AddRange(run.State.Actors.Values
                    .Where(a => a.Kind == ActorKind.Enemy && a.CastId is not null)
                    .Select(a => FormattableString.Invariant($"{a.Id}:{a.CastId}@{run.State.Tick}")));
                // CombatNarrator's "CAST start ... target=" rule: a new CastId or CastStartTick is a cast start.
                castStartsOnWarden.AddRange(run.State.Actors.Values
                    .Where(a => a.CastId is not null &&
                        (!before.Actors.TryGetValue(a.Id, out var p) || p.CastId != a.CastId || p.CastStartTick != a.CastStartTick) &&
                        (a.CastTargetPinned ? a.CastTargetId : a.TargetId) == WardenId)
                    .Select(a => FormattableString.Invariant($"{a.Id}:{a.CastId}@{tick}")));
            }
        }

        var telegraphs = events.Where(e => e.Type == "telegraph").ToList();
        // (a) the death tick.
        Assert.True(death is not null, "the solo Warden never died in run B");
        var d = death!.Value;
        // R-BACKFILL-2: the run did not end on the wipe; the Warden respawned on the release tick and the walk stopped there.
        Assert.DoesNotContain(events, e => e.Type == "run_end");
        // The death event carries its step's tick; the release counts from the post-step tick, as the wipe grace did.
        Assert.Equal(d + 1 + RunWiring.ReleaseDelayTicks, run.State.Tick);
        Assert.Contains(events, e => e.Type == "respawn" && Int(e, "actorId") == WardenId && e.Tick == d + 1 + RunWiring.ReleaseDelayTicks);
        // (b) every call is made while he lives, and there is at least one (vacuity guard).
        Assert.True(pings.Count > 0,
            $"no HUD ping SPREAD/STACK call in run B (Warden dead at {d}): the scenario no longer reaches the Pair's mark, the test is vacuous");
        Assert.All(pings, p => Assert.True(p.Tick < d, $"'{p.Line}' at tick {p.Tick}, not strictly before the Warden's death at {d}"));
        // (c) every telegraph live on the death tick (in the registry entering that step, not hit on it) cancels on
        // that tick, and none outlives the step.
        var hitAtDeath = events.Where(e => e.Type == "telegraph_hit" && e.Tick == d).Select(e => Int(e, "telegraphId")).ToHashSet();
        var liveAtDeath = registryBeforeDeath!.Where(id => !hitAtDeath.Contains(id)).OrderBy(x => x).ToList();
        var cancelledAtDeath = events.Where(e => e.Type == "telegraph_cancel" && e.Tick == d)
            .Select(e => Int(e, "telegraphId")).OrderBy(x => x).ToList();
        Assert.True(liveAtDeath.Count > 0, $"no telegraph live when the Warden died at {d}: nothing to cancel, the test is vacuous");
        Assert.Equal(liveAtDeath, cancelledAtDeath);
        Assert.True(registryAfterDeath!.Count == 0, $"telegraphs {string.Join(",", registryAfterDeath)} outlive the Warden's death at {d}");
        // Check 8's log count (a "telegraph" EVENT carries no telegraphId): drawn by d, less hit by d and cancelled before d.
        var logLive = telegraphs.Count(t => t.Tick <= d) - events.Count(e => e.Type == "telegraph_hit" && e.Tick <= d) -
            events.Count(e => e.Type == "telegraph_cancel" && e.Tick < d);
        Assert.Equal(liveAtDeath.Count, logLive);
        // (d) after the death: no cast starts on actor 1, no further call.
        Assert.True(castStartsOnWarden.Count == 0, $"a cast started on the dead Warden (died at {d}): {string.Join(", ", castStartsOnWarden.Take(5))}");
        Assert.DoesNotContain(pings, p => p.Tick >= d);
        Assert.True(telegraphs.Count > 0,
            $"no telegraph in run B (Warden dead at {death}): the scenario no longer reaches the Pair's mark, the test is vacuous");
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
