using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;

namespace Keyrun.Bots.Shared;

/// <summary>
/// G2.5 / f-7b374e72 (Matt 2026-10-25: "Tanks very often will run ahead of the group while they wait out of LOS, and grab all
/// the packs and bring them back... he should not be just running it down"): the tank walks a tagged gather back to its
/// gather spot. This is a pure rule over state and plan; it changes nothing. It returns the spot when a gather's engaged group
/// is on the field, <see cref="ChainPull"/> has no pack it could still tag, and the leader stands farther than
/// <see cref="ArriveMm"/> from the spot. Otherwise null.
/// </summary>
public static class GatherReturn
{
    /// <summary>The leader counts as arrived within this distance of the gather spot, mm.</summary>
    public const long ArriveMm = 2000;

    /// <summary>
    /// The gather spot the leader walks back to this tick, or null. Null when the leader is not a living tank, no fight is on,
    /// an engaged enemy is an elite or boss (<see cref="ForcesPlan.ChainBlocked"/>), no gather has an engaged group, a tag is
    /// still open (<see cref="ChainPull.GatherNext"/> is non-null), or the leader is already within <see cref="ArriveMm"/>.
    /// </summary>
    /// <param name="state">The state.</param>
    /// <param name="layout">The layout.</param>
    /// <param name="plan">The pull plan (its gathers, groups and forces table).</param>
    /// <param name="leader">The party leader.</param>
    /// <returns>The gather spot to walk to, or null.</returns>
    public static Vec3? Spot(SimState state, NavLayout layout, PullPlan plan, ActorState leader)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(leader);
        if (!leader.Alive || leader.Role != ActorRole.Tank)
        {
            return null;
        }
        var gathers = plan.Gathers ?? Array.Empty<GatherSpot>();
        var blocked = plan.Forces.ChainBlocked;
        var engaged = state.Actors.Values.Where(a => a.Kind == ActorKind.Enemy && a.Alive && !a.Idle).ToList();
        if (engaged.Count == 0 || engaged.Any(a => blocked.Contains(a.ClassId)))
        {
            return null;
        }
        var gather = gathers.FirstOrDefault(g => plan.Groups.Any(grp =>
            g.GroupIds.Contains(grp.Id) && grp.ActorIds.Any(id => engaged.Any(e => e.Id == id))));
        if (gather is null || PendingBlocked(state, plan, gather) || ChainPull.GatherNext(state, layout, plan, leader, engaged, out _) is not null)
        {
            return null;
        }
        return Dist2(leader.Pos, gather.Spot) > ArriveMm * ArriveMm ? gather.Spot : null;
    }

    /// <summary>
    /// True when a pending group of <paramref name="gather"/> (one with an idle living member) holds an enemy whose class is
    /// chain-blocked. It reuses the blocked-class check <see cref="ChainPull.GatherNext"/> applies, so the walk-back never
    /// fetches the tank toward a gather whose next pack is an elite or boss.
    /// </summary>
    private static bool PendingBlocked(SimState state, PullPlan plan, GatherSpot gather) =>
        plan.Groups.Any(g => gather.GroupIds.Contains(g.Id) && ChainPull.Pending(state, g) && ChainPull.HoldsBlocked(state, g, plan.Forces.ChainBlocked));

    private static long Dist2(Vec3 a, Vec3 b)
    {
        var dx = a.X - b.X;
        var dz = a.Z - b.Z;
        return (dx * dx) + (dz * dz);
    }
}
