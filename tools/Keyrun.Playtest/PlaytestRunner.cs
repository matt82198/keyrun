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

    /// <summary>Hard stop, ticks; a run still going is "unfinished". Default: twice par.</summary>
    public int? MaxTicks { get; init; }

    /// <summary>
    /// Test seam (read-only): called once per tick with the tick, the run before the step and the commands about to be
    /// stepped, after the leader's travel and fight strip are applied. Null in play.
    /// </summary>
    public Action<int, Run, IReadOnlyList<Command>>? Observe { get; init; }

    /// <summary>
    /// Finding f-3dcc702c (WoW: plan the count, pull to 100, then boss): the leader pulls the layout's packs until the Enemy
    /// Forces bar is full before it walks to the boss, as the host autorun Warden does. False restores the old walk past
    /// every optional pack (a control for tests). Null (the default) is true for the scripted party (no policy) and false
    /// for a policy run: a playtester's policy owns its pulls, and a pull plan in a policy is a follow-up.
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
/// Plays one Hollow Vault run headlessly with the three host seats (Warden 1, Chanter 2, Cinder 3) all driven by bots,
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
        var setup = HostRunSetup.Build(registry, options.DungeonId, options.Key, HostRunSetup.Roster[0].ClassId, withBots: true);
        // R7: HostRunFactory, the host's assembly path. No bot loadouts and no options record, as this runner always
        // built its runs (RunRequest.BotLoadouts), so the playtest output is unchanged.
        var run = HostRunFactory.Create(setup, new RunRequest(options.Seed, registry, HostRunSetup.Roster[0].ClassId)
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
            var view = BotViews.BuildBotView(run.State, run.Encounter.Telegraphs.Select(BotViews.ToBotTelegraph), ClassKitView.Empty, kits) with { Layout = run.Content.Layout };
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
                    commands.RemoveAll(c => c.ActorId == leaderId && c is MoveCommand or DashCommand && !Equals(c, dodge) && !Equals(c, approach));
                }
            }

            foreach (var c in commands)
            {
                log.Append("C ").Append(c).Append('\n');
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
