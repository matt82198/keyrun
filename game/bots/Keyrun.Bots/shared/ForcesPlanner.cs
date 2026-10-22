using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;

namespace Keyrun.Bots.Shared;

/// <summary>
/// The Enemy Forces table the route planner counts against (forces.json): each enemy's points and the points that fill the
/// bar. Built from content by the caller (the host from the registry), so Keyrun.Bots stays free of the HUD.
/// </summary>
/// <param name="PointsByEnemy">Enemy id to forces value.</param>
/// <param name="Required">Points that fill the bar (100%).</param>
public sealed record ForcesPlan(IReadOnlyDictionary<string, int> PointsByEnemy, int Required)
{
    /// <summary>The plan for <paramref name="registry"/>'s forces table, or null when content has none.</summary>
    /// <param name="registry">Content.</param>
    /// <returns>The plan, or null.</returns>
    public static ForcesPlan? Of(Keyrun.Content.ContentRegistry? registry)
    {
        if (registry?.Forces is not { } forces)
        {
            return null;
        }
        var table = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var v in forces.Values)
        {
            table[v.EnemyId] = v.Points;
        }
        return new ForcesPlan(table, forces.Required);
    }
}

/// <summary>
/// Finding f-3dcc702c (WoW Mythic+: plan the count, pull to 100, then boss). The scripted leader counts the forces bar the
/// way a key group does: the teaching pulls on the line are committed (the party fights them anyway), so the optional packs
/// only have to make up the difference, and the group picks the packs that give the most forces per health to chew through
/// (the cheapest kill for the count), not every pack on the map. It pulls the picked packs in route order, backtracking for
/// one left behind, and walks on to the boss only once the bar is full or no pack can add to it. Stateless: it reads the
/// run (state, packs, layout, dungeon) and the forces table only, and the pick is stable as packs die because a death moves
/// points from "committed" to "banked" without changing their sum.
/// </summary>
public static class ForcesPlanner
{
    /// <summary>
    /// The point the leader walks at to pull the next picked pack, or null to walk the route (the bar is full, the pulls on
    /// the line already cover it, or no pack remains). A pack is pending while any member lives and is idle (an engaged pack
    /// is a fight, which the roles run). Only picked packs in the leader's box or behind it are taken: a pack ahead waits
    /// until the leader reaches its room, so the planner never skips the room-by-room hold (LEADER-WAITS). Walking at the
    /// pack centre ends in the pack's aggro ring, which engages it.
    /// </summary>
    /// <param name="run">The run (its state, packs, layout and dungeon).</param>
    /// <param name="plan">The forces table.</param>
    /// <param name="leader">The leader.</param>
    /// <returns>The pack's centre, or null.</returns>
    public static Vec3? NextPackPoint(Keyrun.Sim.Runs.Run run, ForcesPlan plan, ActorState leader)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(leader);
        if (run.Content.Layout is not { } layout || run.Content.Packs is not { } packs || plan.Required <= 0)
        {
            return null;
        }
        var state = run.State;
        if (Forces.Read(state, plan.PointsByEnemy, plan.Required).Full)
        {
            return null;
        }
        // Committed: banked, plus every living enemy the party fights anyway (the dungeon's pulls) or is already fighting
        // (an engaged pack). The idle packs are the choice.
        long committed = Forces.Points(state, plan.PointsByEnemy);
        foreach (var spawn in run.Dungeon.Pulls.SelectMany(p => p.Spawns))
        {
            committed += LivePoints(state, plan, spawn.ActorId);
        }
        var pending = new List<(PackGroup Pack, int Order, long Points, long Health)>();
        for (var i = 0; i < packs.Count; i++)
        {
            var pack = packs[i];
            if (Pending(state, pack))
            {
                pending.Add((pack, i, pack.Spawns.Sum(s => LivePoints(state, plan, s.ActorId)), pack.Spawns.Sum(s => LiveHealth(state, s.ActorId))));
            }
            else
            {
                committed += pack.Spawns.Sum(s => LivePoints(state, plan, s.ActorId));
            }
        }
        var need = plan.Required - committed;
        if (need <= 0)
        {
            return null;
        }
        // Most forces per health first (cross-multiplied, integers only); the route order breaks a tie.
        var picked = new List<(PackGroup Pack, int Order, long Points, long Health)>();
        foreach (var p in pending.Where(p => p.Points > 0).OrderByDescending(p => p, new ForcesPerHealth()).ThenBy(p => p.Order))
        {
            if (need <= 0)
            {
                break;
            }
            picked.Add(p);
            need -= p.Points;
        }
        var reach = HighBox(layout, leader.Pos);
        PackGroup? best = null;
        var bestBox = int.MaxValue;
        foreach (var p in picked)
        {
            var box = Math.Max(0, layout.BoxIndexAt(p.Pack.Center));
            if ((reach < 0 || box <= reach) && box < bestBox)
            {
                best = p.Pack;
                bestBox = box;
            }
        }
        return best?.Center;
    }

    private sealed class ForcesPerHealth : IComparer<(PackGroup Pack, int Order, long Points, long Health)>
    {
        public int Compare((PackGroup Pack, int Order, long Points, long Health) a, (PackGroup Pack, int Order, long Points, long Health) b) =>
            (a.Points * Math.Max(1, b.Health)).CompareTo(b.Points * Math.Max(1, a.Health));
    }

    private static long LivePoints(SimState state, ForcesPlan plan, int actorId) =>
        state.Actors.TryGetValue(actorId, out var a) && a.Alive && plan.PointsByEnemy.TryGetValue(a.ClassId, out var points) ? Math.Max(0, points) : 0;

    private static long LiveHealth(SimState state, int actorId) =>
        state.Actors.TryGetValue(actorId, out var a) && a.Alive ? a.MaxHealth : 0;

    private static bool Pending(SimState state, PackGroup pack)
    {
        foreach (var spawn in pack.Spawns)
        {
            if (state.Actors.TryGetValue(spawn.ActorId, out var a) && a.Alive && a.Idle)
            {
                return true;
            }
        }
        return false;
    }

    private static int HighBox(NavLayout layout, Vec3 p)
    {
        var high = -1;
        for (var i = 0; i < layout.Boxes.Count; i++)
        {
            if (Movement.Contains(layout.Boxes[i].Box, p))
            {
                high = i;
            }
        }
        return high;
    }
}
