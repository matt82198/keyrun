using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Encounter;

/// <summary>How a pack the party left behind was left.</summary>
public enum SkipOutcome
{
    /// <summary>The pack was never engaged: the party walked past its post (design/DUNGEON-SYNERGY.md section 2.6).</summary>
    Skipped,

    /// <summary>The pack engaged and then dropped out of the engaged set (a leash reset): a leash, not a skip.</summary>
    Leashed,
}

/// <summary>One pack the party left behind.</summary>
/// <param name="PackId">The group id.</param>
/// <param name="Tick">The tick the boundary was crossed.</param>
/// <param name="PartyPos">The living seated players' centroid on that tick (integer mm).</param>
/// <param name="Outcome">Skipped or leashed.</param>
/// <param name="ForcesForgone">Forces the pack's living members are worth; they are not banked.</param>
public sealed record SkipEntry(string PackId, int Tick, Vec3 PartyPos, SkipOutcome Outcome, int ForcesForgone);

/// <summary>The skip record carried by <see cref="EncounterState.Skips"/>.</summary>
/// <param name="EverEngaged">Every group that has engaged at least once, in first-engage order.</param>
/// <param name="Entries">Recorded packs, in record order (tick, then group order).</param>
public sealed record SkipLedger(IReadOnlyList<string> EverEngaged, IReadOnlyList<SkipEntry> Entries)
{
    /// <summary>Nothing engaged, nothing recorded.</summary>
    public static readonly SkipLedger Empty = new(Array.Empty<string>(), Array.Empty<SkipEntry>());
}

/// <summary>
/// G2.5 lane L4 (R-SYNERGY-1 / R-SYNERGY-2; design/DUNGEON-SYNERGY.md section 2.6): the deterministic skip record, so a
/// pack the party routes around is a log fact and never a bug. On the first tick every living seated player stands in a
/// layout box deeper in the route than the box holding a pack's post, and the pack still has a living member and is not
/// engaged, the pack is recorded once: <see cref="SkipOutcome.Skipped"/> if it never engaged (and
/// <c>{tick, type:"pack_skipped", packId, forcesLeft, x, z}</c> fires), <see cref="SkipOutcome.Leashed"/> if it engaged
/// earlier and is now out of the engaged set (no event: a leash is not a skip). Skipping is free: nothing here touches
/// the penalty clock or the forces bar (<see cref="Forces.Points"/> still sums dead enemies only, so the skipped pack
/// stays un-banked and <c>forcesLeft</c> says what the bar still needs). Integers only, no randomness.
/// SEAM (lane L1, leash rules): "leashed" keys off the engaged set shrinking, not off L1's <c>pack_evade</c> event.
/// </summary>
public static class Skips
{
    /// <summary>The skip rules after the aggro check, on the freshly stepped state.</summary>
    /// <param name="next">The post-combat state (read only).</param>
    /// <param name="encounter">The encounter, with this tick's engages already applied.</param>
    /// <param name="groups">The run's aggro groups.</param>
    /// <param name="layout">The run's layout; its box order is the route order.</param>
    /// <param name="goal">The dungeon's forces goal, or null.</param>
    /// <param name="openSeats">Abandoned or unfilled seats' actor ids, or null.</param>
    /// <param name="emit">Event sink.</param>
    /// <returns>The encounter with the ledger updated.</returns>
    public static EncounterState Step(SimState next, EncounterState encounter, IReadOnlyList<AggroGroup> groups, NavLayout layout, ForcesGoal? goal, IReadOnlyList<int>? openSeats, Action<SimEvent> emit)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(encounter);
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(emit);
        var ledger = encounter.Skips;
        var ever = ledger.EverEngaged;
        foreach (var id in encounter.Engaged)
        {
            if (!ever.Contains(id, StringComparer.Ordinal))
            {
                ever = ever.Append(id).ToList();
            }
        }

        var entries = ledger.Entries;
        var partyBox = int.MaxValue;
        long sumX = 0, sumY = 0, sumZ = 0;
        var seats = 0;
        foreach (var a in next.Actors.Values)
        {
            if (!a.Alive || a.Kind != ActorKind.Player || (openSeats is not null && openSeats.Contains(a.Id)))
            {
                continue;
            }
            partyBox = Math.Min(partyBox, layout.BoxIndexAt(a.Pos));
            sumX += a.Pos.X;
            sumY += a.Pos.Y;
            sumZ += a.Pos.Z;
            seats++;
        }

        if (seats > 0)
        {
            var at = new Vec3(sumX / seats, sumY / seats, sumZ / seats);
            foreach (var g in groups)
            {
                if (entries.Any(e => string.Equals(e.PackId, g.Id, StringComparison.Ordinal)) || Pulls.IsEngaged(encounter, g.Id))
                {
                    continue;
                }
                var living = g.ActorIds.Select(id => next.Actors.TryGetValue(id, out var m) ? m : null).Where(m => m is { Alive: true }).Select(m => m!).ToList();
                if (living.Count == 0 || layout.BoxIndexAt(g.Center) is var packBox && (packBox < 0 || partyBox <= packBox))
                {
                    continue;
                }
                var outcome = ever.Contains(g.Id, StringComparer.Ordinal) ? SkipOutcome.Leashed : SkipOutcome.Skipped;
                var forgone = goal is null ? 0 : (int)Math.Min(int.MaxValue, living.Sum(m => (long)Math.Max(0, goal.PointsByEnemy.GetValueOrDefault(m.ClassId))));
                entries = entries.Append(new SkipEntry(g.Id, next.Tick, at, outcome, forgone)).ToList();
                if (outcome == SkipOutcome.Skipped)
                {
                    var left = goal is null ? 0 : Math.Max(0, goal.Required - Forces.Points(next, goal.PointsByEnemy));
                    emit(new SimEvent(next.Tick, "pack_skipped",
                        ("packId", JsValue.Str(g.Id)), ("forcesLeft", JsValue.Int(left)), ("x", JsValue.Int(at.X)), ("z", JsValue.Int(at.Z))));
                }
            }
        }

        return ReferenceEquals(ever, ledger.EverEngaged) && ReferenceEquals(entries, ledger.Entries)
            ? encounter
            : encounter with { Skips = new SkipLedger(ever, entries) };
    }

    /// <summary>The run-log line for a <c>pack_skipped</c> event: <c>SKIP pack=id tick=t at=x,z forcesLeft=n</c>.</summary>
    /// <param name="e">Any sim event.</param>
    /// <returns>The line, or null for any other event.</returns>
    public static string? LogLine(SimEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (e.Type != "pack_skipped" || e.Get("packId") is not JsString pack || e.Get("forcesLeft") is not JsInt left
            || e.Get("x") is not JsInt x || e.Get("z") is not JsInt z)
        {
            return null;
        }
        return FormattableString.Invariant($"SKIP pack={pack.Value} tick={e.Tick} at={x.Value},{z.Value} forcesLeft={left.Value}");
    }
}
