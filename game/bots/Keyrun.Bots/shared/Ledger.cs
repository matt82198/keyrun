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
    /// <returns>The assignment, or null.</returns>
    public static InterruptAssignment? ResolveInterrupt(
        SortedDictionary<int, ActorState> actors,
        string interruptAbilityId,
        IEnumerable<int> eligibleClassmates,
        int tick,
        IReadOnlyList<string>? priorityAbilityIds = null)
    {
        ArgumentNullException.ThrowIfNull(actors);
        ArgumentNullException.ThrowIfNull(eligibleClassmates);
        var targetId = SelectPriorityCastTarget(CastingEnemies(actors), priorityAbilityIds);
        if (targetId is null)
        {
            return null;
        }
        var ready = eligibleClassmates.Where(id =>
            actors.TryGetValue(id, out var a)
            && a.Alive
            && Readiness.CooldownReadyTick(a, interruptAbilityId) <= tick
            && a.GcdReadyTick <= tick).ToList();
        var kickerId = SelectKicker(ready);
        return kickerId is null ? null : new InterruptAssignment(targetId.Value, kickerId.Value);
    }
}
