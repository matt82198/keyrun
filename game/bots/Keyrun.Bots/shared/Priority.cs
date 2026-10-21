using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;

namespace Keyrun.Bots.Shared;

/// <summary>
/// Port of bots/src/shared/priority.ts: target selection and rotation-priority helpers. Pure functions over the
/// actor table, which iterates ascending by id (the JS order of integer keys).
/// </summary>
public static class Priority
{
    private static IEnumerable<ActorState> AliveEnemies(SortedDictionary<int, ActorState> actors) =>
        actors.Values.Where(a => a.Kind == ActorKind.Enemy && a.Alive && !a.Idle);

    private static IEnumerable<ActorState> AliveAllies(SortedDictionary<int, ActorState> actors) =>
        actors.Values.Where(a => a.Kind == ActorKind.Player && a.Alive);

    /// <summary>priority.ts threatDeficit: how far below the top threat holder <paramref name="tankId"/> sits on <paramref name="enemy"/>.</summary>
    /// <param name="enemy">Enemy.</param>
    /// <param name="tankId">Tank.</param>
    /// <returns>Non-negative deficit.</returns>
    public static long ThreatDeficit(ActorState enemy, int tankId)
    {
        ArgumentNullException.ThrowIfNull(enemy);
        var top = enemy.Threat.Count > 0 ? enemy.Threat.Values.Max() : 0;
        var mine = enemy.Threat.TryGetValue(tankId, out var m) ? m : 0;
        return Math.Max(0, top - mine);
    }

    /// <summary>priority.ts pickTankTarget: the alive enemy with the highest threat deficit, ties to the lowest id.</summary>
    /// <param name="actors">Actor table.</param>
    /// <param name="tankId">Tank.</param>
    /// <returns>Enemy id, or null when none remain.</returns>
    public static int? PickTankTarget(SortedDictionary<int, ActorState> actors, int tankId)
    {
        ArgumentNullException.ThrowIfNull(actors);
        ActorState? best = null;
        long bestDeficit = -1;
        foreach (var e in AliveEnemies(actors))
        {
            var d = ThreatDeficit(e, tankId);
            if (d > bestDeficit || (d == bestDeficit && best is not null && e.Id < best.Id))
            {
                best = e;
                bestDeficit = d;
            }
        }
        return best?.Id;
    }

    /// <summary>
    /// priority.ts pickHealTarget: the lowest health-fraction ally strictly under <paramref name="thresholdPct"/>,
    /// ties to the lowest id. Fractions are doubles (<c>health / maxHealth</c>), as in the TS.
    /// </summary>
    /// <param name="actors">Actor table.</param>
    /// <param name="thresholdPct">Threshold in 0..1.</param>
    /// <returns>Ally id, or null.</returns>
    public static int? PickHealTarget(SortedDictionary<int, ActorState> actors, double thresholdPct = 0.7)
    {
        ArgumentNullException.ThrowIfNull(actors);
        var candidates = AliveAllies(actors).Where(a => Pct(a) < thresholdPct).ToList();
        if (candidates.Count == 0)
        {
            return null;
        }
        return candidates.OrderBy(Pct).ThenBy(a => a.Id).First().Id;
    }

    private static double Pct(ActorState a) => (double)a.Health / a.MaxHealth;

    /// <summary>priority.ts pickDispelTarget: the lowest-id alive ally carrying an effect in <paramref name="dispellableIds"/>.</summary>
    /// <param name="actors">Actor table.</param>
    /// <param name="dispellableIds">Dispellable effect ids.</param>
    /// <returns>Ally id, or null.</returns>
    public static int? PickDispelTarget(SortedDictionary<int, ActorState> actors, IReadOnlySet<string> dispellableIds)
    {
        ArgumentNullException.ThrowIfNull(actors);
        ArgumentNullException.ThrowIfNull(dispellableIds);
        return AliveAllies(actors).FirstOrDefault(a => a.Effects.Any(e => dispellableIds.Contains(e.EffectId)))?.Id;
    }

    /// <summary>priority.ts pickRotationAbility: the first ability in <paramref name="rotation"/> that <paramref name="isReady"/> accepts.</summary>
    /// <param name="rotation">Abilities in priority order.</param>
    /// <param name="isReady">Readiness predicate.</param>
    /// <returns>Ability id, or null.</returns>
    public static string? PickRotationAbility(IEnumerable<string> rotation, Func<string, bool> isReady)
    {
        ArgumentNullException.ThrowIfNull(rotation);
        ArgumentNullException.ThrowIfNull(isReady);
        foreach (var abilityId in rotation)
        {
            if (isReady(abilityId))
            {
                return abilityId;
            }
        }
        return null;
    }

    /// <summary>priority.ts isInterruptResponsible: only the lowest eligible actorId kicks.</summary>
    /// <param name="selfId">Asking bot.</param>
    /// <param name="eligibleActorIds">Bots with their interrupt ready.</param>
    /// <returns>True when <paramref name="selfId"/> is the lowest.</returns>
    public static bool IsInterruptResponsible(int selfId, IReadOnlyCollection<int> eligibleActorIds)
    {
        ArgumentNullException.ThrowIfNull(eligibleActorIds);
        return eligibleActorIds.Count != 0 && selfId == eligibleActorIds.Min();
    }
}
