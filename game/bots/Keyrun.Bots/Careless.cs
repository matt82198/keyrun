using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Shared;
using Keyrun.Sim.Core;

namespace Keyrun.Bots;

/// <summary>
/// A careless seat's plan (CARELESS-SEAT; Matt 2026-10-20, verbatim: "no a bad player will accidentally aggro more
/// groups, just test it naturally"): from <paramref name="FromTick"/> the seat strays off the party toward the enemy
/// group <paramref name="GroupActorIds"/> until that group engages.
/// </summary>
/// <param name="FromTick">The first tick the seat strays.</param>
/// <param name="GroupActorIds">The actor ids of the group it strays toward (a pull's spawns).</param>
public sealed record CarelessPlan(int FromTick, IReadOnlyList<int> GroupActorIds);

/// <summary>
/// A careless seat (CARELESS-SEAT): a bot that plays badly by wandering off toward an idle enemy group. From the plan's
/// tick, while every living member of the group still stands idle (PRESPAWN-AGGRO, BIBLE section 9.1), the seat ignores
/// its formation, the leader's hold and its role's own moves and walks through the layout's boxes
/// (<see cref="Follow.WalkTo"/>) at the group's nearest idle member; its role still decides its casts. A closed gate
/// simply holds it at the doorway, as it holds every walker. Nothing here engages anything: the seat walks into the
/// group's aggro ring and the sim's own rule (<c>Aggro.Triggered</c>) engages the group, which then picks its target by
/// the sim's own targeting (<c>ScriptTarget</c>: highest threat, else the nearest living player). Once the group has
/// engaged (or is dead) the seat plays its role again. Integers only, no PRNG draw of its own: the inner bot's memory and
/// PRNG thread through unchanged, so a run stays a pure function of its seed and commands.
/// </summary>
public static class Careless
{
    private sealed class CarelessBot : IBot
    {
        private readonly IBot _inner;
        private readonly CarelessPlan _plan;

        public CarelessBot(IBot inner, CarelessPlan plan)
        {
            _inner = inner;
            _plan = plan;
        }

        public BotDecision Decide(BotView view, BotMemory memory, int actorId, BotPrngState prng, IReadOnlyList<PingEvent>? pings = null)
        {
            var decided = _inner.Decide(view, memory, actorId, prng, pings);
            if (!view.State.Actors.TryGetValue(actorId, out var self) || StrayTarget(view, self, _plan) is not { } target)
            {
                return decided;
            }
            var commands = decided.Commands.Where(c => c is not MoveCommand and not DashCommand).ToList();
            if (Follow.WalkTo(view.State.Tick, self, view.Layout!, target.Pos) is { } walk)
            {
                commands.Add(walk);
            }
            return decided with { Commands = commands };
        }
    }

    /// <summary>The careless bot over <paramref name="inner"/> (rules in the class summary).</summary>
    /// <param name="inner">The seat's bot.</param>
    /// <param name="plan">The plan.</param>
    /// <returns>The bot.</returns>
    public static IBot Wrap(IBot inner, CarelessPlan plan)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(plan);
        return new CarelessBot(inner, plan);
    }

    /// <summary>
    /// The group member <paramref name="self"/> strays toward this tick: on a layout run, from the plan's tick, while
    /// <paramref name="self"/> lives and every living member of the group is idle, the nearest such member (squared XZ
    /// distance, then the lower id). Null otherwise (before the tick, off a layout run, once the group has engaged or died).
    /// </summary>
    /// <param name="view">This tick's view.</param>
    /// <param name="self">The careless seat.</param>
    /// <param name="plan">The plan.</param>
    /// <returns>The member, or null.</returns>
    public static ActorState? StrayTarget(BotView view, ActorState self, CarelessPlan plan)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(plan);
        if (view.Layout is null || view.State.Tick < plan.FromTick || !self.Alive)
        {
            return null;
        }
        var living = plan.GroupActorIds
            .Select(id => view.State.Actors.TryGetValue(id, out var a) ? a : null)
            .Where(a => a is { Alive: true })
            .Select(a => a!)
            .ToList();
        if (living.Count == 0 || living.Any(a => !a.Idle))
        {
            return null;
        }
        return living
            .OrderBy(a => Distance2(a.Pos, self.Pos))
            .ThenBy(a => a.Id)
            .First();
    }

    private static long Distance2(Vec3 a, Vec3 b)
    {
        var dx = a.X - b.X;
        var dz = a.Z - b.Z;
        return (dx * dx) + (dz * dz);
    }
}
