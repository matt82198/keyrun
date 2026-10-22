using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;

namespace Keyrun.Bots.Shared;

/// <summary>bots/src/shared/ledger.ts CastingTarget: an enemy mid-cast and the ability it is casting.</summary>
/// <param name="ActorId">Casting enemy.</param>
/// <param name="AbilityId">Ability being cast.</param>
public sealed record CastingTarget(int ActorId, string AbilityId);

/// <summary>bots/src/shared/ledger.ts InterruptAssignment: which enemy gets kicked, and by whom.</summary>
/// <param name="TargetId">Enemy to kick.</param>
/// <param name="KickerId">Bot that kicks.</param>
public sealed record InterruptAssignment(int TargetId, int KickerId);

/// <summary>
/// Port of bots/src/shared/ledger.ts: the kick ledger. Which casting enemy is kicked (ranked by a priority-cast
/// list, ties to the lowest id) and who kicks it (the lowest-id eligible player whose interrupt is off cooldown
/// and off the GCD).
/// </summary>
public static class Ledger
{
    /// <summary>ledger.ts castingEnemies: every alive enemy with a cast in progress, ascending by id.</summary>
    /// <param name="actors">Actor table.</param>
    /// <returns>Casting targets.</returns>
    public static List<CastingTarget> CastingEnemies(SortedDictionary<int, ActorState> actors)
    {
        ArgumentNullException.ThrowIfNull(actors);
        return actors.Values
            .Where(a => a.Kind == ActorKind.Enemy && a.Alive && a.CastId is not null)
            .Select(a => new CastingTarget(a.Id, a.CastId!))
            .ToList();
    }

    /// <summary>
    /// ledger.ts selectPriorityCastTarget: the cast ranked highest in <paramref name="priorityAbilityIds"/> (index 0
    /// first; unlisted casts rank below every listed one), ties to the lowest actorId. Null if nothing casts.
    /// </summary>
    /// <param name="casting">Casting enemies.</param>
    /// <param name="priorityAbilityIds">Priority-cast list.</param>
    /// <returns>Enemy id, or null.</returns>
    public static int? SelectPriorityCastTarget(IEnumerable<CastingTarget> casting, IReadOnlyList<string>? priorityAbilityIds = null)
    {
        ArgumentNullException.ThrowIfNull(casting);
        var priority = priorityAbilityIds ?? Array.Empty<string>();
        int Rank(string abilityId)
        {
            for (var i = 0; i < priority.Count; i++)
            {
                if (string.Equals(priority[i], abilityId, StringComparison.Ordinal))
                {
                    return i;
                }
            }
            return priority.Count;
        }
        CastingTarget? best = null;
        var bestRank = int.MaxValue;
        foreach (var c in casting)
        {
            var r = Rank(c.AbilityId);
            if (best is null || r < bestRank || (r == bestRank && c.ActorId < best.ActorId))
            {
                best = c;
                bestRank = r;
            }
        }
        return best?.ActorId;
    }

    /// <summary>
    /// BOT-KICK-TIMING: a human M+ player needs 0.3-0.8 s to see a cast and react; 12 ticks is 600 ms at 20 Hz, the
    /// middle of that range. No kick lands earlier than this after the cast starts (bounded by the cast itself).
    /// </summary>
    public const int KickReactionTicks = 12;

    /// <summary>
    /// BOT-KICK-TIMING: the kick window opens at least this long before the cast resolves, so the assigned kicker still
    /// lands it: 40 ticks (2 s) covers the longest player GCD (30 ticks) started the tick before the window opened plus
    /// the slowest skill-dial reaction delay (10 ticks).
    /// </summary>
    public const int KickLandMarginTicks = 40;

    /// <summary>
    /// BOT-KICK-TIMING (design/BIBLE.md sections 3, 4 and 9; no TS counterpart): the first tick a bot may kick
    /// <paramref name="caster"/>'s cast. Good players kick in the back half of a long cast, after a reaction: the delay is
    /// half the cast but at least <see cref="KickReactionTicks"/>, capped at <see cref="KickLandMarginTicks"/> before the
    /// cast resolves, and never below the reaction (or the cast's last tick, whichever is sooner). The Choir's 80-tick
    /// heal opens at 40. Integer ticks only; a caster with no cast fields opens at once.
    /// </summary>
    /// <param name="caster">The casting enemy.</param>
    /// <returns>The opening tick.</returns>
    public static int KickOpensTick(ActorState caster)
    {
        ArgumentNullException.ThrowIfNull(caster);
        if (caster.CastStartTick is not { } start || caster.CastEndsTick is not { } ends || ends <= start)
        {
            return int.MinValue;
        }
        var duration = ends - start;
        var backHalf = Math.Min(Math.Max(KickReactionTicks, duration / 2), duration - KickLandMarginTicks);
        return start + Math.Max(Math.Min(KickReactionTicks, duration - 1), backHalf);
    }

    /// <summary>ledger.ts selectKicker: the lowest eligible actorId, or null.</summary>
    /// <param name="eligibleActorIds">Eligible kickers.</param>
    /// <returns>Kicker id, or null.</returns>
    public static int? SelectKicker(IReadOnlyCollection<int> eligibleActorIds)
    {
        ArgumentNullException.ThrowIfNull(eligibleActorIds);
        return eligibleActorIds.Count == 0 ? null : eligibleActorIds.Min();
    }

    /// <summary>
    /// ledger.ts resolveInterrupt: full ledger resolution for one interrupt ability id. <paramref name="eligibleClassmates"/>
    /// is already narrowed to players whose own class carries <paramref name="interruptAbilityId"/>.
    /// </summary>
    /// <param name="actors">Actor table.</param>
    /// <param name="interruptAbilityId">Interrupt ability.</param>
    /// <param name="eligibleClassmates">Candidate kickers.</param>
    /// <param name="tick">Current tick.</param>
    /// <param name="priorityAbilityIds">Priority-cast list.</param>
    /// <param name="interruptOffGcd">True when the interrupt's own gcdTicks is 0: a kicker need not be off the GCD.</param>
    /// <returns>The assignment, or null.</returns>
    public static InterruptAssignment? ResolveInterrupt(
        SortedDictionary<int, ActorState> actors,
        string interruptAbilityId,
        IEnumerable<int> eligibleClassmates,
        int tick,
        IReadOnlyList<string>? priorityAbilityIds = null,
        bool interruptOffGcd = false)
    {
        ArgumentNullException.ThrowIfNull(actors);
        ArgumentNullException.ThrowIfNull(eligibleClassmates);
        var targetId = SelectPriorityCastTarget(CastingEnemies(actors), priorityAbilityIds);
        if (targetId is null)
        {
            return null;
        }
        // Spell queue window (Readiness.IsCastLocked): a kicker in the last window ticks of its own cast counts, its
        // kick landing when that cast ends, but only when that tick is still inside the enemy's cast. The kick that
        // lands first wins, ties to the lowest id (BIBLE s3: intended tick, fixed tiebreak).
        var targetEnds = actors[targetId.Value].CastEndsTick ?? int.MaxValue;
        var ready = eligibleClassmates.Where(id =>
            actors.TryGetValue(id, out var a)
            && a.Alive
            && !Readiness.IsCastLocked(a, tick)
            && Readiness.LandingTick(a, tick) < targetEnds
            && Readiness.CooldownReadyTick(a, interruptAbilityId) <= tick
            && (interruptOffGcd || a.GcdReadyTick <= tick)).ToList();
        if (ready.Count == 0)
        {
            return null;
        }
        var firstLanding = ready.Min(id => Readiness.LandingTick(actors[id], tick));
        var kickerId = SelectKicker(ready.Where(id => Readiness.LandingTick(actors[id], tick) == firstLanding).ToList());
        return kickerId is null ? null : new InterruptAssignment(targetId.Value, kickerId.Value);
    }

    /// <summary>
    /// CROSS-CLASS-KICK-ORDER (finding f-9a4e663c; design/BIBLE.md sections 3, 4 and 9; no TS counterpart): the party's
    /// kick ledger across every class. The target is the same priority-cast target as <see cref="ResolveInterrupt"/>; the
    /// kicker is the alive seat whose own interrupt is off cooldown, off the GCD and within its range of the target
    /// (<see cref="Casting.RangeMm"/>, the sim's own integer check), taken in a fixed order: least recently kicked first
    /// (the earliest interrupt-ready tick), ties to the lowest seat id. One cast gets one kicker, and consecutive casts
    /// rotate across seats. Integer ticks and mm only.
    /// </summary>
    /// <param name="actors">Actor table.</param>
    /// <param name="seats">
    /// Every seat with an interrupt: its id, its interrupt id, that interrupt's range, mm, and whether that interrupt is
    /// off the GCD (its own gcdTicks is 0, main #370 f-80b5cf16: such a kicker need not be off the GCD).
    /// </param>
    /// <param name="tick">Current tick.</param>
    /// <param name="priorityAbilityIds">Priority-cast list.</param>
    /// <returns>The assignment, or null.</returns>
    public static InterruptAssignment? ResolvePartyInterrupt(
        SortedDictionary<int, ActorState> actors,
        IEnumerable<(int ActorId, string InterruptAbilityId, long RangeMm, bool InterruptOffGcd)> seats,
        int tick,
        IReadOnlyList<string>? priorityAbilityIds = null)
    {
        ArgumentNullException.ThrowIfNull(actors);
        ArgumentNullException.ThrowIfNull(seats);
        var targetId = SelectPriorityCastTarget(CastingEnemies(actors), priorityAbilityIds);
        if (targetId is null)
        {
            return null;
        }
        var target = actors[targetId.Value];
        var kicker = seats
            .Where(s => actors.TryGetValue(s.ActorId, out var a)
                && a.Alive
                && Readiness.CooldownReadyTick(a, s.InterruptAbilityId) <= tick
                && (s.InterruptOffGcd || a.GcdReadyTick <= tick)
                && Casting.RangeMm(a, target) <= s.RangeMm)
            .OrderBy(s => Readiness.CooldownReadyTick(actors[s.ActorId], s.InterruptAbilityId))
            .ThenBy(s => s.ActorId)
            .Select(s => (int?)s.ActorId)
            .FirstOrDefault();
        if (kicker is not null)
        {
            return new InterruptAssignment(targetId.Value, kicker.Value);
        }
        var late = LateKicker(actors, seats, target, tick);
        return late is null ? null : new InterruptAssignment(targetId.Value, late.Value);
    }

    /// <summary>
    /// KICK-LEDGER-COOLDOWN-GAP (finding f-13b5c33a; the WoW Mythic+ kick rotation): when no seat can kick the cast right
    /// now, the next must-kick goes to the seat that can land a kick before the cast resolves. A seat whose interrupt is on
    /// cooldown but comes off it before the resolve waits and kicks late, and a seat whose interrupt is ready but out of
    /// its range walks in (the Warden's 3 m Shield Bash). The landing tick of a seat is the latest of now, its cooldown
    /// end, its GCD (an off-GCD interrupt has none), its own cast ending and its walk into range at the seat's own speed
    /// (<see cref="DefaultWalkMmPerTick"/> when it has none). A kick must land strictly before the cast's end tick (a
    /// cast ending on a tick completes before that tick's commands run). The earliest landing wins, ties to the least
    /// recently kicked (earliest cooldown end), then the lowest seat id. Integer ticks and mm only.
    /// </summary>
    private static int? LateKicker(
        SortedDictionary<int, ActorState> actors,
        IEnumerable<(int ActorId, string InterruptAbilityId, long RangeMm, bool InterruptOffGcd)> seats,
        ActorState target,
        int tick)
    {
        if (target.CastEndsTick is not { } ends)
        {
            return null;
        }
        return seats
            .Where(s => actors.TryGetValue(s.ActorId, out var a) && a.Alive)
            .Select(s => (Seat: s, Actor: actors[s.ActorId]))
            .Select(x => (x.Seat.ActorId, Ready: Readiness.CooldownReadyTick(x.Actor, x.Seat.InterruptAbilityId), Land: LandingTickOf(x.Actor, x.Seat, target, tick)))
            .Where(x => x.Land < ends)
            .OrderBy(x => x.Land)
            .ThenBy(x => x.Ready)
            .ThenBy(x => x.ActorId)
            .Select(x => (int?)x.ActorId)
            .FirstOrDefault();
    }

    /// <summary>The tick <paramref name="seat"/> can land its interrupt on <paramref name="target"/> if it starts at <paramref name="tick"/>.</summary>
    private static long LandingTickOf(ActorState a, (int ActorId, string InterruptAbilityId, long RangeMm, bool InterruptOffGcd) seat, ActorState target, int tick)
    {
        long land = Math.Max(tick, Readiness.CooldownReadyTick(a, seat.InterruptAbilityId));
        land = Math.Max(land, Readiness.LandingTick(a, tick));
        if (!seat.InterruptOffGcd)
        {
            land = Math.Max(land, a.GcdReadyTick);
        }
        var gap = Casting.RangeMm(a, target) - seat.RangeMm;
        if (gap > 0)
        {
            var speed = a.SpeedMmPerTick ?? DefaultWalkMmPerTick;
            land = Math.Max(land, tick + ((gap + speed - 1) / speed));
        }
        return land;
    }

    /// <summary>
    /// KICK-LEDGER-COOLDOWN-GAP: the walk speed, mm per tick, the ledger assumes for a seat with no speed of its own; equal to
    /// the dungeon content's baseSpeedMmPerTick (180 mm/tick, 3.6 m per second at 20 Hz).
    /// </summary>
    public const long DefaultWalkMmPerTick = 180;
}
