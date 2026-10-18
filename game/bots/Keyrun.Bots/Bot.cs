using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Shared;
using Keyrun.Sim.Core;

namespace Keyrun.Bots;

/// <summary>bots/src/bot.ts BotMemory: the skill dial and the next tick the bot may act.</summary>
/// <param name="SkillDial">0..100.</param>
/// <param name="NextActionTick">Role dispatch waits until this tick.</param>
public sealed record BotMemory(int SkillDial, int NextActionTick)
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
public sealed record BotBatch(IReadOnlyList<Command> Commands, SortedDictionary<int, BotMemory> Memories, BotPrngState Prng);

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

    /// <summary>bot.ts dangerAvoidCommand: a move to the nearest safe point when inside an active danger telegraph.</summary>
    private static MoveCommand? DangerAvoidCommand(BotView view, ActorState self)
    {
        var tick = view.State.Tick;
        var selfMm = PointMm.Of(self.Pos);
        var active = view.Telegraphs.Where(t => t.Danger && t.ResolvesTick > tick).ToList();
        if (!Positioning.IsInAnyDanger(selfMm, active))
        {
            return null;
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
            commands.Add(avoid);
        }

        var pingCommands = PingsPolicy.PingReactionCommands(view, self, pings);
        commands.AddRange(pingCommands);
        var pingCast = pingCommands.Any(c => c is CastCommand);

        if (tick < memory.NextActionTick)
        {
            return new BotDecision(commands, memory, prng);
        }

        var roll = Skill.RollMistake(memory.SkillDial, prng);
        var nextMemory = memory with { NextActionTick = tick + Skill.ReactionDelayTicks(memory.SkillDial) };
        if (roll.Mistake)
        {
            return new BotDecision(commands, nextMemory, roll.Next);
        }

        if (!pingCast && roles.For(self.Role) is { } role)
        {
            commands.AddRange(role.Decide(view, self));
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
            commands.AddRange(result.Commands);
            nextMemories[id] = result.Memory;
            nextPrng = result.Prng;
        }
        return new BotBatch(commands, nextMemories, nextPrng);
    }
}
