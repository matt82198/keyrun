using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;

namespace Keyrun.Host;

/// <summary>
/// HUMAN-PLAY (D65/D66, "if you dont know, defer to wow"): the enemies the human's Tab cycles through. In WoW, Tab
/// targets the hostiles around you, never a pack two rooms away. Every pack stands prespawned and idle from tick 0
/// (PRESPAWN-AGGRO), so cycling every live enemy in id order (the sim's <c>Targeting.CycleTarget</c> over all enemies)
/// put Tab on an idle Pair Hall melee while the Choir fought (a headless Cinder replay: Tab from the Choir acolyte went
/// to actor 103, the first Pair Hall enemy). The candidates are the live engaged enemies (not idle); with none engaged,
/// the live enemies within <see cref="RangeMm"/> of the human. <c>Targeting.CycleTarget</c> still picks the next one
/// in id order. Godot-free, so PlayerInput and the headless replay (Keyrun.Playtest HumanReplay) share it.
/// </summary>
public static class TabTargeting
{
    /// <summary>The reach of Tab when nothing is engaged, mm (40 m, a WoW Tab range).</summary>
    public const long RangeMm = 40_000;

    /// <summary>The enemies Tab cycles through for <paramref name="human"/>.</summary>
    /// <param name="state">The state.</param>
    /// <param name="human">The human's actor.</param>
    /// <returns>The candidates (any order; the cycle sorts by id).</returns>
    public static IReadOnlyList<ActorState> Candidates(SimState state, ActorState human)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(human);
        var live = state.Actors.Values.Where(a => a.Kind == ActorKind.Enemy && a.Alive).ToList();
        var engaged = live.Where(a => !a.Idle).ToList();
        if (engaged.Count > 0)
        {
            return engaged;
        }
        return live.Where(a =>
        {
            long dx = a.Pos.X - human.Pos.X;
            long dz = a.Pos.Z - human.Pos.Z;
            return (dx * dx) + (dz * dz) <= RangeMm * RangeMm;
        }).ToList();
    }
}
