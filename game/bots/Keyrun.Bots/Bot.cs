using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Shared;
using Keyrun.Sim.Core;
using Keyrun.Sim.Nav;

namespace Keyrun.Bots;

/// <summary>bots/src/bot.ts BotMemory: the skill dial and the next tick the bot may act.</summary>
/// <param name="SkillDial">0..100.</param>
/// <param name="NextActionTick">Role dispatch waits until this tick.</param>
/// <param name="FillerHistory">
/// Filler abilities this seat cast, least recently cast first (null = none yet). <see cref="Roles.Fillers.ArrangeFillers"/>
/// reads it so the least recently cast ready filler is chosen, not the same one every global (finding f-c356528f).
/// </param>
public sealed record BotMemory(int SkillDial, int NextActionTick, IReadOnlyList<string>? FillerHistory = null)
{
    /// <summary>bot.ts defaultMemory: <c>{skillDial, nextActionTick: 0}</c>.</summary>
    /// <param name="skillDial">Skill dial (default 100).</param>
    /// <returns>Fresh memory.</returns>
    public static BotMemory Default(int skillDial = 100) => new(skillDial, 0);
}

/// <summary>One bot decision: commands, the next memory and the advanced PRNG.</summary>
/// <param name="Commands">Commands for this tick.</param>
/// <param name="Memory">Next memory.</param>
/// <param name="Prng">Next PRNG state.</param>
public sealed record BotDecision(IReadOnlyList<Command> Commands, BotMemory Memory, BotPrngState Prng);

/// <summary>A runBots result: every bot's commands in actorId order, the memories, and the threaded PRNG.</summary>
/// <param name="Commands">All commands.</param>
/// <param name="Memories">Memories by actorId (ascending).</param>
/// <param name="Prng">PRNG after the last bot.</param>
public sealed record BotBatch(IReadOnlyList<Command> Commands, SortedDictionary<int, BotMemory> Memories, BotPrngState Prng)
{
    /// <summary>One <see cref="Reason"/> per command, index-aligned with <see cref="Commands"/> (G2.4). Receipt data only: it never feeds a decision or the fingerprint.</summary>
    public IReadOnlyList<Reason> Reasons { get; init; } = Array.Empty<Reason>();
}

/// <summary>bots/src/bot.ts Bot: a pure function <c>(view, memory, actorId, prng, pings) =&gt; {commands, memory, prng}</c>.</summary>
public interface IBot
{
    /// <summary>bot.ts Bot.decide.</summary>
    /// <param name="view">This tick's view.</param>
    /// <param name="memory">This bot's memory.</param>
    /// <param name="actorId">The actor it drives.</param>
    /// <param name="prng">Bot PRNG state.</param>
    /// <param name="pings">Live party pings (null = none).</param>
    /// <returns>The decision.</returns>
    BotDecision Decide(BotView view, BotMemory memory, int actorId, BotPrngState prng, IReadOnlyList<PingEvent>? pings = null);
}

/// <summary>
/// Port of bots/src/bot.ts: the bot runner. Each tick, highest priority first: (1) danger-telegraph avoidance,
/// skill-dial-free; (2) party-ping reactions, also skill-dial-free (a ping cast skips role dispatch so a bot
/// never casts twice in a tick); (3) role dispatch through <see cref="RoleSet"/>, gated by the reaction delay
/// and the mistake roll.
/// </summary>
public static class Bot
{
    private sealed class RunnerBot : IBot
    {
        private readonly RoleSet _roles;

        public RunnerBot(RoleSet roles) => _roles = roles;

        public BotDecision Decide(BotView view, BotMemory memory, int actorId, BotPrngState prng, IReadOnlyList<PingEvent>? pings = null) =>
            DecideForActor(_roles, view, memory, actorId, prng, pings ?? Array.Empty<PingEvent>());
    }

    /// <summary>
    /// bot.ts dangerAvoidCommand: a move to the nearest safe point when inside an active danger telegraph. On a layout run
    /// (LINE-DODGE, finding f-e17e190c) the safe point must be walkable and the move walks the sim's path to it: the sim
    /// clamps every actor into its open boxes after each tick, so a straight move into a wall leaves the seat inside the
    /// telegraph. Off a layout run (every oracle run) the straight move is unchanged.
    /// </summary>
    public static MoveCommand? DangerAvoidCommand(BotView view, ActorState self)
    {
        var tick = view.State.Tick;
        var selfMm = PointMm.Of(self.Pos);
        // DEAD-CASTER-TELEGRAPH: a telegraph whose caster died before its cast began warns of nothing (Positioning.IsSpent).
        var active = view.Telegraphs.Where(t => t.Danger && t.ResolvesTick > tick && !Positioning.IsSpent(t, view.State)).ToList();
        if (!Positioning.IsInAnyDanger(selfMm, active))
        {
            return null;
        }
        if (view.Layout is { } layout)
        {
            var grid = NavGrid.Of(layout);
            var walkable = Positioning.NearestSafePoint(
                selfMm,
                active,
                extraCheck: p => grid.Walkable(new Vec3(p.X, self.Pos.Y, p.Z)));
            return walkable is null ? null : PathMoves.Along(tick, self, layout, new Vec3(walkable.Value.X, self.Pos.Y, walkable.Value.Z));
        }
        var safe = Positioning.NearestSafePoint(selfMm, active);
        return safe is null ? null : Moves.MoveToward(tick, self.Id, selfMm, safe.Value);
    }

    private static BotDecision DecideForActor(RoleSet roles, BotView view, BotMemory memory, int actorId, BotPrngState prng, IReadOnlyList<PingEvent> pings)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(memory);
        if (!view.State.Actors.TryGetValue(actorId, out var self) || !self.Alive)
        {
            return new BotDecision(Array.Empty<Command>(), memory, prng);
        }
        var tick = view.State.Tick;

        var commands = new List<Command>();
        var avoid = DangerAvoidCommand(view, self);
        if (avoid is not null)
        {
            commands.Add(Reasons.Tag(avoid, "bot:danger-dodge"));
        }

        var pingCommands = Reasons.TagAll(PingsPolicy.PingReactionCommands(view, self, pings), "bot:ping-reaction");
        commands.AddRange(pingCommands);
        var pingCast = pingCommands.Any(c => c is CastCommand);

        // Travel between rooms (STATE.md D18; no TS counterpart, Follow.FollowCommand): on a layout run the bot walks the
        // route to its slot behind the leader while no enemy lives, every tick and skill-dial-free like the danger dodge
        // (the reaction delay would leave it a third of the leader's speed), and skips its role. In a fight it started
        // outside the fight's room, it walks in the same way while its role still decides (its casts are kept, its own
        // moves dropped). A dodge or a ping reaction wins over both. Inert in the oracle runner: its view carries no layout.
        // KF2 (BOTS-ENGAGE-CHOIR): a bot in the leader seat walks the route between fights, so a party with no human
        // leading reaches the next pull instead of idling at the Entrance. Skill-dial-free like the follow walk.
        // SEED-BLOOM-TEACHES (BIBLE s9.6 item 3): while an encounter floor Window (the Seed's Death Bloom) is open, a bot
        // that can land a Payoff in it walks into the ring and lands it, ahead of travel and its role; skill-dial-free like
        // travel. Inert in the oracle runner (no layout), like the travel it outranks. See EncounterWindow.
        if (avoid is null && !pingCommands.Any() && EncounterWindow.Decide(view, self) is { } bloom)
        {
            commands.AddRange(Reasons.TagAll(bloom, "bot:seed-bloom"));
            return new BotDecision(commands, memory, prng);
        }

        // KICK-LEDGER-COOLDOWN-GAP (finding f-13b5c33a, the WoW kick rotation: a melee kicker walks in to reach the caster):
        // when the party ledger names this seat to kick a cast its interrupt cannot reach yet, it walks at the caster. Every
        // tick and skill-dial-free like the walks above (the reaction delay would leave it a third of its speed, and the
        // ledger's landing estimate is at full speed). Also in the oracle runner (no layout): a straight walk, KICK-APPROACH-NO-LAYOUT.
        if (avoid is null && !pingCommands.Any() && Roles.RoleShared.LedgerKickApproachCommand(view, self, Roles.RoleShared.KitOf(view, self.ClassId)) is { } kickWalk)
        {
            commands.Add(Reasons.Tag(kickWalk, "bot:kick-approach"));
            return new BotDecision(commands, memory, prng);
        }

        var lead = avoid is null && !pingCommands.Any() ? Follow.LeadCommand(view, self) : null;
        if (lead is not null)
        {
            commands.Add(Reasons.Tag(lead, "bot:lead-route"));
            return new BotDecision(commands, memory, prng);
        }

        var follow = avoid is null && !pingCommands.Any() ? Follow.FollowCommand(view, self) : null;
        // R-AOE-1 (c), f-c8ed3a62: the bot tank in the lead seat chain-pulls the next pack while the current one is nearly dead
        // (ChainPull). Skill-dial-free like every walk above; its casts stay and its role's own moves are dropped, as when
        // travelling. The followers go with him through FollowCommand.
        var chain = avoid is null && !pingCommands.Any() && view.Layout is { } chainLayout && view.PullPlan is { } chainPlan && Follow.LeaderOf(view)?.Id == self.Id
            ? ChainPull.Move(tick, view.State, chainLayout, chainPlan, self)
            : null;
        if (chain is not null)
        {
            commands.Add(Reasons.Tag(chain, "bot:chain-pull"));
        }

        var travelling = follow is not null || chain is not null;
        // HEALER-TOPS-OFF (the WoW Mythic+ dungeon loop): between pulls a travelling healer still tops off a hurt ally
        // (its casts kept, its own moves dropped, through the same reaction delay and skill roll as a fight decision),
        // so the party never walks into the next pack hurt. Every other bot, and a healer with nobody hurt, only travels.
        var topOff = travelling && self.Role == ActorRole.Healer && !Follow.AnyLivingEnemy(view.State) && Roles.HealerRole.NeedsTopOff(view.State.Actors);
        if (follow is not null)
        {
            commands.Add(Reasons.Tag(follow, "bot:follow-route"));
            if (!Follow.AnyLivingEnemy(view.State) && !topOff)
            {
                return new BotDecision(commands, memory, prng);
            }
        }

        // CHASE-SPEED (f-2f6982e9, BIBLE section 3): the tank's out-of-reach approach is skill-dial-free, like travel. A
        // move holds for the one tick it is issued, so gating it behind the reaction delay and the mistake roll left the
        // Warden walking one tick in three to six (about 40 mm/tick of the 180 walk) toward an engaged pack while the
        // human out-ran him into aggro. A tick on which the role does not decide (delay or mistake) still carries the
        // approach; a deciding tick gets it from TankEngage itself, so it is never issued twice.
        var approach = avoid is null && !pingCommands.Any() && !travelling && self.Role == ActorRole.Tank
            ? Roles.TankEngage.ApproachMove(view, self, Roles.RoleShared.KitOf(view, self.ClassId))
            : null;

        if (tick < memory.NextActionTick)
        {
            if (approach is not null)
            {
                commands.Add(Reasons.Tag(approach, "bot:tank-approach"));
            }
            return new BotDecision(commands, memory, prng);
        }

        var roll = Skill.RollMistake(memory.SkillDial, prng);
        var nextMemory = memory with { NextActionTick = tick + Skill.ReactionDelayTicks(memory.SkillDial) };
        if (roll.Mistake)
        {
            if (approach is not null)
            {
                commands.Add(Reasons.Tag(approach, "bot:tank-approach"));
            }
            return new BotDecision(commands, nextMemory, roll.Next);
        }

        if (!pingCast && roles.For(self.Role) is { } role)
        {
            var history = memory.FillerHistory ?? Array.Empty<string>();
            var decided = topOff ? Reasons.TagAll(Roles.HealerRole.TopOffCommands(view, self), "bot:healer-top-off") : role.Decide(view with { FillerHistory = history }, self);
            commands.AddRange(!travelling ? decided : decided.Where(c => c is not MoveCommand and not DashCommand));
            var recorded = Roles.Fillers.RecordCasts(view, self, history, decided);
            if (!ReferenceEquals(recorded, history))
            {
                nextMemory = nextMemory with { FillerHistory = recorded };
            }
        }

        return new BotDecision(commands, nextMemory, roll.Next);
    }

    /// <summary>bot.ts makeBot: a bot that dispatches roles through <paramref name="roles"/> (default: none).</summary>
    /// <param name="roles">Role registry; null means <see cref="RoleSet.None"/>.</param>
    /// <returns>The bot.</returns>
    public static IBot MakeBot(RoleSet? roles = null) => new RunnerBot(roles ?? RoleSet.None);

    /// <summary>
    /// bot.ts runBots: every bot decides in ascending actorId order, threading one PRNG state through them, all on
    /// the same view and pings. A bot with no memory starts from <see cref="BotMemory.Default"/>.
    /// </summary>
    /// <param name="view">This tick's view.</param>
    /// <param name="bots">Bots by actorId.</param>
    /// <param name="memories">Memories by actorId.</param>
    /// <param name="prng">Bot PRNG state.</param>
    /// <param name="pings">Live party pings (null = none).</param>
    /// <returns>The batch.</returns>
    public static BotBatch RunBots(
        BotView view,
        IReadOnlyDictionary<int, IBot> bots,
        IReadOnlyDictionary<int, BotMemory> memories,
        BotPrngState prng,
        IReadOnlyList<PingEvent>? pings = null)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(bots);
        ArgumentNullException.ThrowIfNull(memories);
        var live = pings ?? Array.Empty<PingEvent>();
        var commands = new List<Command>();
        var nextMemories = new SortedDictionary<int, BotMemory>();
        foreach (var kv in memories)
        {
            nextMemories[kv.Key] = kv.Value;
        }
        var nextPrng = prng;
        foreach (var id in bots.Keys.OrderBy(k => k))
        {
            var memory = memories.TryGetValue(id, out var m) ? m : BotMemory.Default();
            var result = bots[id].Decide(view, memory, id, nextPrng, live);
            // R-FACING-2 (b): a targeted cast is preceded by the seat's turn to its target (CastFacing).
            commands.AddRange(CastFacing.Apply(result.Commands, view.State));
            nextMemories[id] = result.Memory;
            nextPrng = result.Prng;
        }
        return new BotBatch(commands, nextMemories, nextPrng) { Reasons = Keyrun.Bots.Reasons.OfAll(commands) };
    }
}
