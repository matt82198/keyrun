using System;
using System.Collections.Generic;
using System.Globalization;
using Keyrun.Sim.Core;

namespace Keyrun.Host;

/// <summary>A scripted seat abandon (<c>--abandon-seat</c>, lane 3J): actor <paramref name="ActorId"/> leaves at a run
/// tick. The fixed form <c>A@T</c> (<paramref name="AnchorPull"/> null) leaves at run tick <paramref name="Tick"/>. The
/// pull-relative form <c>A@pullN+O</c> (RULING R-415-D: a leaver mid-pull is a scenario, not a number) leaves
/// <paramref name="Tick"/> ticks after pull <paramref name="AnchorPull"/>'s <c>pull_start</c>, so a scenario such as
/// HudSceneTest run F ("a seat abandons during the first pull") keeps its meaning when the party's pacing changes.</summary>
/// <param name="ActorId">The seat's player actor (1 the human, 2 and 3 the bot seats).</param>
/// <param name="Tick">The fixed form's run tick (pre-step), or the pull-relative form's offset from the pull start.</param>
/// <param name="AnchorPull">The pull index (the log's <c>PULL spawn index=N</c>) the offset counts from; null for the
/// fixed form.</param>
public sealed record SeatAbandonPlan(int ActorId, int Tick, int? AnchorPull = null)
{
    private const string PullPrefix = "pull";

    /// <summary>Parses <c>A@T</c> or <c>A@pullN+O</c> (A a positive actor id; T, N and O whole numbers of 0 or more,
    /// digits only).</summary>
    /// <param name="raw">The flag value.</param>
    /// <returns>The plan.</returns>
    /// <exception cref="ArgumentException">Neither form.</exception>
    public static SeatAbandonPlan Parse(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        var parts = raw.Split('@');
        if (parts.Length == 2 && Whole(parts[0]) is { } actor && actor > 0)
        {
            if (Whole(parts[1]) is { } tick)
            {
                return new SeatAbandonPlan(actor, tick);
            }
            if (parts[1].StartsWith(PullPrefix, StringComparison.Ordinal))
            {
                var rel = parts[1][PullPrefix.Length..].Split('+');
                if (rel.Length == 2 && Whole(rel[0]) is { } pull && Whole(rel[1]) is { } offset)
                {
                    return new SeatAbandonPlan(actor, offset, pull);
                }
            }
        }
        throw new ArgumentException($"--abandon-seat needs actorId@tick or actorId@pullN+offset, got '{raw}'");
    }

    /// <summary>The run tick (pre-step) at which the seat empties: the fixed form's <see cref="Tick"/>, or the anchor
    /// pull's start plus the offset. Null while a pull-relative plan's pull has not started.</summary>
    /// <param name="anchorStartTick">The anchor pull's <c>pull_start</c> tick once seen (<see cref="AnchorFrom"/>), else
    /// null. Ignored by the fixed form.</param>
    /// <returns>The abandon tick, or null when it is not known yet.</returns>
    public int? ResolveTick(int? anchorStartTick) =>
        AnchorPull is null ? Tick : anchorStartTick is { } start ? start + Tick : null;

    /// <summary>The tick of the anchor pull's <c>pull_start</c> among <paramref name="events"/> (the first one whose
    /// <c>pullIndex</c> is <see cref="AnchorPull"/>), or null when none is there or the plan is the fixed form.</summary>
    /// <param name="events">One step's events.</param>
    /// <returns>The pull start tick, or null.</returns>
    public int? AnchorFrom(IEnumerable<SimEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (AnchorPull is not { } pull)
        {
            return null;
        }
        foreach (var e in events)
        {
            if (e.Type == "pull_start" && e.Get("pullIndex") is JsInt idx && idx.Value == pull)
            {
                return e.Tick;
            }
        }
        return null;
    }

    private static int? Whole(string s) =>
        int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : null;
}
