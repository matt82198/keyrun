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
/// (<see cref="Follow.WalkTo"/>) at the group's nearest idle member; its role still decides its casts. It pulls between
/// fights, never mid-fight: while any engaged enemy lives (<see cref="Follow.AnyLivingEnemy"/>) it walks only as far as
/// the fight room's far doorway (<see cref="HoldPoint"/>) and waits there, as that room's shut gate held it before
/// PATH-TO-BOSS (STATE.md D44), then runs on when the fight ends. Nothing here engages anything: the seat walks into the
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
            if (!view.State.Actors.TryGetValue(actorId, out var self) ||
                (StrayTarget(view, self, _plan)?.Pos ?? HoldPoint(view, self, _plan)) is not { } goal)
            {
                return decided;
            }
            var commands = decided.Commands.Where(c => c is not MoveCommand and not DashCommand).ToList();
            if (Follow.WalkTo(view.State.Tick, self, view.Layout!, goal) is { } walk)
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
    /// distance, then the lower id), when no engaged enemy lives. Null otherwise (before the tick, off a layout run, while
    /// the party is in a fight, once the group has engaged or died).
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
        // Between pulls only (PR #308 run C: with no gate the seat walked mid-Choir down the open path, pulled the Pair
        // onto the Choir fight at tick 440 and the party wiped at tick 1630).
        if (Follow.AnyLivingEnemy(view.State))
        {
            return null;
        }
        return NearestIdleMember(view, self, plan);
    }

    /// <summary>
    /// Where <paramref name="self"/> waits while the party is in a fight (an engaged enemy lives) and the plan is active
    /// (its tick reached, the seat alive, every living member of the group still idle): the far doorway of the room the
    /// fight is in (layout route point k for box k, the box holding the nearest engaged enemy), when the group stands in a
    /// later box. It is the doorway the fight room's shut gate held a careless walker at before PATH-TO-BOSS (STATE.md
    /// D44), so the seat is ahead of the party when the fight ends and runs on into the group's ring
    /// (<see cref="StrayTarget"/>). Null otherwise (no fight, the plan inactive, or the group not further along the route).
    /// </summary>
    /// <param name="view">This tick's view.</param>
    /// <param name="self">The careless seat.</param>
    /// <param name="plan">The plan.</param>
    /// <returns>The hold point, or null.</returns>
    public static Vec3? HoldPoint(BotView view, ActorState self, CarelessPlan plan)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(plan);
        if (view.Layout is not { } layout || NearestIdleMember(view, self, plan) is not { } member ||
            EngagedTargets.Nearest(view.State, self) is not { } foe)
        {
            return null;
        }
        var fightBox = layout.BoxIndexAt(foe.Pos);
        return fightBox >= 0 && layout.BoxIndexAt(member.Pos) > fightBox ? layout.Route[fightBox] : null;
    }

    // The plan's nearest idle member (rules in StrayTarget's summary), fight or not.
    private static ActorState? NearestIdleMember(BotView view, ActorState self, CarelessPlan plan)
    {
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
