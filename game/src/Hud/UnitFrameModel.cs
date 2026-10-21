using System;
using System.Collections.Generic;
using Keyrun.Sim.Core;

namespace Keyrun.Hud;

/// <summary>One enemy unit frame (Godot-free): which frame, and the enemy it shows.</summary>
/// <param name="Kind">"target" (the human's current target) or "boss" (the engaged boss).</param>
/// <param name="ActorId">The enemy's actor id.</param>
public sealed record UnitFrame(string Kind, int ActorId);

/// <summary>
/// Which enemy unit frames show (HUD-DECLUTTER, Matt 2026-10-19: "enemy health does not need to be overhead, they can
/// have unitframes but not just a random number"): a boss frame while a live boss (class id ending ".boss") is in the
/// run, then a target frame for the human's current target when it is a live enemy other than that boss (the boss
/// frame already shows it). Display only. Godot-free, so the cast proof can be tested without the engine.
/// </summary>
public static class UnitFrameModel
{
    /// <summary>The frames to draw, top to bottom.</summary>
    /// <param name="state">The sim state.</param>
    /// <param name="humanActorId">The played character's actor id.</param>
    /// <returns>The frames.</returns>
    public static IReadOnlyList<UnitFrame> Build(SimState state, int humanActorId)
    {
        ArgumentNullException.ThrowIfNull(state);
        var frames = new List<UnitFrame>();
        int? bossId = null;
        foreach (var (id, actor) in state.Actors)
        {
            // PRESPAWN-AGGRO: the boss stands idle in its arena from tick 0; its frame appears when it engages, as in WoW.
            if (actor.Alive && !actor.Idle && actor.Kind != ActorKind.Player && actor.ClassId.EndsWith(".boss", StringComparison.Ordinal))
            {
                bossId = id;
                frames.Add(new UnitFrame("boss", id));
                break;
            }
        }
        if (state.Actors.TryGetValue(humanActorId, out var me) && me.TargetId is { } targetId && targetId != bossId &&
            state.Actors.TryGetValue(targetId, out var target) && target.Alive && target.Kind != ActorKind.Player)
        {
            frames.Add(new UnitFrame("target", targetId));
        }
        return frames;
    }
}
