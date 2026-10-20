using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Encounter;

/// <summary>A scripted cast held because its caster was busy when the step fired (sim/encounter/deferred-casts.ts).</summary>
/// <param name="ActorId">The caster.</param>
/// <param name="Step">The step that fired; its cast (and telegraph) is issued once the caster is free.</param>
public sealed record DeferredCast(int ActorId, ScriptStep Step);

/// <summary>
/// STARTCAST-BUSY (sim/encounter/deferred-casts.ts). <see cref="Combat.Casting.StartCast"/> refuses a caster with a cast
/// in flight (BIBLE section 3: a cast bar is never silently replaced). A scripted step that fires while its caster is
/// busy (a cast in flight, or a cast-time cast this same call already issued for it) is held and issued the first
/// tick the caster is free, instead of being sent to combat and dropped, so the boss's core rule still happens
/// (BIBLE section 9: the unarmed hollowWarden.lock fires on the slam's tick and lands when the slam ends). Held casts
/// go first, oldest first; a held step is held once; it is dropped if its caster dies or leaves the scripts map; and
/// it re-draws its telegraph on the tick it is issued (the onDeath convention).
/// </summary>
public static class CastDeferral
{
    /// <summary>deferred-casts.ts busyAt: rules complete a cast whose end tick is now BEFORE running commands.</summary>
    public static bool BusyAt(ActorState actor, int tick)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return actor.CastId is not null && actor.CastEndsTick != tick;
    }

    /// <summary>deferred-casts.ts leavesCastInFlight: only a known ability with a cast time keeps its caster busy.</summary>
    public static bool LeavesCastInFlight(string cast, IReadOnlyDictionary<string, ScriptAbility> abilities)
    {
        ArgumentNullException.ThrowIfNull(abilities);
        return abilities.TryGetValue(cast, out var a) && a.CastTicks > 0;
    }

    /// <summary>
    /// deferred-casts.ts casterGate: issues (or re-holds into <paramref name="nextHeld"/>) this caster's held casts now
    /// and returns <c>fire(step)</c> for the tick's own steps. <paramref name="issue"/> builds and adds the cast command
    /// (and, when its second argument is true, the step's telegraph) and returns false when targeting blocked it.
    /// </summary>
    public static Action<ScriptStep> CasterGate(
        int actorId,
        ActorState actor,
        int tick,
        IReadOnlyDictionary<string, ScriptAbility> abilities,
        IReadOnlyList<DeferredCast> held,
        List<DeferredCast> nextHeld,
        Func<ScriptStep, bool, bool> issue)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(held);
        ArgumentNullException.ThrowIfNull(nextHeld);
        ArgumentNullException.ThrowIfNull(issue);
        var claimed = actor.Alive && BusyAt(actor, tick);
        void Fire(ScriptStep step, bool redraw)
        {
            if (claimed)
            {
                if (!nextHeld.Any(d => d.ActorId == actorId && d.Step.Cast == step.Cast))
                {
                    nextHeld.Add(new DeferredCast(actorId, step));
                }
                return;
            }
            if (issue(step, redraw))
            {
                claimed = LeavesCastInFlight(step.Cast, abilities);
            }
        }
        if (actor.Alive)
        {
            foreach (var h in held)
            {
                if (h.ActorId == actorId)
                {
                    Fire(h.Step, true);
                }
            }
        }
        return step => Fire(step, false);
    }
}
