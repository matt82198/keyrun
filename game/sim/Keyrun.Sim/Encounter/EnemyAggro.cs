using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Encounter;

/// <summary>
/// AGGRO-CHASE (C#-only, layout runs; f-f0c886eb; WoW Mythic+, where the bible is silent): the per-enemy half of the
/// aggro rule that <see cref="Aggro.Triggered"/> folds in beside each group's ring. Every enemy may carry its own aggro
/// radius (<see cref="ActorState.AggroMm"/>, measured from the enemy itself, so the front of a pack sees the party first)
/// and an assist radius (<see cref="ActorState.AssistMm"/>: an idle enemy that an engaged enemy comes near joins, with its
/// whole group, the body pull that chains a second pack into a fight dragged past it). An enemy that is dead, dormant
/// (the Seed's room gate), already engaged or evading home (<see cref="Leash"/>) neither sees nor assists. Integer
/// squared distances in XZ, inclusive; no randomness. With every radius null (every oracle run) nothing here fires.
/// </summary>
public static class EnemyAggro
{
    /// <summary>True when any living member of <paramref name="g"/> is evading home (its group is not engageable yet).</summary>
    /// <param name="state">The state.</param>
    /// <param name="g">The group.</param>
    /// <returns>True while the group leashes.</returns>
    public static bool Evading(SimState state, AggroGroup g)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(g);
        return g.ActorIds.Any(id => state.Actors.TryGetValue(id, out var a) && a.Alive && a.Evading);
    }

    /// <summary>
    /// True when some idle, living, non-dormant member of <paramref name="g"/> has a living player of
    /// <paramref name="players"/> within its own aggro radius.
    /// </summary>
    /// <param name="state">The post-combat state.</param>
    /// <param name="g">The group.</param>
    /// <param name="players">The living seated players.</param>
    /// <returns>True when a member sees a player.</returns>
    public static bool SeesPlayer(SimState state, AggroGroup g, IReadOnlyList<ActorState> players)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(g);
        ArgumentNullException.ThrowIfNull(players);
        foreach (var id in g.ActorIds)
        {
            if (Watcher(state, id) is { AggroMm: { } radius } enemy && players.Any(p => Aggro.InRing(p.Pos, enemy.Pos, radius)))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>True when some idle enemy of <paramref name="state"/> carries an assist radius (the assist pass has work).</summary>
    /// <param name="state">The state.</param>
    /// <returns>True when assist can fire.</returns>
    public static bool AnyAssister(SimState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.Actors.Values.Any(a => a.Kind == ActorKind.Enemy && a.Alive && a.Idle && a.AssistMm is not null);
    }

    /// <summary>
    /// True when some idle, living, non-dormant member of <paramref name="g"/> has, within its assist radius, a living
    /// enemy that is engaged (not idle, not evading) or that belongs to a group in <paramref name="engaging"/> (engaging
    /// this tick: social aggro is immediate).
    /// </summary>
    /// <param name="state">The post-combat state.</param>
    /// <param name="g">The idle group.</param>
    /// <param name="groups">Every group (to find the members of <paramref name="engaging"/>).</param>
    /// <param name="engaging">Group ids engaging this tick.</param>
    /// <returns>True when the group is assisted in.</returns>
    public static bool Assisted(SimState state, AggroGroup g, IReadOnlyList<AggroGroup> groups, IReadOnlySet<string> engaging)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(g);
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(engaging);
        HashSet<int>? joining = null;
        Dictionary<int, string>? groupOf = null;
        foreach (var id in g.ActorIds)
        {
            if (Watcher(state, id) is not { AssistMm: { } radius } enemy)
            {
                continue;
            }
            joining ??= groups.Where(o => engaging.Contains(o.Id)).SelectMany(o => o.ActorIds).ToHashSet();
            groupOf ??= GroupOf(groups);
            foreach (var other in state.Actors.Values)
            {
                if (other.Id == enemy.Id || other.Kind != ActorKind.Enemy || !other.Alive || other.Evading)
                {
                    continue;
                }
                // R-PATH-5 (p), WoW: an engaged boss group never social-aggros another group's room trash. A boss-group member
                // is skipped as the source when it is engaged (not idle) or engaging this tick (joining, its idle flag not yet
                // cleared), and only for a member of a different group.
                if ((!other.Idle || joining.Contains(other.Id)) && groupOf.TryGetValue(other.Id, out var otherGroup) && IdleWander.IsBossGroup(otherGroup) && otherGroup != g.Id)
                {
                    continue;
                }
                if ((!other.Idle || joining.Contains(other.Id)) && Aggro.InRing(other.Pos, enemy.Pos, radius))
                {
                    return true;
                }
            }
        }
        return false;
    }

    // Actor id -> the id of the group it belongs to (every group, boss or trash).
    private static Dictionary<int, string> GroupOf(IReadOnlyList<AggroGroup> groups)
    {
        var map = new Dictionary<int, string>();
        foreach (var o in groups)
        {
            foreach (var id in o.ActorIds)
            {
                map[id] = o.Id;
            }
        }
        return map;
    }

    // A member that can notice anything: alive, idle (not yet engaged), not evading.
    private static ActorState? Watcher(SimState state, int id) =>
        state.Actors.TryGetValue(id, out var a) && a.Kind == ActorKind.Enemy && a.Alive && a.Idle && !a.Evading ? a : null;
}
