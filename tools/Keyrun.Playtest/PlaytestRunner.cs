using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Keyrun.Bots;
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
/// built exactly as the host builds a run (<see cref="HostRunSetup.Build"/>, then <see cref="BossRules.Arm"/>). The leader
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
        var created = RunRoot.CreateRun(options.Seed, setup.Party, setup.Dungeon, setup.Content, null);
        var run = created with { Encounter = BossRules.Arm(created.Encounter, setup.Boss) };
        var kits = KitRegistry.From(registry);
        var context = new PolicyContext();

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
                    if (!Resting(run.State, policy) && Follow.LeadRoute(tick, leader, run) is { } walk)
                    {
                        commands.Add(walk);
                    }
                }
                else
                {
                    // R-SEED-1 rule (c): in a fight the leader keeps only the close-in move Follow.LeadCommand gives it
                    // (no living engaged enemy in melee range of the party: walk to the nearest engaged enemy).
                    // The view carries the run's ability defs (KitRegistry.ForRun), as the host's BotCommandSource does, so the
                    // leader reads its basic-attack reach from the run's own warden.strike (3000 mm).
                    var closeIn = Follow.LeadCommand(view with { Kits = kits.ForRun(run.Content.Abilities) }, leader);
                    // A zero-dir MoveCommand is the leader's turn (TankEngage faces its target before it casts), not travel:
                    // it stays, so the cone it casts lands where the leader faces.
                    commands.RemoveAll(c => c.ActorId == leaderId && c is MoveCommand or DashCommand && !Equals(c, dodge) && !IsTurn(c));
                    if (closeIn is not null && !commands.Contains(closeIn))
                    {
                        commands.Add(closeIn);
                    }
                }
            }

            foreach (var c in commands)
            {
                log.Append("C ").Append(c).Append('\n');
            }
            stats.ObserveCommands(commands);
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

    // R-SEED-1: a leader MoveCommand with no travel (zero dir) only sets facing.
    private static bool IsTurn(Command c) => c is MoveCommand { Dir: { X: 0, Z: 0 } };

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
