using System;
using System.Collections.Generic;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;
using Keyrun.Sim.Encounter;

namespace Keyrun.Sim.Nav;

/// <summary>
/// R-PATH-1 path following on a layout run: an actor walks its <see cref="NavGrid.FindPath"/> path one waypoint at a
/// time, as a plain <see cref="MoveCommand"/> toward the next waypoint, so combat moves it at its own speed (walk speed,
/// movement modifiers, roots) exactly as before; only the direction changes. Integer only; a run without a layout (every
/// oracle run) never reaches this code.
/// </summary>
public static class NavFollow
{
    /// <summary>Unit-vector scale of a move direction (the bots' command.ts DIR_SCALE and the chase's 1000).</summary>
    public const long DirScale = 1000;

    /// <summary>
    /// The path-following step: a move for <paramref name="actor"/> toward the next waypoint of its path to
    /// <paramref name="goal"/>, facing the way it walks; null once within <paramref name="arriveMm"/> of the goal.
    /// </summary>
    /// <param name="tick">Command tick.</param>
    /// <param name="actor">The walker.</param>
    /// <param name="grid">The layout grid.</param>
    /// <param name="goal">Destination.</param>
    /// <param name="arriveMm">Stop distance, mm.</param>
    /// <returns>The move, or null.</returns>
    public static MoveCommand? MoveAlong(int tick, ActorState actor, NavGrid grid, Vec3 goal, long arriveMm)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(grid);
        var dx = goal.X - actor.Pos.X;
        var dz = goal.Z - actor.Pos.Z;
        if ((dx * dx) + (dz * dz) <= arriveMm * arriveMm)
        {
            return null;
        }
        return Toward(tick, actor.Id, actor.Pos, grid.NextWaypoint(actor.Pos, goal, actor.Id));
    }

    /// <summary>A move from <paramref name="from"/> straight at <paramref name="to"/>, facing it; null when they coincide.</summary>
    /// <param name="tick">Command tick.</param>
    /// <param name="actorId">The walker.</param>
    /// <param name="from">Start.</param>
    /// <param name="to">Point to walk at.</param>
    /// <returns>The move, or null.</returns>
    public static MoveCommand? Toward(int tick, int actorId, Vec3 from, Vec3 to)
    {
        var delta = new Vec3(to.X - from.X, 0, to.Z - from.Z);
        if (delta.X == 0 && delta.Z == 0)
        {
            return null;
        }
        var dir = Fixed.NormaliseTo(delta, DirScale);
        return new MoveCommand(tick, actorId, new Dir2(dir.X, dir.Z), Fixed.YawBetween(from, to));
    }

    /// <summary>
    /// Enemy chase through the grid (R-PATH-1): every enemy move in <paramref name="commands"/> that is the chase's
    /// straight step at its script target (<see cref="Chase.ChaseCommands"/>: direction = target minus position,
    /// normalised to 1000) is re-aimed at the next waypoint of the enemy's path to that target. Turn-only moves, other
    /// moves and other commands pass unchanged, in order. With the target in sight the waypoint is the target itself and
    /// the command is unchanged.
    /// </summary>
    /// <param name="state">The pre-combat state the chase read.</param>
    /// <param name="commands">The encounter's commands.</param>
    /// <param name="grid">The layout grid.</param>
    /// <returns>The commands with chase moves routed.</returns>
    public static IReadOnlyList<Command> RouteChase(SimState state, IReadOnlyList<Command> commands, NavGrid grid)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(grid);
        List<Command>? output = null;
        for (var i = 0; i < commands.Count; i++)
        {
            var routed = Route(state, commands[i], grid);
            if (!ReferenceEquals(routed, commands[i]) && output is null)
            {
                output = new List<Command>(commands.Count);
                for (var k = 0; k < i; k++)
                {
                    output.Add(commands[k]);
                }
            }
            output?.Add(routed);
        }
        return output ?? commands;
    }

    private static Command Route(SimState state, Command command, NavGrid grid)
    {
        if (command is not MoveCommand move || (move.Dir.X == 0 && move.Dir.Z == 0) ||
            !state.Actors.TryGetValue(move.ActorId, out var actor) || actor.Kind != ActorKind.Enemy)
        {
            return command;
        }
        if (ScriptTarget.Resolve(state, actor).TargetId is not { } tid || !state.Actors.TryGetValue(tid, out var target))
        {
            return command;
        }
        var straight = Fixed.NormaliseTo(new Vec3(target.Pos.X - actor.Pos.X, 0, target.Pos.Z - actor.Pos.Z), DirScale);
        if (straight.X != move.Dir.X || straight.Z != move.Dir.Z)
        {
            return command;
        }
        var next = grid.NextWaypoint(actor.Pos, target.Pos, actor.Id);
        if (next.X == target.Pos.X && next.Z == target.Pos.Z)
        {
            return command;
        }
        return Toward(move.Tick, actor.Id, actor.Pos, next) ?? command;
    }

    /// <summary>
    /// The obstacle rule after the walkable clamp (R-PATH-1; inert with no obstacles, as the shipped layout has none): a
    /// move that ends strictly inside an obstacle slides along it, keeping the x step (z held) or else the z step (x
    /// held) when that end is free, and otherwise stays at <paramref name="pre"/>. An actor that already stood inside one
    /// (placed there) is not held.
    /// </summary>
    /// <param name="pre">Position before the move.</param>
    /// <param name="post">Position after the move and the walkable clamp.</param>
    /// <param name="obstacles">The obstacles.</param>
    /// <returns>The accepted position.</returns>
    public static Vec3 SlideOffObstacles(Vec3 pre, Vec3 post, IReadOnlyList<Bounds> obstacles)
    {
        ArgumentNullException.ThrowIfNull(obstacles);
        if (obstacles.Count == 0 || !NavGrid.Blocked(obstacles, post) || NavGrid.Blocked(obstacles, pre))
        {
            return post;
        }
        var alongX = post with { Z = pre.Z };
        if (!NavGrid.Blocked(obstacles, alongX))
        {
            return alongX;
        }
        var alongZ = post with { X = pre.X };
        return NavGrid.Blocked(obstacles, alongZ) ? pre : alongZ;
    }
}
