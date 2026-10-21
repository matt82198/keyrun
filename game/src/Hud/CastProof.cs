using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;

namespace Keyrun.Hud;

/// <summary>
/// The HUD-DECLUTTER cast proof lines (R-DECLUTTER-1; BIBLE s3 "cast bars are the enemy's language"). One placement
/// decision per live cast per step: the frame an enemy cast draws on (the current target or the boss frame) or the
/// over-head bar, decided once from the step's own state (<see cref="UnitFrameModel.Build"/> and
/// <see cref="HudDeclutter.CastPlacement"/>). The "HUD castbar" line and, when the cast draws on a unit frame, the
/// "HUD unitframe cast" line are emitted from that one decision, so both describe the same moment. A retarget mid-cast
/// is a new placement and logs again: the frame while targeted, the over-head bar after (WoW: the target's cast shows
/// on the target frame; a cast on a unit that is no longer targeted draws over its head).
/// Godot-free, so the decision is tested without the engine.
/// </summary>
public sealed class CastProofLog
{
    private readonly Dictionary<int, (int Start, string Where)> _last = new();

    /// <summary>The proof lines for this step, in actor id order.</summary>
    /// <param name="state">The state after the step.</param>
    /// <param name="humanActorId">The played character's actor id (whose target draws the target frame).</param>
    /// <param name="interruptible">Whether an ability id is interruptible (the frame line's flag).</param>
    /// <returns>The lines to print, in order.</returns>
    public IReadOnlyList<string> Observe(SimState state, int humanActorId, Func<string, bool> interruptible)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(interruptible);
        var frames = UnitFrameModel.Build(state, humanActorId);
        var lines = new List<string>();
        foreach (var (id, actor) in state.Actors)
        {
            if (!actor.Alive || actor.CastId is not { } castId || actor.CastStartTick is not { } start)
            {
                _last.Remove(id);
                continue;
            }
            var where = HudDeclutter.CastPlacement(actor, frames);
            if (_last.TryGetValue(id, out var last) && last == (start, where))
            {
                continue;
            }
            _last[id] = (start, where);
            lines.Add($"HUD castbar actor={id} kind={HudDeclutter.Side(actor)} where={where} ability={castId}");
            if (where == "frame" && actor.CastEndsTick is not null)
            {
                var kind = frames.First(f => f.ActorId == id).Kind;
                lines.Add($"HUD unitframe cast kind={kind} actor={id} ability={castId} interruptible={(interruptible(castId) ? 1 : 0)}");
            }
        }
        return lines;
    }
}
