using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Runs;

namespace Keyrun.Host;

/// <summary>
/// player-commands.ts castTargetFor (D10 first-run fix), Godot-free: a hostile single-target ability with no live enemy
/// targeted goes at the nearest live enemy (squared XZ distance, lowest id on a tie); everything else keeps the current
/// target, else the caster.
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
        if (current is { } c && actors.TryGetValue(c, out var cur) && cur.Alive && cur.Kind != player.Kind)
        {
            return cur.Id;
        }
        int? best = null;
        long bestD2 = 0;
        foreach (var a in actors.Values)
        {
            if (!a.Alive || a.Kind == player.Kind)
            {
                continue;
            }
            var dx = a.Pos.X - player.Pos.X;
            var dz = a.Pos.Z - player.Pos.Z;
            var d2 = (dx * dx) + (dz * dz);
            if (best is null || d2 < bestD2 || (d2 == bestD2 && a.Id < best))
            {
                best = a.Id;
                bestD2 = d2;
            }
        }
        return best ?? current ?? humanId;
    }
}
