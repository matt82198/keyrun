using System;
using System.Collections.Generic;

namespace Keyrun.Bots.Shared;

/// <summary>bots/src/shared/pings.ts PingKind: "target" | "spot".</summary>
public enum PingKind
{
    /// <summary>"target": a ping on an actor.</summary>
    Target,

    /// <summary>"spot": a ping on a floor point.</summary>
    Spot,
}

/// <summary>
/// bots/src/shared/pings.ts PingEvent: a party G-ping. Not a sim command; a bot-side channel carried alongside
/// snapshots. <see cref="TargetId"/> and <see cref="Point"/> are the optional TS fields.
/// </summary>
/// <param name="Tick">Tick the ping was made.</param>
/// <param name="ActorId">Pinging actor.</param>
/// <param name="Kind">Ping kind.</param>
public sealed record PingEvent(int Tick, int ActorId, PingKind Kind)
{
    /// <summary>Pinged actor (target pings).</summary>
    public int? TargetId { get; init; }

    /// <summary>Pinged point (spot pings), mm.</summary>
    public PointMm? Point { get; init; }
}

/// <summary>Port of bots/src/shared/pings.ts: ping liveness and selection.</summary>
public static class Pings
{
    /// <summary>pings.ts PING_RATE_LIMIT_TICKS: one ping per 20 ticks per actor.</summary>
    public const int PingRateLimitTicks = 20;

    /// <summary>pings.ts PING_REACTION_WINDOW_TICKS: a ping stays live for 60 ticks (3 s at 20 Hz).</summary>
    public const int PingReactionWindowTicks = 60;

    /// <summary>pings.ts WINDOW_EFFECT_PREFIX: a Window-carrying effect id starts with "window:".</summary>
    public const string WindowEffectPrefix = "window:";

    /// <summary>pings.ts isPingLive: <c>tick &gt;= event.tick &amp;&amp; tick - event.tick &lt;= windowTicks</c>.</summary>
    /// <param name="ping">Ping.</param>
    /// <param name="tick">Current tick.</param>
    /// <param name="windowTicks">Reaction window.</param>
    /// <returns>True when live.</returns>
    public static bool IsPingLive(PingEvent ping, int tick, int windowTicks = PingReactionWindowTicks)
    {
        ArgumentNullException.ThrowIfNull(ping);
        return tick >= ping.Tick && tick - ping.Tick <= windowTicks;
    }

    /// <summary>
    /// pings.ts latestLivePing: the most recent live ping (optionally of <paramref name="kind"/>), latest tick
    /// first, ties broken by lowest actorId. Null if none qualify.
    /// </summary>
    /// <param name="pings">Pings.</param>
    /// <param name="tick">Current tick.</param>
    /// <param name="windowTicks">Reaction window.</param>
    /// <param name="kind">Kind filter, or null for any.</param>
    /// <returns>The ping, or null.</returns>
    public static PingEvent? LatestLivePing(IEnumerable<PingEvent> pings, int tick, int windowTicks = PingReactionWindowTicks, PingKind? kind = null)
    {
        ArgumentNullException.ThrowIfNull(pings);
        PingEvent? best = null;
        foreach (var p in pings)
        {
            if (kind is not null && p.Kind != kind)
            {
                continue;
            }
            if (!IsPingLive(p, tick, windowTicks))
            {
                continue;
            }
            if (best is null || p.Tick > best.Tick || (p.Tick == best.Tick && p.ActorId < best.ActorId))
            {
                best = p;
            }
        }
        return best;
    }
}
