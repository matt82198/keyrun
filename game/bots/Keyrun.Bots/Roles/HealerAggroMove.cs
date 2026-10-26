using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Bots.Shared;
using Keyrun.Sim.Core;

namespace Keyrun.Bots.Roles;

/// <summary>
/// R-HEALAGGRO-1 (amendment c, S2): a healer with no heal cast ready (GCD-locked or idle, never cast-locked) that holds
/// melee aggro walks toward its tank instead of standing still. Movement is never GCD-gated; the path comes from
/// <see cref="PathMoves"/> and the tank seat from the party's living allies.
/// </summary>
public static class HealerAggroMove
{
    /// <summary>
    /// Melee threat range, mm: 5 m. The same reach the shared Floor helper uses for a melee bot's re-approach
    /// (shared/Floor.cs MeleeRangeMm, private there, so mirrored here rather than widening Floor's surface).
    /// </summary>
    public const double MeleeThreatRangeMm = 5 * 1000;

    /// <summary>
    /// The move toward the tank seat, or null. Null when <paramref name="healCommands"/> is not empty (the heal branch
    /// already acts), when the healer is cast-locked, when no living engaged enemy targeting this seat is within
    /// <see cref="MeleeThreatRangeMm"/>, or when no other living seat exists.
    /// </summary>
    /// <param name="view">Bot view.</param>
    /// <param name="self">The healer.</param>
    /// <param name="healCommands">The heal branch's commands; movement only fills an otherwise empty heal branch.</param>
    /// <returns>The move, or null.</returns>
    public static MoveCommand? Command(BotView view, ActorState self, IReadOnlyList<Command> healCommands)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(healCommands);
        var tick = view.State.Tick;
        if (healCommands.Count > 0 || Readiness.IsCastLocked(self, tick))
        {
            return null;
        }
        var actors = view.State.Actors;
        var threatened = actors.Values.Any(a =>
            a.Kind == ActorKind.Enemy && a.Alive && !a.Idle && a.TargetId == self.Id
            && RoleShared.Dist(a, self) <= MeleeThreatRangeMm);
        if (!threatened || TankSeat(actors, self) is not { } tank)
        {
            return null;
        }
        return PathMoves.Toward(view, tick, self, tank.Pos);
    }

    /// <summary>
    /// The tank seat: the living ally (not the healer) holding the most living engaged enemies, ties to the lowest
    /// seat id; with no ally holding any enemy this is the lowest-numbered other living seat. Null when none is alive.
    /// </summary>
    /// <param name="actors">The state's actors, id-ordered.</param>
    /// <param name="self">The healer, excluded from the candidates.</param>
    /// <returns>The tank seat, or null.</returns>
    public static ActorState? TankSeat(SortedDictionary<int, ActorState> actors, ActorState self)
    {
        var holds = actors.Values
            .Where(a => a.Kind == ActorKind.Player && a.Alive && a.Id != self.Id)
            .Select(a => (Ally: a, Count: actors.Values.Count(e => e.Kind == ActorKind.Enemy && e.Alive && !e.Idle && e.TargetId == a.Id)))
            .ToList();
        return holds.Count == 0
            ? null
            : holds.OrderByDescending(h => h.Count).ThenBy(h => h.Ally.Id).First().Ally;
    }
}
