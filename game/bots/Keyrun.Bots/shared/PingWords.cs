using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;

namespace Keyrun.Bots.Shared;

/// <summary>
/// PING-FEEDBACK (design/BIBLE.md s10, "G marks a target or a spot; the game turns it into the party's language
/// ("kick", "stack here", "Window!")"): the word a G-ping shows the human, chosen from the same context
/// <see cref="PingsPolicy"/>'s bots react to, in the same priority (interrupt before Window payoff). Deterministic
/// and read-only: it reads the actor table and never writes the run or adds a sim rule.
/// </summary>
public static class PingWords
{
    /// <summary>A target ping on a casting enemy: the bots' interrupt reaction.</summary>
    public const string Kick = "kick";

    /// <summary>A target ping on a live Window carrier: the bots' payoff reaction.</summary>
    public const string Window = "Window!";

    /// <summary>A ping while a stack call is live (mark or pending telegraph), or on a stack-marked actor.</summary>
    public const string StackHere = "stack here";

    /// <summary>The plain target marker.</summary>
    public const string Focus = "focus";

    /// <summary>The plain spot marker.</summary>
    public const string Here = "here";

    /// <summary>The stack mark effect (content effects/enemy.json; game/src/Hud/Pings.cs PingCalls.StackEffectId).</summary>
    public const string StackEffectId = "stackMarked";

    /// <summary>How long the word stays up: 2 s, <see cref="PingsPolicy.PingMoveTicks"/> (the bots' spot reaction).</summary>
    public const int ShowTicks = PingsPolicy.PingMoveTicks;

    /// <summary>The word for <paramref name="ping"/> made at <paramref name="tick"/>.</summary>
    /// <param name="actors">The sim actor table.</param>
    /// <param name="tick">The current tick.</param>
    /// <param name="ping">The ping.</param>
    /// <param name="stackPending">True while a STACK telegraph is pending (the warning before the mark lands).</param>
    /// <returns>One of <see cref="Kick"/>, <see cref="Window"/>, <see cref="StackHere"/>, <see cref="Focus"/>, <see cref="Here"/>.</returns>
    public static string Say(IReadOnlyDictionary<int, ActorState> actors, int tick, PingEvent ping, bool stackPending = false)
    {
        ArgumentNullException.ThrowIfNull(actors);
        ArgumentNullException.ThrowIfNull(ping);
        if (ping.Kind == PingKind.Target)
        {
            if (ping.TargetId is { } id && actors.TryGetValue(id, out var target) && target.Alive)
            {
                if (target.Kind == ActorKind.Enemy && target.CastId is not null)
                {
                    return Kick;
                }
                if (Live(target, tick).Any(e => e.EffectId.StartsWith(Pings.WindowEffectPrefix, StringComparison.Ordinal)))
                {
                    return Window;
                }
                if (Live(target, tick).Any(e => e.EffectId == StackEffectId))
                {
                    return StackHere;
                }
            }
            return Focus;
        }
        var stackLive = stackPending || actors.Values.Any(a => a.Alive && Live(a, tick).Any(e => e.EffectId == StackEffectId));
        return stackLive ? StackHere : Here;
    }

    private static IEnumerable<EffectInstance> Live(ActorState actor, int tick) => actor.Effects.Where(e => e.EndsTick > tick);
}
