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
    /// the same facts check 8 reads from the host log: (a) the Warden's death tick (his <c>death</c> event); (b) every
    /// <c>HUD ping label=SPREAD|STACK</c> call (Pings.OnStepped: a new <see cref="PingCall.Key"/> after a step, logged at
    /// that step's tick) comes strictly before it. Re-pointed 2026-10-21: run B has no caster telegraph and no Pair mark
    /// lands before the death, so no call is required here; the live call is proven where a mark lands (the bot party
    /// test below, and HudSceneTest check 11 / E2eAssert). (c) no Pair caster telegraph is drawn at any tick, no telegraph
    /// is drawn after the death, and on the death tick the live telegraphs are exactly the <c>telegraph_cancel</c> EVENTs
    /// there (TELEGRAPH-CANCEL-ON-DEATH; none are live now, so that is zero); (d) after
    /// it no cast starts on actor 1 (R-DEADCAST-1) and no further call appears while he is dead. R-BACKFILL-2 (f-47a7a6db):
    /// the solo wipe no longer ends run B after the wipe grace; the dead Warden releases and respawns at the entrance
    /// <see cref="RunWiring.ReleaseDelayTicks"/> after his death, so "after the death" is his time dead, up to and
    /// including the respawn step, and the walk stops there.
    /// </summary>
    [Fact(DisplayName = "Run B (pull 2, solo Warden): the Pair draws no mark telegraph, no mark ring is centred on the dead Warden, and no cast starts on him")]
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
        var ringsOnDeadWarden = 0;
        int? death = null;
        while (run.State.Tick < 900 && !(death is not null && run.State.Actors[WardenId].Alive))
        {
            var tick = run.State.Tick;
            var before = run.State;
            var commands = new List<Command>();
            warden.Sample(tick, run, commands, null);
            var step = Backfill.Step(backfill, run, commands, Array.Empty<QueueRequest>());
            backfill = step.Backfill;
            run = step.Step.Run;
            events.AddRange(step.Step.Events);
            // R-PILE-10 (mark form): while he is dead, no Pair mark ring is centred on him (PairMarks.Rings reads live actors).
            if (death is not null && !run.State.Actors[WardenId].Alive)
            {
                ringsOnDeadWarden += Keyrun.Sim.Combat.PairMarks.Rings(run.State, setup.Content.Marks).Count(r => r.ActorId == WardenId);
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

        // (a) the death tick.
        Assert.True(death is not null, "the solo Warden never died in run B");
        var d = death!.Value;
        // R-BACKFILL-2: the run did not end on the wipe; the Warden respawned on the release tick and the walk stopped there.
        Assert.DoesNotContain(events, e => e.Type == "run_end");
        // The death event carries its step's tick; the release counts from the post-step tick, as the wipe grace did.
        Assert.Equal(d + 1 + RunWiring.ReleaseDelayTicks, run.State.Tick);
        Assert.Contains(events, e => e.Type == "respawn" && Int(e, "actorId") == WardenId && e.Tick == d + 1 + RunWiring.ReleaseDelayTicks);
        // (b) the Pair draws no mark telegraph in run B (a caster circle is gone, BIBLE s3 s9; a mark ring exists only once a
        // mark lands), and every SPREAD/STACK call that does appear is made while he lives. Measured on this pull: no
        // pair.spreadMark or pair.stackCall completes before the Warden dies, so no call appears here before the death.
        Assert.DoesNotContain(events, e => e.Type == "telegraph" && Str(e, "cast").StartsWith("pair.", StringComparison.Ordinal));
        Assert.All(pings, p => Assert.True(p.Tick < d, $"'{p.Line}' at tick {p.Tick}, not strictly before the Warden's death at {d}"));
        // (c) telegraphs: none is drawn after the death, and the cancels on the death tick are exactly the telegraphs live then
        // (drawn up to it, less the hits up to it and the cancels before it). Zero on both sides in run B now.
        Assert.DoesNotContain(events, e => e.Type == "telegraph" && e.Tick > d);
        var liveAtDeath = events.Count(e => e.Type == "telegraph" && e.Tick <= d)
            - events.Count(e => e.Type == "telegraph_hit" && e.Tick <= d)
            - events.Count(e => e.Type == "telegraph_cancel" && e.Tick < d);
        Assert.Equal(liveAtDeath, events.Count(e => e.Type == "telegraph_cancel" && e.Tick == d));
        // (c) the marks are a real rule in this run: the host's mark rules are loaded, and no ring is centred on him while dead.
        Assert.NotNull(setup.Content.Marks);
        Assert.True(ringsOnDeadWarden == 0, $"{ringsOnDeadWarden} Pair mark ring(s) centred on the Warden while he was dead (died at {d})");
        // (d) after the death: no cast starts on actor 1, no further call.
        Assert.True(castStartsOnWarden.Count == 0, $"a cast started on the dead Warden (died at {d}): {string.Join(", ", castStartsOnWarden.Take(5))}");
        Assert.DoesNotContain(pings, p => p.Tick >= d);
        Assert.True(castsAfterDeath.Count == 0, $"an enemy holds a cast after its only target died at {d}: {string.Join(", ", castsAfterDeath.Take(5))}");
    }

    [Fact(DisplayName = "Pair pull with the bot party: a SPREAD/STACK mark lands on a living player and prints a live call (the live-ping proof of E2eAssert and HudSceneTest check 11)")]
    public void Pair_marks_land_on_the_bot_party_and_print_a_live_call()
    {
        // Run B's pull with the bot party (no solo wipe): with the Pair's caster telegraphs removed, the only SPREAD/STACK
        // call is the mark ring's (PingCalls mark source, emitted at the step the mark lands), so a live call proves a mark landed.
        var registry = ContentLoader.Load();
        var setup = HostRunSetup.Build(registry, "hollow-vault", Key, "warden", withBots: true);
        var run = Create(setup, registry, 2, null, null);
        var bots = new BotCommandSource(setup.BotActorIds, KitRegistry.From(registry), Seed, BotCommandSource.DefaultSkillDial, null);
        var backfill = BackfillState.Empty;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var markCalls = new List<PingCall>();
        while (run.State.Tick < 900 && run.RunEnd.ResultTick is null)
        {
            var commands = new List<Command>();
            bots.Sample(run.State.Tick, run, commands);
            var step = Backfill.Step(backfill, run, commands, Array.Empty<QueueRequest>());
            backfill = step.Backfill;
            run = step.Step.Run;
            markCalls.AddRange(PingCalls.Calls(run).Where(c => c.Source == PingSource.Mark && seen.Add(c.Key)));
        }
        Assert.NotEmpty(markCalls);
        Assert.Contains(markCalls, c => c.Label == "SPREAD" || c.Label == "STACK");
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
        // Golden re-recorded 2026-10-21 on 24916b4b under the sim-tick recorder (R-CLOCK-2): Enter 0 -> 9, G 2 -> 11, Enter end+3 (694) -> end+12 (703), Enter end+6 (697) -> end+15 (706); the run starts on screen frame 9, so each run-end Enter is +9.
        var expected = new List<(int, string?, bool)> { (9, "Enter", true), (11, "G", true), (end + 12, "Enter", true), (end + 15, "Enter", true) };
        Assert.Equal(expected, recorded);
    }
}
