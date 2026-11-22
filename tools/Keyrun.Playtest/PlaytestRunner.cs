using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Keyrun.Bots;
using Keyrun.Bots.Guides;
using Keyrun.Bots.Roles;
using Keyrun.Bots.Shared;
using Keyrun.Host;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Runs;
using C = Keyrun.Content;

namespace Keyrun.Playtest;

/// <summary>The text retained in <see cref="PlaytestResult.Log"/> after a run.</summary>
public enum PlaytestLogMode
{
    /// <summary>Commands, events and per-tick capture lines.</summary>
    Full,
    /// <summary>Only canonical event lines.</summary>
    Events,
    /// <summary>Keep only the run and its statistics.</summary>
    None,
}

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

    /// <summary>
    /// R-K6-2 (a seeded segmented dungeon, <see cref="DungeonId"/>): the branch (0 or 1) the leader walks in every segment, so a
    /// playtest can force the party onto the other branch. The bots follow the leader seat (D44). Null (the default) keeps the
    /// seeded pick (<c>SeededLayout.BotBranches</c>); a hand-built dungeon ignores it.
    /// </summary>
    public int? LeaderBranch { get; init; }

    /// <summary>The retained log detail. Full preserves the host capture format.</summary>
    public PlaytestLogMode LogMode { get; init; } = PlaytestLogMode.Full;

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
/// <param name="Log">The requested log lines in step order, or an empty string when logging is disabled.</param>
/// <param name="Stats">What the observer counted.</param>
/// <param name="Setup">The host setup the run was built from.</param>
/// <param name="Registry">The content the run used (talents applied).</param>
public sealed record PlaytestResult(Run Run, string Log, RunStats Stats, HostRunSetup Setup, C.ContentRegistry Registry)
{
    private readonly Lazy<string> _log = new(Log);

    /// <summary>The requested log text. Large captures expand on first access.</summary>
    public string Log
    {
        get => _log.Value;
        init => _log = new(value);
    }

    internal PlaytestResult(Run run, Lazy<string> log, RunStats stats, HostRunSetup setup, C.ContentRegistry registry)
        : this(run, string.Empty, stats, setup, registry) => _log = log;

    /// <summary>Compares the positional fields, including the log text.</summary>
    public bool Equals(PlaytestResult? other) => ReferenceEquals(this, other) ||
        (other is not null && Run == other.Run && Stats == other.Stats && Setup == other.Setup &&
            Registry == other.Registry && string.Equals(Log, other.Log, StringComparison.Ordinal));

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Run, Log, Stats, Setup, Registry);
}

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
    // Full captures can be tens of millions of characters. Keep only the current batch
    // in plain text while stepping. Small logs never allocate a compression stream.
    private sealed class RunLogBuffer : IDisposable
    {
        private const int BatchChars = 32 * 1024;
        private readonly MemoryStream _storage = new();
        private DeflateStream? _writer;
        private int _flushedChars;

        internal StringBuilder Text { get; } = new();

        internal void FlushBatch()
        {
            if (Text.Length < BatchChars)
            {
                return;
            }
            _writer ??= new DeflateStream(_storage, CompressionLevel.Fastest, leaveOpen: true);
            WriteText(_writer);
        }

        private void WriteText(Stream stream)
        {
            foreach (var chunk in Text.GetChunks())
            {
                stream.Write(MemoryMarshal.AsBytes(chunk.Span));
            }
            _flushedChars = checked(_flushedChars + Text.Length);
            Text.Clear();
        }

        internal Lazy<string> Finish()
        {
            if (_writer is null)
            {
                return new Lazy<string>(Text.ToString());
            }
            WriteText(_writer);
            _writer.Dispose();
            _writer = null;
            var bytes = _storage.GetBuffer();
            var byteCount = checked((int)_storage.Length);
            var charCount = _flushedChars;
            return new Lazy<string>(() =>
            {
                using var data = new MemoryStream(bytes, 0, byteCount, writable: false);
                using var reader = new DeflateStream(data, CompressionMode.Decompress);
                return string.Create(charCount, reader, static (chars, source) =>
                    source.ReadExactly(MemoryMarshal.AsBytes(chars)));
            });
        }

        public void Dispose()
        {
            _writer?.Dispose();
            _storage.Dispose();
        }
    }

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
        // K3a (design/PLAYTESTER-GUIDE.md section 1): a plan states its mode; no plan is the guided scripted party.
        var mode = policy?.Mode ?? Keyrun.Bots.Shared.PlaytesterMode.Guided;
        var registry = policy is null ? content : WithTalents(content, policy);
        // R-MULTIBOSS-1: the run seed also generates a multi-segment dungeon's layout (the Sunken Bellfoundry); the Hollow Vault ignores it.
        var setup = HostRunSetup.Build(registry, options.DungeonId, options.Key, options.HumanClassId, withBots: true, dungeonSeed: options.Seed, leaderBranch: options.LeaderBranch);
        // R7: HostRunFactory, the host's assembly path. No bot loadouts and no options record, as this runner always
        // built its runs (RunRequest.BotLoadouts), so the playtest output is unchanged.
        var run = HostRunFactory.Create(setup, new RunRequest(options.Seed, registry, options.HumanClassId)
        {
            BotLoadouts = false,
            RotationOptions = false,
            Talents = options.Talents,
        }).Run;
        var kits = KitRegistry.From(registry);
        // G2.4 K3b: a guided plan loads the playtester guides (embedded in Keyrun.Content from game/content/data/guides/),
        // failing closed here when none is loaded. A blind plan and the scripted party load nothing.
        var guides = policy is not null && mode == Keyrun.Bots.Shared.PlaytesterMode.Guided ? LoadGuides(registry) : null;
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
                var pb = new PolicyBot(policy, context, registry, options.Seed, p.Id, TankId, guides);
                policyBots[p.Id] = pb;
                bots[p.Id] = pb;
            }
            memories[p.Id] = BotMemory.Default(ScriptedSkillDial);
        }
        IReadOnlyDictionary<int, BotMemory> mem = memories;
        var prng = Skill.SeedPrng(options.Seed);
        var maxTicks = options.MaxTicks ?? (2 * (setup.Content.ParTicks ?? 24000));
        using var buffer = options.LogMode == PlaytestLogMode.None ? null : new RunLogBuffer();
        var log = buffer?.Text;
        var stats = new RunStats(registry, setup.Party, kits);
        // BOT-RESPAWN-RETURN (R-PATH-5 (m)): a seat seen down that is alive again has respawned; its return walk runs until it is
        // back within Follow.ReturnReachMm of a living seat or an engaged enemy (Follow.ReturnSeat returns null).
        var seatsDown = new HashSet<int>();
        var returning = new HashSet<int>();

        while (run.State.Tick < maxTicks && run.RunEnd.ResultTick is null)
        {
            var tick = run.State.Tick;
            context.PullId = PullIdOf(run);
            foreach (var seat in run.State.Actors.Values)
            {
                if (seat.Kind != ActorKind.Player)
                {
                    continue;
                }
                if (!seat.Alive)
                {
                    seatsDown.Add(seat.Id);
                    continue;
                }
                if (seatsDown.Remove(seat.Id))
                {
                    returning.Add(seat.Id);
                }
                if (returning.Contains(seat.Id) && Follow.ReturnSeat(seat, run.State) is null)
                {
                    returning.Remove(seat.Id);
                }
            }
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
                    // BOT-RESPAWN-RETURN (R-PATH-5 (l)): a respawned leader farther than Follow.ReturnReachMm from every living seat
                    // and every engaged enemy walks back to the nearest living seat over the nav path; the filter admits that move
                    // (the same move ScriptedWarden.SampleRotation issues on the host path) and strips nothing else.
                    var back = returning.Contains(leaderId) && run.Content.Layout is { } backLayout && Follow.ReturnSeat(leader, run.State) is { } backSeat
                        ? Follow.WalkTo(tick, leader, backLayout, backSeat.Pos)
                        : null;
                    commands.RemoveAll(c => c.ActorId == leaderId && c is MoveCommand or DashCommand && !Equals(c, dodge) && (chain is not null || (!Equals(c, approach) && !Equals(c, kickWalk) && !Equals(c, back))));
                    if (chain is not null)
                    {
                        commands.Add(chain);
                    }
                    else if (back is not null && !commands.Contains(back))
                    {
                        commands.Add(back);
                    }
                }
            }

            if (log is not null && options.LogMode == PlaytestLogMode.Full)
            {
                foreach (var c in commands)
                {
                    log.Append("C ").Append(c).Append('\n');
                }
            }
            var receipts = options.Receipts ?? Keyrun.Bots.Shared.NullBotReceiptSink.Instance;
            foreach (var c in commands)
            {
                var seatClass = run.State.Actors.TryGetValue(c.ActorId, out var seat) ? seat.ClassId : string.Empty;
                var reason = Reasons.Of(c);
                var receiptReason = reason.RuleId.StartsWith("guide:", StringComparison.Ordinal) ? reason.RuleId : reason.Why;
                receipts.Record(Keyrun.Bots.Shared.BotReceipt.Of(tick, seatClass, c, receiptReason, mode));
            }
            stats.ObserveCommands(commands);
            options.Observe?.Invoke(tick, run, commands);
            var before = run;
            var stepped = RunRoot.StepRun(run, commands);
            run = stepped.Run;
            if (log is not null)
            {
                foreach (var e in stepped.Events)
                {
                    log.Append("E ").Append(e.ToCanonicalJson()).Append('\n');
                }
            }
            if (log is not null && options.LogMode == PlaytestLogMode.Full)
            {
                // R-AGGRO-1 / G7: the capture lines (TICK, ACTOR, GROUP) for this step, the same shapes RunLog.cs writes in Godot.
                var captureGroups = Aggro.GroupsOf(run.Dungeon, tick, run.Content.Packs, null, run.Content.Patrols);
                CaptureLines.AppendLines(log, CaptureLines.StepLines(tick, TankId, run.State, run.Encounter, captureGroups, stepped.Events.Count));
            }
            buffer?.FlushBatch();
            stats.ObserveStep(before, stepped.Events, PullIdOf(before));
        }
        foreach (var (id, pb) in policyBots)
        {
            stats.NoteHands(id, pb.LateKicks, pb.DodgeMoves);
        }
        return new PlaytestResult(run, buffer?.Finish() ?? new Lazy<string>(string.Empty), stats, setup, registry);
    }

    /// <summary>
    /// The playtester guides a guided plan plays from: game/content/data/guides/warden.json, read from the embedded content
    /// (Keyrun.Content embeds content/data/**/*.json, so the Playtest project reaches it through its ProjectReference). Fails
    /// closed with a plan-load message when the Warden guide is absent.
    /// </summary>
    /// <param name="registry">The content (the ability ids a guide press must name).</param>
    /// <returns>The loaded guides.</returns>
    public static GuideSet LoadGuides(C.ContentRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        const string path = "guides/warden.json";
        var source = C.EmbeddedContentSource.Instance;
        if (!source.Paths.Contains(path, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("guided plan: no playtester guide is loaded (game/content/data/guides/warden.json is not embedded in Keyrun.Content); use mode \"blind\" to play the scripted roles");
        }
        var text = Encoding.UTF8.GetString(source.Read(path));
        return GuideSet.Parse(text, registry.Abilities.Keys.ToHashSet(StringComparer.Ordinal));
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
