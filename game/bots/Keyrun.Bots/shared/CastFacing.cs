using System;
using System.Collections.Generic;
using Keyrun.Bots;
using Keyrun.Sim.Core;

namespace Keyrun.Bots.Shared;

/// <summary>
/// R-FACING-2 (b): a bot seat that starts a targeted cast faces its target on that tick. The bots layer owns a bot's
/// yaw, so <see cref="Apply"/> puts a zero-direction <see cref="MoveCommand"/> with the target's yaw immediately before
/// each targeted <see cref="CastCommand"/> that needs it. Movement.ApplyMovement sets the yaw and returns without
/// moving, and the sim does not cancel a cast on a move, so the cast itself is unchanged. The face command is emitted
/// only when the yaw differs (<see cref="Moves.YawChanged"/>), so an already-facing seat adds nothing.
/// </summary>
public static class CastFacing
{
    /// <summary>The rule id of the face command (a row of <see cref="Reasons.Catalog"/>).</summary>
    public const string RuleId = "bot:cast-face";

    /// <summary>
    /// Inserts the face command before each targeted cast in <paramref name="commands"/>. Every other command is passed
    /// through in order. A target on the seat's own X/Z position has no facing, so no command is added for it.
    /// </summary>
    /// <param name="commands">One bot's commands for this tick, in issue order.</param>
    /// <param name="state">The sim state the commands were decided from.</param>
    /// <returns>The commands with face commands inserted; the input list is not modified.</returns>
    public static IReadOnlyList<Command> Apply(IReadOnlyList<Command> commands, SimState state)
    {
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(state);
        var result = new List<Command>(commands.Count);
        foreach (var command in commands)
        {
            if (command is CastCommand { TargetId: int targetId } cast
                && state.Actors.TryGetValue(cast.ActorId, out var seat)
                && state.Actors.TryGetValue(targetId, out var target)
                && (target.Pos.X != seat.Pos.X || target.Pos.Z != seat.Pos.Z))
            {
                var desired = Fixed.YawBetween(seat.Pos, target.Pos);
                if (Moves.YawChanged(seat.Yaw, desired))
                {
                    result.Add(Reasons.Tag(new MoveCommand(cast.Tick, cast.ActorId, new Dir2(0, 0), desired), RuleId));
                }
            }
            result.Add(command);
        }
        return result;
    }
}
