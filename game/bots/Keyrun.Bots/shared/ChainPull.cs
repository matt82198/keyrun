using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;

namespace Keyrun.Bots.Shared;

/// <summary>
/// R-AOE-1 clause (c), finding f-c8ed3a62 (Matt 2026-10-22: "the goal is normally to kill most of them at the same time /
/// carry them into the next pack to optimize"; defer to WoW Mythic+): the tank chain-pulls. While the engaged enemies are
/// all living non-elite trash, the party is healthy and together, and the next trash group stands within
/// <see cref="ReachMm"/>, the leader walks at that group and pulls it (walking at its centre ends in its aggro ring), so the
/// packs stack and die together. Elites and bosses are never chained into, and an engaged elite or boss stops the chain.
/// This is the one origin of the chain move: the bot in the lead seat (<see cref="Follow"/>, via the view), the scripted
/// Warden (ScriptedWarden) and the headless playtest runner all call <see cref="Move"/>, so they cannot drift apart. It
/// reads state only and changes no rule: wipes stay D44 (no living seated player), the leader stays the lowest living seat,
/// and the R-BOTTANK-LEASH-1 leash holds as <see cref="PartyLeashMm"/>: the tank never starts a chain while a living seat is
/// farther from him than the leash.
/// </summary>
public static class ChainPull
{
    /// <summary>
    /// The density bound: the next group's centre is chained into only within this distance of the leader, mm. The vault's
    /// room centres are 17000 mm apart (choir to pair), so one room of walking is in bound and two are not.
    /// </summary>
    public const long ReachMm = 25000;

    /// <summary>
    /// R-BOTTANK-LEASH-1: every living seat other than the leader stands within this distance of him to start (and keep) a chain,
    /// mm, so the bot tank never pulls a pack the party cannot reach with him.
    /// </summary>
    public const long PartyLeashMm = 12000;

    /// <summary>The leader chains only at or above this health, percent of max.</summary>
    public const int MinLeaderHealthPct = 60;

    /// <summary>Every living seat is at or above this health, percent of max, to start a chain.</summary>
    public const int MinPartyHealthPct = 40;

    /// <summary>
    /// The chain starts once the enemies still engaged have at most this much health left, percent of their max (WoW: a key
    /// group pulls the next pack when the current one is nearly dead, not on the first swing). A fresh pack is at 100%, so a
    /// chain never stacks a third pack onto two that have not been worked down.
    /// </summary>
    public const int EngagedHealthPct = 40;

    /// <summary>
    /// The chain move for <paramref name="leader"/> this tick, or null when the chain does not hold (no fight, a non-tank
    /// leader, an engaged elite or boss, a hurt or straggling party, or no chainable group in bound).
    /// </summary>
    /// <param name="tick">Command tick.</param>
    /// <param name="state">The state.</param>
    /// <param name="layout">The layout.</param>
    /// <param name="plan">The pull plan (its groups and forces table).</param>
    /// <param name="leader">The party leader.</param>
    /// <returns>The move toward the next group, or null.</returns>
    public static MoveCommand? Move(int tick, SimState state, NavLayout layout, PullPlan plan, ActorState leader)
    {
        if (Intent(state, layout, plan, leader) is not { } next)
        {
            return null;
        }
        foreach (var seat in state.Actors.Values.Where(a => a.Kind == ActorKind.Player && a.Alive && a.Id != leader.Id))
        {
            if ((seat.MaxHealth > 0 && seat.Health * 100 < MinPartyHealthPct * seat.MaxHealth) || Dist2(seat.Pos, leader.Pos) > PartyLeashMm * PartyLeashMm)
            {
                return null;
            }
        }
        return PathMoves.Along(tick, leader, layout, next.Center);
    }

    /// <summary>
    /// The group the leader means to chain into this tick, or null: the leader is a living tank at or above
    /// <see cref="MinLeaderHealthPct"/>, a fight is on, no engaged enemy is an elite or boss, and <see cref="NextGroup"/>
    /// names a group. It ignores the rest of the party, so the followers (<see cref="Follow.FollowCommand"/>) close up on the
    /// leader while <see cref="Move"/> holds him at the leash for them.
    /// </summary>
    /// <param name="state">The state.</param>
    /// <param name="layout">The layout.</param>
    /// <param name="plan">The pull plan.</param>
    /// <param name="leader">The party leader.</param>
    /// <returns>The group, or null.</returns>
    public static AggroGroup? Intent(SimState state, NavLayout layout, PullPlan plan, ActorState leader)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(leader);
        if (!leader.Alive || leader.Role != ActorRole.Tank || leader.MaxHealth <= 0 || leader.Health * 100 < MinLeaderHealthPct * leader.MaxHealth)
        {
            return null;
        }
        var blocked = plan.Forces.ChainBlocked;
        var engaged = state.Actors.Values.Where(a => a.Kind == ActorKind.Enemy && a.Alive && !a.Idle).ToList();
        if (engaged.Count == 0 || engaged.Any(a => blocked.Contains(a.ClassId)) ||
            engaged.Sum(a => a.Health) * 100 > EngagedHealthPct * engaged.Sum(a => a.MaxHealth))
        {
            return null;
        }
        return NextGroup(state, layout, plan, leader);
    }

    /// <summary>
    /// The group to chain into: of the dungeon's own pulls still idle and the one pack the forces planner would pull next, the
    /// one in the leader's box or ahead of it that stands nearest the leader, within <see cref="ReachMm"/>, holding no
    /// elite or boss. Null when none.
    /// </summary>
    /// <param name="state">The state.</param>
    /// <param name="layout">The layout.</param>
    /// <param name="plan">The pull plan.</param>
    /// <param name="leader">The leader.</param>
    /// <returns>The group, or null.</returns>
    public static AggroGroup? NextGroup(SimState state, NavLayout layout, PullPlan plan, ActorState leader)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(leader);
        var pick = ForcesPlanner.NextPackPoint(state, layout, plan, leader);
        var reach = HighBox(layout, leader.Pos);
        AggroGroup? best = null;
        var bestDist = long.MaxValue;
        foreach (var g in plan.Groups)
        {
            var wanted = g.PullIndex >= 0 || (pick is { } p && g.Center.Equals(p));
            if (!wanted || !Pending(state, g) || HoldsBlocked(state, g, plan.Forces.ChainBlocked))
            {
                continue;
            }
            var box = layout.BoxIndexAt(g.Center);
            if (box < 0 || box < reach)
            {
                continue;
            }
            var d = Dist2(g.Center, leader.Pos);
            if (d <= ReachMm * ReachMm && d < bestDist && PatrolOnPath(state, plan, leader.Pos, g.Center))
            {
                continue;
            }
            if (d <= ReachMm * ReachMm && d < bestDist)
            {
                best = g;
                bestDist = d;
            }
        }
        return best;
    }

    /// <summary>
    /// R-PATROL-1: true when a patrol with a living, still idle member walks within its aggro ring (plus
    /// <see cref="PatrolMarginMm"/>) of the straight way from <paramref name="from"/> to <paramref name="to"/>, so the chain
    /// would walk into it as a hidden add. The patrol's whole loop counts (it is a pure function of the tick, sampled every
    /// <see cref="PatrolStepMm"/>), because the tank arrives later than he leaves.
    /// </summary>
    private static bool PatrolOnPath(SimState state, PullPlan plan, Vec3 from, Vec3 to)
    {
        foreach (var k in plan.Patrols)
        {
            if (!k.Spawns.Any(s => state.Actors.TryGetValue(s.ActorId, out var a) && a.Alive && a.Idle))
            {
                continue;
            }
            var loop = Patrols.LoopMm(k);
            var limit = k.AggroMm + PatrolMarginMm;
            for (long at = 0; at < loop; at += PatrolStepMm)
            {
                var pt = Patrols.PointAt(k, (int)(at / k.SpeedMmPerTick));
                if (SegDist2(from, to, pt) <= limit * limit)
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>Distance between samples of a patrol's loop, mm.</summary>
    private const long PatrolStepMm = 1000;

    /// <summary>Slack past a patrol's aggro ring (member offset from the patrol's point, the tank's own stride), mm.</summary>
    private const long PatrolMarginMm = 2000;

    private static long SegDist2(Vec3 a, Vec3 b, Vec3 p)
    {
        var dx = b.X - a.X;
        var dz = b.Z - a.Z;
        var len2 = (dx * dx) + (dz * dz);
        var t = ((p.X - a.X) * dx) + ((p.Z - a.Z) * dz);
        if (len2 == 0 || t <= 0)
        {
            return Dist2(a, p);
        }
        if (t >= len2)
        {
            return Dist2(b, p);
        }
        var ex = p.X - (a.X + (dx * t / len2));
        var ez = p.Z - (a.Z + (dz * t / len2));
        return (ex * ex) + (ez * ez);
    }

    private static bool Pending(SimState state, AggroGroup g) =>
        g.ActorIds.Any(id => state.Actors.TryGetValue(id, out var a) && a.Alive && a.Idle);

    private static bool HoldsBlocked(SimState state, AggroGroup g, IReadOnlySet<string> blocked) =>
        g.ActorIds.Any(id => state.Actors.TryGetValue(id, out var a) && blocked.Contains(a.ClassId));

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

    private static long Dist2(Vec3 a, Vec3 b)
    {
        var dx = a.X - b.X;
        var dz = a.Z - b.Z;
        return (dx * dx) + (dz * dz);
    }
}
