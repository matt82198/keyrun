using System;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;
using Keyrun.Sim.Nav;

namespace Keyrun.Bots.Shared;

/// <summary>
/// R-PATH-1 for the bots: every travel and engage goal (follow the lowest living seat, walk the route, hold, close on a
/// pack, a heal or dispel target, melee re-approach) walks the sim's computed path (<see cref="NavGrid"/>) instead of a
/// straight line. The move is still built by <see cref="Moves.MoveToward"/>, aimed at the next waypoint, so with the goal
/// in sight it is exactly the straight move it always was. Off a layout run (every oracle run) nothing changes.
/// </summary>
public static class PathMoves
{
    /// <summary>A move from <paramref name="self"/> along its path to <paramref name="goal"/> on <paramref name="view"/>'s layout, or straight at it with no layout; null when on the spot.</summary>
    /// <param name="view">This tick's view.</param>
    /// <param name="tick">Command tick.</param>
    /// <param name="self">The walker.</param>
    /// <param name="goal">Where it is going.</param>
    /// <returns>The move, or null.</returns>
    public static MoveCommand? Toward(BotView view, int tick, ActorState self, Vec3 goal)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(self);
        return view.Layout is { } layout
            ? Along(tick, self, layout, goal)
            : Moves.MoveToward(tick, self.Id, PointMm.Of(self.Pos), PointMm.Of(goal));
    }

    /// <summary>A move from <paramref name="self"/> at the next waypoint of its path to <paramref name="goal"/> through <paramref name="layout"/>.</summary>
    /// <param name="tick">Command tick.</param>
    /// <param name="self">The walker.</param>
    /// <param name="layout">The layout.</param>
    /// <param name="goal">Where it is going.</param>
    /// <returns>The move, or null when on the spot.</returns>
    public static MoveCommand? Along(int tick, ActorState self, NavLayout layout, Vec3 goal)
    {
        ArgumentNullException.ThrowIfNull(self);
        ArgumentNullException.ThrowIfNull(layout);
        var next = NavGrid.Of(layout).NextWaypoint(self.Pos, goal, self.Id);
        return Moves.MoveToward(tick, self.Id, PointMm.Of(self.Pos), PointMm.Of(next));
    }
}
