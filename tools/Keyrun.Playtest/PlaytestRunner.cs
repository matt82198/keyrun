using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Keyrun.Bots;
using Keyrun.Bots.Roles;
using Keyrun.Bots.Shared;
using Keyrun.Host;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using C = Keyrun.Content;

namespace Keyrun.Playtest;

/// <summary>What to play.</summary>
/// <param name="Key">The key level (2-20).</param>
/// <param name="Seed">The run seed.</param>
/// <param name="Policy">The playtester's plan, or null for the old scripted party (unchanged behaviour).</param>
public sealed record PlaytestOptions(int Key, long Seed, Policy? Policy)
{
    /// <summary>The dungeon (the POC has one).</summary>
    public string DungeonId { get; init; } = "hollow-vault";

    /// <summary>
    /// Seat 1's class (D65/D66, HUMAN-PLAY): the class the host's human picks on the Character tab. Seat 1 takes that
    /// class's roster slot and the two other roster classes fill seats 2 and 3 in roster order
    /// (<see cref="HostRunSetup.BuildParty"/>), so the party is always one of each role. Default: the Warden, the run this
    /// runner always played.
    /// </summary>
    public string HumanClassId { get; init; } = HostRunSetup.Roster[0].ClassId;

    /// <summary>Hard stop, ticks; a run still going is "unfinished". Default: twice par.</summary>
    public int? MaxTicks { get; init; }

    /// <summary>
    /// Test seam (read-only): called once per tick with the tick, the run before the step and the commands about to be
    /// stepped, after the leader's travel and fight strip are applied. Null in play.
    /// </summary>
    public Action<int, Run, IReadOnlyList<Command>>? Observe { get; init; }

    /// <summary>
    /// G2.4: receives one <see cref="Keyrun.Bots.Shared.BotReceipt"/> per command stepped, in issue order (the playtest
    /// host writes bot-receipts.jsonl next to run.log). Null (the default) records nothing.
    /// </summary>
    public Keyrun.Bots.Shared.IBotReceiptSink? Receipts { get; init; }

    /// <summary>
    /// Finding f-3dcc702c (WoW: plan the count, pull to 100, then boss): the leader pulls the layout's packs until the Enemy
    /// Forces bar is full before it walks to the boss, as the host autorun Warden does. False restores the old walk past
    /// every optional pack (a control for tests). Null (the default) is true, for the scripted party and for a policy run
    /// alike (f-7aa25aa6: a finish needs 100% forces, so a playtester's party counts the bar too; the plan is the leader's
    /// travel, the policy still owns kicks, targets and defensives).
    /// </summary>
    public bool? PullToForces { get; init; }

    /// <summary>
    /// The arena's <c>--talent SEAT:TALENT</c> picks (finding f-a3de0a01, <see cref="RunRequest.Talents"/>), so the
    /// headless replica plays the seats an autorun capture talents. Empty (the default) leaves the run unchanged.
    /// </summary>
    public IReadOnlyList<SeatTalent> Talents { get; init; } = Array.Empty<SeatTalent>();
}

/// <summary>A finished playtest: the final run, the event/command log and the stats the summary reads.</summary>
/// <param name="Run">The run at the last step.</param>
/// <param name="Log">One line per command ("C ...") and per event ("E {...}") in step order.</param>
/// <param name="Stats">What the observer counted.</param>
/// <param name="Setup">The host setup the run was built from.</param>
/// <param name="Registry">The content the run used (talents applied).</param>
public sealed record PlaytestResult(Run Run, string Log, RunStats Stats, HostRunSetup Setup, C.ContentRegistry Registry);

/// <summary>
/// Plays one Hollow Vault run headlessly with the three host seats (Warden 1, Chanter 2, Cinder 3 by default; seat 1 is
/// <see cref="PlaytestOptions.HumanClassId"/> and the other two classes follow in roster order) all driven by bots,
/// built through the host's assembly path (<see cref="HostRunSetup.Build"/>, then <see cref="HostRunFactory.Create"/>,
/// which ends in <see cref="BossRules.Arm"/>; without the bot seats' default loadouts, as before). The leader
/// (<see cref="Follow.Leader"/>, the lowest living seat) walks the route between fights (<see cref="Follow.LeadRoute"/>, the autorun Warden walk) and keeps only its casts
/// in a fight. With no policy every seat is the scripted bot (<see cref="Bot.MakeBot"/> over
/// <see cref="RoleSet.Default"/>, dial 80, bot PRNG from the seed), so the run is today's bot run byte for byte; with a
/// policy every seat is a <see cref="PolicyBot"/>.
/// </summary>
public static class PlaytestRunner
{
    /// <summary>The tank seat (the host human seat, the Warden); the damage dealers assist its target.</summary>
    public const int TankId = HostRunSetup.HumanId;

    /// <summary>The scripted bots' skill dial (BotCommandSource.DefaultSkillDial).</summary>
    public const int ScriptedSkillDial = 80;

    /// <summary>The content with a policy's talent picks applied (each picked fork's ability patched in place).</summary>
    /// <param name="registry">The base content.</param>
    /// <param name="policy">The plan.</param>
    /// <returns>The patched content (the same instance when nothing is picked).</returns>
    public static C.ContentRegistry WithTalents(C.ContentRegistry registry, Policy policy)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(policy);
        var patched = new Dictionary<string, C.AbilityDef>(StringComparer.Ordinal);
        foreach (var (classId, picks) in policy.Talents)
        {
            var cls = registry.Classes[classId];
            var talents = C.TalentPicks.Start(cls);
            foreach (var (forkId, optionId) in picks)
            {
                var fork = cls.Talents.Select((f, i) => (f, i)).First(x => x.f.AbilityId == forkId);
                var option = fork.f.Options.Select((o, i) => (o, i)).First(x => x.o.Id == optionId).i;
                talents = talents.Pick(fork.i, option);
            }
            foreach (var (id, def) in talents.Resolve(registry.Abilities))
            {
                patched[id] = def;
            }
        }
        if (patched.Count == 0)
        {
            return registry;
        }
        var merged = registry.Abilities.Select(kv => patched.TryGetValue(kv.Key, out var p) ? new KeyValuePair<string, C.AbilityDef>(kv.Key, p) : kv);
        return registry with { Abilities = new C.ValueMap<C.AbilityDef>(merged) };
    }

    /// <summary>The policy pull id of the run's active pull: the content pull id, or "boss" for the boss pull.</summary>
    /// <param name="run">The run.</param>
    /// <returns>The id.</returns>
    public static string PullIdOf(Run run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var index = run.Encounter.PullIndex;
        return index >= run.Dungeon.Pulls.Count - 1 ? "boss" : run.Dungeon.Pulls[index].Id;
    }

    /// <summary>Plays the run.</summary>
    /// <param name="options">What to play.</param>
    /// <param name="baseRegistry">The content (default: the embedded content).</param>
    /// <returns>The result.</returns>
    public static PlaytestResult Play(PlaytestOptions options, C.ContentRegistry? baseRegistry = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        var content = baseRegistry ?? C.ContentLoader.Load();
        var policy = options.Policy;
        var registry = policy is null ? content : WithTalents(content, policy);
        // R-MULTIBOSS-1: the run seed also generates a multi-segment dungeon's layout (the Sunken Bellfoundry); the Hollow Vault ignores it.
        var setup = HostRunSetup.Build(registry, options.DungeonId, options.Key, options.HumanClassId, withBots: true, dungeonSeed: options.Seed);
        // R7: HostRunFactory, the host's assembly path. No bot loadouts and no options record, as this runner always
        // built its runs (RunRequest.BotLoadouts), so the playtest output is unchanged.
        var run = HostRunFactory.Create(setup, new RunRequest(options.Seed, registry, options.HumanClassId)
        {
            BotLoadouts = false,
            RotationOptions = false,
            Talents = options.Talents,
        }).Run;
        var kits = KitRegistry.From(registry);
        var context = new PolicyContext();
        var forces = (options.PullToForces ?? true) ? ForcesPlan.Of(registry) : null;

        var bots = new SortedDictionary<int, IBot>();
        var memories = new SortedDictionary<int, BotMemory>();
        var policyBots = new Dictionary<int, PolicyBot>();
        var scripted = Bot.MakeBot(RoleSet.Default);
        foreach (var p in setup.Party)
        {
            if (policy is null)
            {
                bots[p.Id] = scripted;
            }
            else
            {
                var pb = new PolicyBot(policy, context, registry, options.Seed, p.Id, TankId);
                policyBots[p.Id] = pb;
                bots[p.Id] = pb;
            }
            memories[p.Id] = BotMemory.Default(ScriptedSkillDial);
        }
        IReadOnlyDictionary<int, BotMemory> mem = memories;
        var prng = Skill.SeedPrng(options.Seed);
        var maxTicks = options.MaxTicks ?? (2 * (setup.Content.ParTicks ?? 24000));
        var log = new StringBuilder();
        var stats = new RunStats(registry, setup.Party);

        while (run.State.Tick < maxTicks && run.RunEnd.ResultTick is null)
        {
            var tick = run.State.Tick;
            context.PullId = PullIdOf(run);
            var view = BotViews.BuildBotView(run.State, run.Encounter.Telegraphs.Select(BotViews.ToBotTelegraph), ClassKitView.Empty, kits) with { Layout = run.Content.Layout, PullPlan = PullPlan.Of(run, forces) };
            var batch = Bot.RunBots(view, bots, mem, prng);
            mem = batch.Memories;
            prng = batch.Prng;
            var commands = batch.Commands.ToList();
            if (Follow.Leader(run.State) is { } leader)
            {
                var leaderId = leader.Id;
                var dodge = policyBots.TryGetValue(leaderId, out var lb) ? lb.LastDodge : null;
                if (!Follow.AnyLivingEnemy(run.State))
                {
                    commands.RemoveAll(c => c.ActorId == leaderId);
                    if (!Resting(run.State, policy) && Follow.LeadRoute(tick, leader, run, null, forces) is { } walk)
                    {
                        commands.Add(walk);
                    }
                }
                else
                {
                    // R-SEED-1a (f-244d102b): the host passes the leader's fight approach through (BotCommandSource.cs:162-166
                    // adds every batch command; ScriptedWarden.cs:81-97 emits its own approach moves), so the mirror exempts
                    // exactly the TankEngage approach move, identified by its origin (TankEngage.ApproachMove), and no other.
                    var approach = leader.Role == ActorRole.Tank
                        ? TankEngage.ApproachMove(view, leader, view.Kits.ResolveKit(leader.ClassId, view.ClassKit))
                        : null;
                    // KICK-LEDGER-COOLDOWN-GAP (f-13b5c33a): the same holds for the step a scripted leader takes toward a caster its
                    // Shield Bash is named to kick but cannot reach yet (RoleShared.LedgerKickApproachCommand); a plan seat's kicks
                    // come from the plan, so only the scripted party exempts it.
                    var kickWalk = policy is null
                        ? RoleShared.LedgerKickApproachCommand(view, leader, view.Kits.ResolveKit(leader.ClassId, view.ClassKit))
                        : null;
                    // R-AOE-1 (c), f-c8ed3a62: the tank chain-pulls. ChainPull.Move is the one origin of that walk (the lead-seat bot
                    // and the host's scripted Warden call it too); while it holds it replaces every other move the leader queued
                    // but the dodge, and the leader's casts stay.
                    var chain = view.PullPlan is { } chainPlan && view.Layout is { } chainLayout
                        ? ChainPull.Move(tick, run.State, chainLayout, chainPlan, leader)
                        : null;
                    commands.RemoveAll(c => c.ActorId == leaderId && c is MoveCommand or DashCommand && !Equals(c, dodge) && (chain is not null || (!Equals(c, approach) && !Equals(c, kickWalk))));
                    if (chain is not null)
                    {
                        commands.Add(chain);
                    }
                }
            }

            foreach (var c in commands)
            {
                log.Append("C ").Append(c).Append('\n');
            }
            var receipts = options.Receipts ?? Keyrun.Bots.Shared.NullBotReceiptSink.Instance;
            foreach (var c in commands)
            {
                var seatClass = run.State.Actors.TryGetValue(c.ActorId, out var seat) ? seat.ClassId : string.Empty;
                receipts.Record(Keyrun.Bots.Shared.BotReceipt.Of(tick, seatClass, c, Reasons.Of(c).Why));
            }
            stats.ObserveCommands(commands);
            options.Observe?.Invoke(tick, run, commands);
            var before = run;
            var stepped = RunRoot.StepRun(run, commands);
            run = stepped.Run;
            foreach (var e in stepped.Events)
            {
                log.Append("E ").Append(e.ToCanonicalJson()).Append('\n');
            }
            stats.ObserveStep(before, stepped.Events, PullIdOf(before));
        }
        foreach (var (id, pb) in policyBots)
        {
            stats.NoteHands(id, pb.LateKicks, pb.DodgeMoves);
        }
        return new PlaytestResult(run, log.ToString(), stats, setup, registry);
    }

    // pull.restHealthPct: the leader waits while any living seat is under the threshold.
    private static bool Resting(SimState state, Policy? policy)
    {
        if (policy is null || policy.RestHealthPct <= 0)
        {
            return false;
        }
        return state.Actors.Values.Any(a => a.Kind == ActorKind.Player && a.Alive && a.MaxHealth > 0 && a.Health * 100 < policy.RestHealthPct * a.MaxHealth);
    }
}
