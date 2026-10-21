using System;
using System.Collections.Generic;
using System.Linq;
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
}
