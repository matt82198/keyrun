using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Shared;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Runs;

namespace Keyrun.Host;

/// <summary>
/// player-commands.ts castTargetFor (D10 first-run fix), Godot-free: a hostile single-target ability with no live engaged
/// enemy targeted goes at the nearest live engaged enemy (squared XZ distance, lowest id on a tie; an idle prespawned
/// enemy is never picked, PRESPAWN-AGGRO); everything else keeps the current target, else the caster.
/// </summary>
public static class CastTargeting
{
    /// <summary>The target a human cast of <paramref name="abilityId"/> is sent at.</summary>
    /// <param name="run">The run.</param>
    /// <param name="humanId">The caster.</param>
    /// <param name="abilityId">The ability.</param>
    /// <returns>The target id.</returns>
    public static int For(Run run, int humanId, string abilityId)
    {
        ArgumentNullException.ThrowIfNull(run);
        var actors = run.State.Actors;
        if (!actors.TryGetValue(humanId, out var player))
        {
            return humanId;
        }
        var current = player.TargetId;
        if (!run.Content.Abilities.TryGetValue(abilityId, out var def) || def.Shape.Kind != ShapeKind.Target ||
            Affinities.Of(def, run.Content.Effects) != Affinity.Hostile)
        {
            return current ?? humanId;
        }
        // PRESPAWN-AGGRO: the current target while it is a living ENGAGED enemy, else the nearest engaged one
        // (EngagedTargets, Keyrun.Bots). An idle prespawned enemy is never picked, so a cast never pulls a pack.
        if (EngagedTargets.HostileTarget(run.State, player) is { } target)
        {
            return target;
        }
        return current is { } c && actors.TryGetValue(c, out var cur) && cur.Alive && cur.Idle ? humanId : current ?? humanId;
    }
}
