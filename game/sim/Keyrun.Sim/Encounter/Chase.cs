using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Encounter;

/// <summary>
/// Closes a melee enemy onto its target so its scripted swing can land (sim/encounter/chase.ts). A living enemy that
/// is not mid-cast and whose script casts a hostile target-shaped ability has an engage range = the shortest such
/// range; if its default target (<see cref="ScriptTarget"/> without an ability) is farther than
/// <c>engage - (actor.ChaseMarginMm ?? 500)</c>, it emits one <c>move</c> straight at that target, facing it. Speed
/// rides on ActorState (combat's move handling), never on the command. With no abilities nothing chases.
/// </summary>
public static class Chase
{
    /// <summary>How far inside its engage range a chaser stops, mm (chase.ts:35).</summary>
    public const long ChaseMarginMm = 500;

    // chase.ts:37 isHostileTargeted.
    private static bool IsHostileTargeted(ScriptAbility? ability) =>
        ability is not null && ability.ShapeKind == "target" && !ability.Heals;

    /// <summary>chase.ts:42 engageRangeMm: the shortest range among the script's hostile target-shaped casts (onDeath skipped).</summary>
    public static long? EngageRangeMm(IReadOnlyList<ScriptStep> script, IReadOnlyDictionary<string, ScriptAbility> abilities)
    {
        long? best = null;
        foreach (var step in script)
        {
            if (step.Trigger == StepTrigger.OnDeath)
            {
                continue;
            }
            abilities.TryGetValue(step.Cast, out var ability);
            if (IsHostileTargeted(ability) && (best is null || ability!.Range < best.Value))
            {
                best = ability!.Range;
            }
        }
        return best;
    }

    /// <summary>chase.ts:52 chaseCommands: one move per chasing enemy, in ascending actor id order.</summary>
    public static IReadOnlyList<Command> ChaseCommands(
        SimState state,
        IReadOnlyDictionary<int, IReadOnlyList<ScriptStep>> scripts,
        IReadOnlyDictionary<string, ScriptAbility> abilities)
    {
        var output = new List<Command>();
        foreach (var actorId in scripts.Keys.OrderBy(k => k))
        {
            var script = scripts[actorId];
            if (!state.Actors.TryGetValue(actorId, out var actor) || script is null || !actor.Alive || actor.CastId is not null)
            {
                continue;
            }
            var engage = EngageRangeMm(script, abilities);
            if (engage is null)
            {
                continue;
            }
            var targetId = ScriptTarget.Resolve(state, actor).TargetId;
            if (targetId is not { } tid || !state.Actors.TryGetValue(tid, out var target) || !target.Alive)
            {
                continue;
            }
            if (ScriptTarget.RangeMm(actor, target) <= Math.Max(0, engage.Value - (actor.ChaseMarginMm ?? ChaseMarginMm)))
            {
                // ENEMY-ENGAGE: in reach, the chaser still turns to face its target (a zero-direction move only
                // turns), so a pack the tank walks into, or a target that sidesteps, is faced and not swung past.
                var face = Fixed.YawBetween(actor.Pos, target.Pos);
                if (face != actor.Yaw)
                {
                    output.Add(new MoveCommand(state.Tick, actorId, new Dir2(0, 0), face));
                }
                continue;
            }
            var delta = new Vec3(target.Pos.X - actor.Pos.X, 0, target.Pos.Z - actor.Pos.Z);
            var dir = Fixed.NormaliseTo(delta, 1000);
            output.Add(new MoveCommand(state.Tick, actorId, new Dir2(dir.X, dir.Z), Fixed.YawBetween(actor.Pos, target.Pos)));
        }
        return output;
    }
}
