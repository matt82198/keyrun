using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Nav;

namespace Keyrun.Sim.Encounter;

/// <summary>
/// AGGRO-CHASE leash (C#-only, layout runs; f-f0c886eb; WoW Mythic+: an enemy dragged too far from where it stood gives
/// up, walks home and resets). Every idle enemy keeps <see cref="ActorState.Home"/> on its own position. Once its group
/// has engaged, a living member whose <see cref="ActorState.LeashMm"/> is set and who stands farther than that from home
/// makes the whole group evade: every living member stops its cast, drops its target, threat and holder, turns idle and
/// <see cref="ActorState.Evading"/>, the group leaves <see cref="EncounterState.Engaged"/>, and
/// <c>{tick, type:"pack_evade", groupId, actorIds}</c> fires. Evaders walk home along the R-PATH-1 grid path
/// (<see cref="EvadeMoves"/>); one within a step of home lands on it. When every living member is home the group resets:
/// full health, no effects, idle and engageable again by the ordinary rules, with
/// <c>{tick, type:"pack_reset", groupId, actorIds}</c>. Integers only; with every leash null (every oracle run, the boss)
/// nothing here fires.
/// </summary>
public static class Leash
{
    /// <summary>
    /// The leash rules after combat, in place on <paramref name="next"/> (the run root owns this freshly stepped state):
    /// homes, arrivals and resets, then new evades. Returns the encounter with evading groups removed from the engaged set
    /// and their casters' telegraphs cancelled.
    /// </summary>
    /// <param name="next">The post-combat state.</param>
    /// <param name="encounter">The encounter.</param>
    /// <param name="groups">The run's aggro groups.</param>
    /// <param name="baseSpeedMmPerTick">The run's walk speed for actors without their own (the arrival step).</param>
    /// <param name="emit">Event sink.</param>
    /// <returns>The encounter after the leash rules.</returns>
    public static EncounterState Step(SimState next, EncounterState encounter, IReadOnlyList<AggroGroup> groups, long baseSpeedMmPerTick, Action<SimEvent> emit)
    {
        ArgumentNullException.ThrowIfNull(next);
        ArgumentNullException.ThrowIfNull(encounter);
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(emit);
        var tick = next.Tick;
        foreach (var a in next.Actors.Values)
        {
            if (a.Kind == ActorKind.Enemy && a.Alive && a.Idle && !a.Evading)
            {
                a.Home = a.WanderAnchor ?? a.Pos;
            }
        }

        foreach (var g in groups)
        {
            var evaders = Members(next, g).Where(a => a.Evading).ToList();
            if (evaders.Count == 0)
            {
                continue;
            }
            foreach (var a in evaders)
            {
                if (a.Home is { } home && Dist2(a.Pos, home) <= Square(Math.Max(1, a.SpeedMmPerTick ?? baseSpeedMmPerTick)))
                {
                    a.Pos = home;
                }
            }
            if (evaders.All(a => a.Home is not { } h || a.Pos == h))
            {
                foreach (var a in evaders)
                {
                    a.Evading = false;
                    a.Health = a.MaxHealth;
                    a.Effects.Clear();
                }
                emit(GroupEvent(tick, "pack_reset", g.Id, evaders));
            }
        }

        List<Telegraph>? keep = null;
        var engaged = encounter.Engaged;
        foreach (var g in groups)
        {
            if (!Pulls.IsEngaged(encounter, g.Id))
            {
                continue;
            }
            var living = Members(next, g).ToList();
            if (!living.Any(a => !a.Idle && a.LeashMm is { } leash && a.Home is { } home && Dist2(a.Pos, home) > Square(leash)))
            {
                continue;
            }
            foreach (var a in living)
            {
                if (a.CastId is not null)
                {
                    Casting.Stopcast(a, emit, tick);
                }
                a.TargetId = null;
                a.HolderId = null;
                a.Threat.Clear();
                a.Idle = true;
                a.Evading = true;
                // IDLE-WANDER: the pack walks home to its anchor (Home), then wanders again from where it resets.
                a.WanderAnchor = null;
                a.WanderLoop = null;
            }
            engaged = engaged.Where(id => !string.Equals(id, g.Id, StringComparison.Ordinal)).ToList();
            emit(GroupEvent(tick, "pack_evade", g.Id, living));
            var ids = living.Select(a => a.Id).ToHashSet();
            keep ??= encounter.Telegraphs.ToList();
            foreach (var tg in keep.Where(t => ids.Contains(t.SourceId)).ToList())
            {
                keep.Remove(tg);
                emit(new SimEvent(tick, "telegraph_cancel", ("telegraphId", JsValue.Int(tg.Id)), ("actorId", JsValue.Int(tg.SourceId)), ("cast", JsValue.Str(tg.AbilityId))));
            }
        }
        if (keep is null)
        {
            return encounter;
        }
        return encounter with { Engaged = engaged, Telegraphs = keep };
    }

    /// <summary>
    /// One move per living evading enemy of <paramref name="state"/> (the pre-combat state), in ascending actor id: toward
    /// the next waypoint of its grid path home, facing the way it walks. None for an evader already home.
    /// </summary>
    /// <param name="state">The pre-combat state.</param>
    /// <param name="grid">The layout grid.</param>
    /// <returns>The moves.</returns>
    public static IReadOnlyList<Command> EvadeMoves(SimState state, NavGrid grid)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(grid);
        var moves = new List<Command>();
        foreach (var a in state.Actors.Values)
        {
            if (a.Alive && a.Evading && a.Home is { } home && NavFollow.MoveAlong(state.Tick, a, grid, home, 0) is { } move)
            {
                moves.Add(move);
            }
        }
        return moves;
    }

    private static IEnumerable<ActorState> Members(SimState state, AggroGroup g) =>
        g.ActorIds.Select(id => state.Actors.TryGetValue(id, out var a) ? a : null).Where(a => a is { Alive: true }).Select(a => a!);

    private static SimEvent GroupEvent(int tick, string type, string groupId, IEnumerable<ActorState> members) =>
        new(tick, type, ("groupId", JsValue.Str(groupId)), ("actorIds", JsValue.Arr(members.Select(a => JsValue.Int(a.Id)))));

    private static long Dist2(Vec3 a, Vec3 b)
    {
        var dx = a.X - b.X;
        var dz = a.Z - b.Z;
        return (dx * dx) + (dz * dz);
    }

    private static long Square(long v) => v * v;
}
