using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots;
using Keyrun.Bots.Shared;
using Keyrun.Content;
using Keyrun.Host;
using Keyrun.Hud;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using Xunit;

namespace Keyrun.Bots.Tests;

/// <summary>
/// HUD pull label (ledger: S1 spec for the HUD pull counter, WoW M+ objective rule, event seq 376): the objective's
/// "Pull k/n" label names the pull actually being fought. It advances when that pull ENGAGES (its <c>pull_start</c>),
/// never before, and an optional pack engaged between pulls never takes the next pull's label.
/// Observed failure (lanes/s5-capture-4135f4fc.txt): the label moved to the pair at 1440 and to the seed at 2100 and the
/// boss at 2460, but those pulls spawned at 1594, 2249 and 3433; the 8-member warrior-remnant pack engaged at 2462 under
/// "Pull 4/4 boss". Driven headless through the same composition the autorun uses (hollow-vault, key 2, seed 1, the
/// <see cref="ScriptedWarden"/> human seat, <see cref="BotCommandSource"/> bot seats, <see cref="Backfill.Step"/> per
/// tick, as HostPathThreatTelegraphTests does), reading <see cref="ObjectiveModel.Build"/> after every step.
/// </summary>
public sealed class PullLabelTests
{
    private const int MaxTicks = 24000;
    private const long Seed = 1;
    private const int Key = 2;
    private const string DungeonId = "hollow-vault";
    private const string HumanClassId = "warden";
    private const string BossPull = "boss";
    private const string DoneLabel = "done";
    private const string SeedPull = "seed";
    private const string RemnantPack = "warrior-remnant";
    private const string ChoirPull = "choir";

    /// <summary>
    /// One step of the run: the latest <c>pull_start</c> seen so far, the label the HUD shows, any pack engaged this step,
    /// and the pull the ruling expects the label to name (R-HUD-PULL-1 amendment c, see <see cref="ExpectedLabelPull"/>).
    /// </summary>
    private sealed record Sample(int Tick, string? LatestPullStart, string LabelPullId, string LabelTitle, string? PackEngaged, string ExpectedPullId,
        IReadOnlyList<int> EngagedLiveActors, bool PackLive, IReadOnlyList<int> ChoirLiveActors);

    /// <summary>
    /// The choir pull's spawns that are alive while the choir pull is engaged, read from the run state (independent of the
    /// ObjectiveModel). Empty when the choir pull is not engaged or every choir spawn is dead.
    /// </summary>
    private static IReadOnlyList<int> ChoirLiveActors(Run run)
    {
        if (!Pulls.IsEngaged(run.Encounter, ChoirPull))
        {
            return Array.Empty<int>();
        }
        return run.Dungeon.Pulls.Where(p => p.Id == ChoirPull)
            .SelectMany(p => p.Spawns.Select(s => s.ActorId))
            .Where(id => run.State.Actors.TryGetValue(id, out var a) && a.Alive)
            .ToList();
    }

    /// <summary>
    /// Ruling (d): the live engaged actors at one step. A non-boss pull's spawn counts when its pull is engaged and the
    /// spawn is alive; an optional pack member counts when its pack is engaged and the member is alive. <c>PackLive</c>
    /// is true when some optional pack is engaged with a live member (the anti-vacuity precondition).
    /// </summary>
    private static (IReadOnlyList<int> EngagedLiveActors, bool PackLive) EngagedLive(Run run)
    {
        bool Alive(int actorId) => run.State.Actors.TryGetValue(actorId, out var a) && a.Alive;
        var live = new List<int>();
        foreach (var pull in run.Dungeon.Pulls.Where(p => p.Id != BossPull))
        {
            if (!Pulls.IsEngaged(run.Encounter, pull.Id))
            {
                continue;
            }
            live.AddRange(pull.Spawns.Select(s => s.ActorId).Where(Alive));
        }
        var packLive = false;
        foreach (var pack in run.Content.Packs ?? Array.Empty<PackGroup>())
        {
            if (!Pulls.IsEngaged(run.Encounter, pack.Id))
            {
                continue;
            }
            var members = pack.Spawns.Select(s => s.ActorId).Where(Alive).ToList();
            packLive |= members.Count > 0;
            live.AddRange(members);
        }
        // Ruling (ii): an engaged patrol's live members are live non-boss actors too.
        foreach (var patrol in run.Content.Patrols ?? Array.Empty<PatrolPack>())
        {
            if (!Pulls.IsEngaged(run.Encounter, patrol.Id))
            {
                continue;
            }
            live.AddRange(patrol.Spawns.Select(s => s.ActorId).Where(Alive));
        }
        return (live, packLive);
    }

    /// <summary>
    /// Ruling (i): the party is walked into the warrior-remnant optional pack after the seed pull clears and before the boss
    /// pull starts, until that pack engages. True on those ticks only.
    /// </summary>
    private static bool DrivingToRemnant(Run run, string? latestPullStart)
    {
        if (latestPullStart != SeedPull || ExpectedLabelPull(run, latestPullStart, null, false) == SeedPull)
        {
            return false;
        }
        return !Pulls.IsEngaged(run.Encounter, RemnantPack);
    }

    /// <summary>
    /// Ruling (c): while the latest <c>pull_start</c>'s pull is live (some spawn alive or not yet spawned), the header names
    /// that pull. After it clears and before the next <c>pull_start</c>, the header names the next pull, the encounter's
    /// PullIndex. Liveness is read from the run state here, not from the ObjectiveModel, so the check is independent.
    /// Ruling (d) (R-HUD-PULL-1 amendment e) overrides (c) while any engaged non-boss actor is alive: the expected label is
    /// never the boss; the owner itself is the FIRST engaged living group in Aggro.GroupsOf order (ruling seq 415 F2,
    /// pinned by Owner_is_the_first_engaged_living_group_in_GroupsOf_order, not by this helper). With no engaged non-boss actor alive, or no non-boss pull started
    /// yet, (c) applies unchanged.
    /// </summary>
    private static string ExpectedLabelPull(Run run, string? latestPullStart, string? lastNonBossPullStart = null, bool engagedNonBossLive = false)
    {
        if (engagedNonBossLive && lastNonBossPullStart is not null)
        {
            return lastNonBossPullStart;
        }
        var pulls = run.Dungeon.Pulls;
        var latest = latestPullStart is null ? null : pulls.FirstOrDefault(p => p.Id == latestPullStart);
        if (latest is not null && latest.Spawns.Any(s => !run.State.Actors.TryGetValue(s.ActorId, out var a) || a.Alive))
        {
            return latest.Id;
        }
        var index = run.Encounter.PullIndex;
        return index < pulls.Count ? pulls[index].Id : DoneLabel;
    }

    private static readonly Lazy<IReadOnlyList<Sample>> Samples = new(Drive);

    private static IReadOnlyList<Sample> Drive()
    {
        var registry = ContentLoader.Load();
        var setup = HostRunSetup.Build(registry, DungeonId, Key, HumanClassId, withBots: true);
        var start = setup.StartFor(null, null, registry, HumanClassId);
        var options = (start.Options ?? new RunOptions()) with { RotationIndex = setup.RotationIndex };
        var party = setup.PartyFor(setup.Dungeon);
        options = setup.WithBotLoadouts(options, party) ?? options;
        var created = RunRoot.CreateRun(Seed, party, setup.Dungeon, setup.Content, options);
        var run = created with { Encounter = BossRules.Arm(created.Encounter, setup.Boss) };

        var warden = new ScriptedWarden();
        var bots = new BotCommandSource(setup.BotActorIds, KitRegistry.From(registry), Seed, BotCommandSource.DefaultSkillDial, null);
        bots.Reset();

        var backfill = BackfillState.Empty;
        string? latestPullStart = null;
        string? lastNonBossPullStart = null;
        var samples = new List<Sample>();
        while (run.State.Tick < MaxTicks && run.RunEnd.Result is null)
        {
            var commands = new List<Command>();
            // SimDriver.StepOnce order: the human's commands first, then every bot seat's.
            warden.Sample(run.State.Tick, run, commands, backfill.Open.Select(s => s.ActorId).ToList());
            bots.Sample(run.State.Tick, run, commands);
            bots.TakeReactions();
            if (DrivingToRemnant(run, latestPullStart) && run.Content.Packs?.FirstOrDefault(p => p.Id == RemnantPack) is { } remnant &&
                run.State.Actors.TryGetValue(HostRunSetup.HumanId, out var human) && human.Alive && run.Content.Layout is { } layout)
            {
                // Ruling (i): the human seat's own moves this tick are replaced by the walk into the pack (Follow.WalkTo, the
                // R-PATH-1 grid path); the bots follow the lead seat as they do in the autorun.
                commands.RemoveAll(c => c.ActorId == human.Id && c is MoveCommand or DashCommand);
                if (Follow.WalkTo(run.State.Tick, human, layout, remnant.Center) is { } walk)
                {
                    commands.Add(walk);
                }
            }
            var step = Backfill.Step(backfill, run, commands, Array.Empty<QueueRequest>());
            backfill = step.Backfill;
            run = step.Step.Run;

            string? packEngaged = null;
            foreach (var e in step.Step.Events)
            {
                if (e.Type == "pull_start")
                {
                    latestPullStart = ((JsString)e.Get("pullId")!).Value;
                    if (latestPullStart != BossPull)
                    {
                        lastNonBossPullStart = latestPullStart;
                    }
                }
                else if (e.Type == "pack_engage")
                {
                    packEngaged = ((JsString)e.Get("packId")!).Value;
                }
            }

            var line = ObjectiveModel.Build(run, registry);
            var (engagedLive, packLive) = EngagedLive(run);
            var expected = ExpectedLabelPull(run, latestPullStart, lastNonBossPullStart, engagedLive.Count > 0);
            samples.Add(new Sample(run.State.Tick, latestPullStart, line.PullId, line.Title, packEngaged, expected,
                engagedLive, packLive, ChoirLiveActors(run)));
        }

        return samples;
    }

    /// <summary>The samples while the run is open (the label is not the cleared "done" line).</summary>
    private static IEnumerable<Sample> OpenSamples() => Samples.Value.Where(s => s.LabelPullId != DoneLabel);

    /// <summary>
    /// Case c (R-HUD-PULL-1 c, ruling seq 405): the choir pull is engaged and at least one of its spawns (100, 101, 102) is
    /// alive, so the header must name choir. Checked at the LATEST open tick where a choir spawn is still alive and engaged,
    /// the tick most likely to show the observed bug (header named the pair while the choir was alive).
    /// </summary>
    [Fact(DisplayName = "HUD pull label (R-HUD-PULL-1 c): while the choir pull is engaged with a live spawn, the header names choir")]
    public void Header_names_choir_while_a_choir_spawn_is_alive_and_engaged()
    {
        var open = OpenSamples().ToList();
        Assert.NotEmpty(open);

        // Anti-vacuity: the choir pull must actually be engaged with a live spawn on some open tick, else the scenario did not occur.
        var choirTicks = open.Where(s => s.ChoirLiveActors.Count > 0).ToList();
        Assert.True(choirTicks.Count > 0,
            "precondition failed: the choir pull was never engaged with a live spawn on any open tick, so the scenario was not reproduced.");

        var target = choirTicks[^1];
        Assert.True(target.LabelPullId == ChoirPull,
            $"tick {target.Tick}: expected header pull \"{ChoirPull}\" while choir spawns [{string.Join(", ", target.ChoirLiveActors)}] are alive and engaged, " +
            $"but the HUD shows \"{target.LabelTitle}\" (pull {target.LabelPullId}); latest pull_start {target.LatestPullStart ?? "none"}");
    }

    [Fact(DisplayName = "HUD pull label (R-HUD-PULL-1 d): while any non-boss pull spawn or optional pack member is engaged and alive, the label never names the boss pull")]
    public void Engaged_live_non_boss_actor_never_shows_the_boss_label()
    {
        // Anti-vacuity precondition: the scenario must be reproduced, an optional pack engaged and alive on some open tick.
        var packTicks = string.Join("; ", OpenSamples().Where(s => s.PackEngaged is not null)
            .Select(s => $"tick {s.Tick} pack {s.PackEngaged} packLive={s.PackLive} engagedLive=[{string.Join(",", s.EngagedLiveActors)}] latest={s.LatestPullStart}"));
        Assert.True(OpenSamples().Any(s => s.PackLive),
            "precondition failed: no optional pack (the warrior remnant or any optional pack) was engaged and alive on any open tick, so the scenario was not reproduced. " +
            "pack_engage ticks: " + (packTicks.Length == 0 ? "none" : packTicks));

        var bossLabelWhileLive = OpenSamples()
            .Where(s => s.EngagedLiveActors.Count > 0 && s.LabelPullId == BossPull)
            .Select(s => $"tick {s.Tick}: HUD shows \"{s.LabelTitle}\" while engaged live actors [{string.Join(", ", s.EngagedLiveActors)}] exist " +
                         $"(latest pull_start {s.LatestPullStart ?? "none"}" +
                         (s.PackEngaged is null ? ")" : $", pack {s.PackEngaged} engaged this step)"))
            .ToList();
        Assert.Empty(bossLabelWhileLive);
    }

    // ---- Ruling seq 415 (F1 segment title, F2 first-in-GroupsOf-order owner, F3 no pull number on a pack/patrol line) ----

    private const string BellfoundryId = "sunken-bellfoundry";

    /// <summary>A sunken-bellfoundry run parked on its first segment boss pull with the chosen groups engaged.</summary>
    private static (Run Run, ContentRegistry Registry, IReadOnlyList<AggroGroup> Groups) BellfoundryAtBoss(Func<IReadOnlyList<AggroGroup>, IReadOnlyList<string>> engage)
    {
        var registry = ContentLoader.Load();
        var setup = DungeonPick.BuildSetup(registry, BellfoundryId, runSeed: 1, dungeonSeed: null, keyLevel: 2, humanClassId: HumanClassId);
        var run = RunRoot.CreateRun(1, setup.PartyFor(setup.Dungeon), setup.Dungeon, setup.Content);
        var content = registry.Dungeons[BellfoundryId];
        var firstBoss = LayoutDef.BossPullIds(content).First();
        var index = run.Dungeon.Pulls.Select(p => p.Id).ToList().IndexOf(firstBoss);
        Assert.True(index >= 0, "precondition: the first segment boss pull is a dungeon pull");
        var groups = Aggro.GroupsOf(run.Dungeon, run.State.Tick, run.Content.Packs, run.Content.Layout, run.Content.Patrols);
        run = run with { Encounter = run.Encounter with { PullIndex = index, Engaged = engage(groups) } };
        return (run, registry, groups);
    }

    private static bool LivingEnemy(Run run, int id) =>
        run.State.Actors.TryGetValue(id, out var a) && a.Alive && a.Kind == ActorKind.Enemy;

    [Fact(DisplayName = "HUD pull label (seq 415 F1): a multi-segment boss pull with an engaged live pack keeps the segment title; the count comes from the pack")]
    public void Multi_segment_engaged_pack_keeps_the_segment_title()
    {
        var (run, registry, groups) = BellfoundryAtBoss(g => new[] { g.First(x => x.PullIndex < 0).Id });
        var pack = groups.First(g => g.PullIndex < 0);
        var living = pack.ActorIds.Count(id => LivingEnemy(run, id));
        Assert.True(living > 0, "precondition: the engaged pack has living members");

        var line = ObjectiveModel.Build(run, registry);

        Assert.StartsWith("Segment 1/", line.Title);
        Assert.Contains(": toward ", line.Title);
        Assert.Equal(living, line.Remaining);
        Assert.Equal(pack.ActorIds.Count, line.Count);
    }

    [Fact(DisplayName = "HUD pull label (seq 415 F2): the owner is the FIRST engaged living group in GroupsOf order")]
    public void Owner_is_the_first_engaged_living_group_in_GroupsOf_order()
    {
        var (run, registry, groups) = BellfoundryAtBoss(g => g.Where(x => x.PullIndex < 0).Take(2).Select(x => x.Id).Reverse().ToList());
        var firstTwo = groups.Where(g => g.PullIndex < 0).Take(2).ToList();
        Assert.Equal(2, firstTwo.Count);
        Assert.All(firstTwo, g => Assert.True(g.ActorIds.Any(id => LivingEnemy(run, id)), "precondition: both engaged groups live"));

        var line = ObjectiveModel.Build(run, registry);

        Assert.Equal(firstTwo[0].Id, line.PullId);
        Assert.Equal(firstTwo[0].ActorIds.Count(id => LivingEnemy(run, id)), line.Remaining);
    }

    [Fact(DisplayName = "HUD pull label (seq 415 F3): a pack or patrol owner line shows no pull number")]
    public void Pack_owner_line_shows_no_pull_number()
    {
        var registry = ContentLoader.Load();
        var setup = HostRunSetup.Build(registry, DungeonId, Key, HumanClassId, withBots: true);
        var run = RunRoot.CreateRun(Seed, setup.PartyFor(setup.Dungeon), setup.Dungeon, setup.Content);
        var groups = Aggro.GroupsOf(run.Dungeon, run.State.Tick, run.Content.Packs, run.Content.Layout, run.Content.Patrols);
        var pack = groups.First(g => g.PullIndex < 0 && g.ActorIds.Any(id => LivingEnemy(run, id)));
        run = run with { Encounter = run.Encounter with { PullIndex = run.Dungeon.Pulls.Count - 1, Engaged = new[] { pack.Id } } };

        var line = ObjectiveModel.Build(run, registry);

        Assert.Equal(pack.Id, line.PullId);
        Assert.DoesNotContain("Pull ", line.Title);
        Assert.DoesNotMatch(@"\d+/\d+", line.Title);
    }
}
