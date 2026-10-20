using System;
using Keyrun.Sim.Core;

namespace Keyrun.Bots.Shared;

/// <summary>
/// Hostile target choice that never picks an idle (not yet engaged) prespawned enemy (PRESPAWN-AGGRO, BIBLE section
/// 9.1; D30 "no pulling aggro unless they intentionally do"). It is used by the scripted Warden and the human cast
/// targeting in game/src/Host/PlayerInput.cs and lives here, Godot-free, so a test project can reach it. The idle rule
/// matches the holder lock's (Holder.LockHolders, 111995a): a candidate is an actor of another kind, alive and not idle.
/// Pure: reads the state, changes nothing.
/// </summary>
public static class EngagedTargets
{
    /// <summary>True when <paramref name="other"/> is a living, engaged actor hostile to <paramref name="self"/>.</summary>
    /// <param name="self">The chooser.</param>
    /// <param name="other">The candidate.</param>
    /// <returns>True when it may be targeted.</returns>
    public static bool IsEngagedHostile(ActorState self, ActorState other)
    {
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(other);
        return other.Alive && !other.Idle && other.Kind != self.Kind;
    }

    /// <summary>
    /// The engaged hostile nearest <paramref name="self"/> (squared XZ distance, lowest id on a tie), or null when none
    /// is engaged (every enemy dead or still idle).
    /// </summary>
    /// <param name="state">The state.</param>
    /// <param name="self">The chooser.</param>
    /// <returns>The nearest engaged hostile, or null.</returns>
    public static ActorState? Nearest(SimState state, ActorState self)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(self);
        ActorState? best = null;
        long bestD2 = 0;
        foreach (var a in state.Actors.Values)
        {
            if (!IsEngagedHostile(self, a))
            {
                continue;
            }
            var dx = a.Pos.X - self.Pos.X;
            var dz = a.Pos.Z - self.Pos.Z;
            var d2 = (dx * dx) + (dz * dz);
            if (best is null || d2 < bestD2 || (d2 == bestD2 && a.Id < best.Id))
            {
                best = a;
                bestD2 = d2;
            }
        }
        return best;
    }

    /// <summary>
    /// The hostile a single-target cast goes at: <paramref name="self"/>'s current target while it is a living engaged
    /// hostile, else the nearest engaged hostile, else null (nothing engaged: no pull by targeting).
    /// </summary>
    /// <param name="state">The state.</param>
    /// <param name="self">The chooser.</param>
    /// <returns>The target id, or null.</returns>
    public static int? HostileTarget(SimState state, ActorState self)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(self);
        if (self.TargetId is { } c && state.Actors.TryGetValue(c, out var current) && IsEngagedHostile(self, current))
        {
            return current.Id;
        }
        return Nearest(state, self)?.Id;
    }
}
