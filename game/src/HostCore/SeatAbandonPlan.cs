using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;

namespace Keyrun.Host;

/// <summary>A scripted seat abandon (<c>--abandon-seat</c>, lane 3J): actor <paramref name="ActorId"/> leaves at a run
/// tick. The fixed form <c>A@T</c> (<paramref name="AnchorPull"/> null) leaves at run tick <paramref name="Tick"/>. The
/// pull-relative form <c>A@pullN+O</c> (RULING R-415-D: a leaver mid-pull is a scenario, not a number) leaves
/// <paramref name="Tick"/> ticks after pull <paramref name="AnchorPull"/>'s <c>pull_start</c>. The health-relative form
/// <c>A@pullN.hpP+O</c> (finding f-0fd3e6fb) leaves O ticks after the first step that ends with pull N's enemies, still
/// fighting, down to P percent of their total health: the leave lands late in pull N however long its fight runs, so
/// HudSceneTest run F ("a seat abandons during the first pull" and the party still reaches the next pull) no longer flips
/// when a pacing change moves the Choir's clear past a fixed offset from its engage.</summary>
/// <param name="ActorId">The seat's player actor (1 the human, 2 and 3 the bot seats).</param>
/// <param name="Tick">The fixed form's run tick (pre-step), or the anchored forms' offset from the anchor.</param>
/// <param name="AnchorPull">The pull index (the log's <c>PULL spawn index=N</c>) the offset counts from; null for the
/// fixed form.</param>
/// <param name="HealthPercent">The health-relative form's P (1 to 99); null for the fixed and pull_start forms.</param>
public sealed record SeatAbandonPlan(int ActorId, int Tick, int? AnchorPull = null, int? HealthPercent = null)
{
    private const string PullPrefix = "pull";
    private const string HealthInfix = ".hp";

    /// <summary>The <c>SEAT abandon</c> line's anchor fields after <c>pull=N</c>: <c>start=S</c> for the pull_start
    /// form, <c>hp=P at=S</c> for the health-relative form (S the anchor tick; the abandon tick is S + O either way).</summary>
    /// <param name="anchorTick">The anchor tick.</param>
    /// <returns>The fields.</returns>
    public string AnchorFields(int? anchorTick) =>
        HealthPercent is { } p
            ? FormattableString.Invariant($"hp={p} at={anchorTick}")
            : FormattableString.Invariant($"start={anchorTick}");

    /// <summary>Parses <c>A@T</c>, <c>A@pullN+O</c> or <c>A@pullN.hpP+O</c> (A a positive actor id; T, N and O whole
    /// numbers of 0 or more, digits only; the health form's P is 1 to 99 and its O at least 1).</summary>
    /// <param name="raw">The flag value.</param>
    /// <returns>The plan.</returns>
    /// <exception cref="ArgumentException">None of the forms.</exception>
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
                if (rel.Length == 2 && Whole(rel[1]) is { } offset)
                {
                    if (Whole(rel[0]) is { } pull)
                    {
                        return new SeatAbandonPlan(actor, offset, pull);
                    }
                    // The health anchor is the pre-step tick of a step already run, so offset 0 names a tick that has
                    // passed and the seat would never leave; P 0 or 100 is never "still fighting, down to P".
                    var hp = rel[0].Split(HealthInfix);
                    if (hp.Length == 2 && Whole(hp[0]) is { } hpPull && Whole(hp[1]) is { } percent &&
                        percent is > 0 and < 100 && offset > 0)
                    {
                        return new SeatAbandonPlan(actor, offset, hpPull, percent);
                    }
                }
            }
        }
        throw new ArgumentException($"--abandon-seat needs actorId@tick, actorId@pullN+offset or actorId@pullN.hpP+offset (P 1-99, offset 1 or more), got '{raw}'");
    }

    /// <summary>The run tick (pre-step) at which the seat empties: the fixed form's <see cref="Tick"/>, or the anchor
    /// tick plus the offset. Null while an anchored plan's anchor has not been seen.</summary>
    /// <param name="anchorTick">The anchor tick once seen (<see cref="AnchorAt"/>), else null. Ignored by the fixed
    /// form.</param>
    /// <returns>The abandon tick, or null when it is not known yet.</returns>
    public int? ResolveTick(int? anchorTick) =>
        AnchorPull is null ? Tick : anchorTick is { } start ? start + Tick : null;

    /// <summary>The tick of the pull_start form's anchor among one step's <paramref name="events"/>: the first
    /// <c>pull_start</c> whose <c>pullIndex</c> is <see cref="AnchorPull"/>, or null when none is there or the plan is
    /// not the pull_start form.</summary>
    /// <param name="events">One step's events.</param>
    /// <returns>The pull start tick, or null.</returns>
    public int? AnchorFrom(IEnumerable<SimEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        if (AnchorPull is not { } pull || HealthPercent is not null)
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

    /// <summary>The plan's anchor for one step, as SimDriver.StepOnce asks it after the step: the pull_start form's
    /// (<see cref="AnchorFrom"/>), or, for the health form, <paramref name="stepTick"/> when the step ends with pull
    /// <see cref="AnchorPull"/>'s spawns in <paramref name="dungeon"/> still standing (one alive) and their health in
    /// <paramref name="actors"/> at or below <see cref="HealthPercent"/> of their max (an unspawned one counts at full).
    /// Null otherwise and for the fixed form.</summary>
    /// <param name="stepTick">The step's pre-step tick (the tick its events carry).</param>
    /// <param name="events">The step's events.</param>
    /// <param name="dungeon">The run's dungeon (<c>Run.Dungeon</c>), which names the pull's spawns.</param>
    /// <param name="actors">The run's actors after the step (<c>Run.State.Actors</c>).</param>
    /// <returns>The anchor tick, or null.</returns>
    public int? AnchorAt(int stepTick, IEnumerable<SimEvent> events, DungeonDef dungeon, IReadOnlyDictionary<int, ActorState> actors)
    {
        ArgumentNullException.ThrowIfNull(dungeon);
        ArgumentNullException.ThrowIfNull(actors);
        if (HealthPercent is not { } percent)
        {
            return AnchorFrom(events);
        }
        if (AnchorPull is not { } pull || pull >= dungeon.Pulls.Count)
        {
            return null;
        }
        long left = 0;
        long total = 0;
        var standing = false;
        foreach (var spawn in dungeon.Pulls[pull].Spawns)
        {
            if (actors.TryGetValue(spawn.ActorId, out var a))
            {
                total += a.MaxHealth;
                left += a.Alive ? Math.Max(0, a.Health) : 0;
                standing |= a.Alive;
            }
            else
            {
                total += spawn.Health;
                left += spawn.Health;
                standing = true;
            }
        }
        return standing && total > 0 && left * 100 <= total * percent ? stepTick : null;
    }

    private static int? Whole(string s) =>
        int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : null;
}
