using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Encounter;

/// <summary>
/// Closes a melee enemy onto its target so its scripted swing can land (sim/encounter/chase.ts). A living enemy that
/// is not mid-cast and whose script casts a hostile target-shaped ability or a caster-centred damage area has an
/// engage range = the shortest such reach (a target cast's range, an area's radius); if its default target (<see cref="ScriptTarget"/> without an ability) is farther than
/// <c>engage - (actor.ChaseMarginMm ?? 500)</c>, it emits one <c>move</c> straight at that target, facing it. Speed
/// rides on ActorState (combat's move handling), never on the command. With no abilities nothing chases.
/// </summary>
public static class Chase
{
    /// <summary>How far inside its engage range a chaser stops, mm (chase.ts:35).</summary>
    public const long ChaseMarginMm = 500;

    // chase.ts engageReachMm: a hostile (no heal) target-shaped cast reaches its range; a caster-centred damage area
    // (circle or cone with a damage op, ScriptAbility.AreaReachMm) reaches its radius; anything else does not engage.
    private static long? EngageReachMm(ScriptAbility? ability)
    {
        if (ability is null || ability.Heals)
        {
            return null;
        }
        return ability.ShapeKind == "target" ? ability.Range : ability.AreaReachMm;
    }

    /// <summary>chase.ts engageRangeMm: the shortest reach among the script's engaging casts (onDeath skipped).</summary>
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
            if (EngageReachMm(ability) is { } reach && (best is null || reach < best.Value))
            {
                best = reach;
            }
        }
        return best;
    }

    /// <summary>
    /// L5 caster-los (R-SYNERGY-2): the "move until in sight" step. An enemy whose script casts a hostile target-shaped
    /// ability (a caster) and whose default target is alive but out of sight (<paramref name="sight"/> false from the
    /// enemy to the target: the cast would be withheld, <see cref="Combat.Casting.NoSightError"/>) walks at its target even
    /// when already within reach. The move is the chase's straight step, so the layout run routes it onto the A* path
    /// (<see cref="Nav.NavFollow.RouteChase"/>) and the caster stops walking the tick its ray is clear. An actor that is
    /// idle, dead or mid-cast is left alone, and a straight chase move already in <paramref name="commands"/> is kept; a
    /// turn-only move (zero direction) for a blind caster is replaced by the walking one. Integer only.
    /// </summary>
    /// <param name="state">The pre-combat state the chase reads.</param>
    /// <param name="scripts">The enemy scripts.</param>
    /// <param name="abilities">The script view of each ability.</param>
    /// <param name="sight">True when a cast from the first point to the second has sight.</param>
    /// <param name="commands">The encounter's commands.</param>
    /// <returns><paramref name="commands"/> itself when no caster is blind, else the commands with the walking moves.</returns>
    public static IReadOnlyList<Command> SeekSight(
        SimState state,
        IReadOnlyDictionary<int, IReadOnlyList<ScriptStep>> scripts,
        IReadOnlyDictionary<string, ScriptAbility> abilities,
        Func<Vec3, Vec3, bool> sight,
        IReadOnlyList<Command> commands)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(scripts);
        ArgumentNullException.ThrowIfNull(abilities);
        ArgumentNullException.ThrowIfNull(sight);
        ArgumentNullException.ThrowIfNull(commands);
        List<Command>? output = null;
        foreach (var actorId in scripts.Keys.OrderBy(k => k))
        {
            if (!state.Actors.TryGetValue(actorId, out var actor) || !actor.Alive || actor.Idle || actor.CastId is not null ||
                !CastsAtTarget(scripts[actorId], abilities))
            {
                continue;
            }
            var targetId = ScriptTarget.Resolve(state, actor).TargetId;
            if (targetId is not { } tid || !state.Actors.TryGetValue(tid, out var target) || !target.Alive || sight(actor.Pos, target.Pos))
            {
                continue;
            }
            var current = output ?? commands;
            if (current.Any(c => c.ActorId == actorId && c is MoveCommand { Dir: var d } && (d.X != 0 || d.Z != 0)))
            {
                continue;
            }
            var walk = new Vec3(target.Pos.X - actor.Pos.X, 0, target.Pos.Z - actor.Pos.Z);
            if (walk.X == 0 && walk.Z == 0)
            {
                continue;
            }
            var dir = Fixed.NormaliseTo(walk, 1000);
            output = current.Where(c => !(c.ActorId == actorId && c is MoveCommand)).ToList();
            output.Add(new MoveCommand(state.Tick, actorId, new Dir2(dir.X, dir.Z), Fixed.YawBetween(actor.Pos, target.Pos)));
        }
        return output ?? commands;
    }

    // A caster: some non-onDeath step casts a hostile target-shaped ability (the casts the sight rule withholds).
    private static bool CastsAtTarget(IReadOnlyList<ScriptStep> script, IReadOnlyDictionary<string, ScriptAbility> abilities)
    {
        foreach (var step in script)
        {
            if (step.Trigger != StepTrigger.OnDeath && abilities.TryGetValue(step.Cast, out var a) && !a.Heals && a.ShapeKind == "target")
            {
                return true;
            }
        }
        return false;
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
            if (!state.Actors.TryGetValue(actorId, out var actor) || script is null || !actor.Alive || actor.Idle || actor.CastId is not null)
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
