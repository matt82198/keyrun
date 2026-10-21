using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Runs;

/// <summary>
/// STUN-STOPS-CAST (finding f-8b3fff18; BIBLE s9.3 "Stops as well as kicks"): a stun, freeze or silence that lands on a
/// caster during an interruptible cast ends the cast, as a Mythic+ stop does. NEW behaviour with no TS oracle: it runs
/// only when <see cref="RunContent.StopEffectIds"/> is set (a host sets it; every oracle run leaves it null).
/// <list type="bullet">
/// <item>A stop "lands" on a tick when the actor ends the tick holding a stop instance it did not hold before the tick, or
/// one whose end tick moved later (a refresh). This covers every way an effect is applied: ability and combo ops in
/// combat, and the Break bar's onEmpty patch the run root applies after combat.</item>
/// <item>A silence (any stop that is not a stun) needs an interruptible cast (s9.3 "during an interruptible cast").
/// A stun (<see cref="RunContent.StunEffectIds"/>) stops every cast of a stunnable actor, the uninterruptible ones
/// included (finding f-c63b43bf, ruling "defer to WoW": uninterruptible means kick-immune, not stun-immune). An actor
/// whose class is in <see cref="RunContent.StunImmuneClassIds"/> (the boss) is not stopped by a stun at all.</item>
/// <item>The stop clears the cast and emits <c>{tick, type:"interrupt", sourceId, targetId, abilityId, school, stop}</c>:
/// it counts as an interrupt for Echo, and <c>stop</c> names the effect so nothing reads it as a kick. It applies no
/// school lockout (a stop is not a kick).</item>
/// </list>
/// The run root runs it before <see cref="Keyrun.Sim.Encounter.Telegraphs.CancelUnlanded"/>, so the stopped cast's floor telegraph
/// is cancelled on the same tick and never resolves.
/// </summary>
public static class ControlStops
{
    /// <summary>
    /// Stops every cast in <paramref name="after"/> on which a qualifying stop effect landed during the tick that led
    /// from <paramref name="before"/> to it (any stop on an interruptible cast; a stun on any cast of a stunnable actor).
    /// Actors run ascending by id, and each actor's instances in insertion order.
    /// </summary>
    /// <param name="before">The state at the start of the tick (not modified).</param>
    /// <param name="after">The state after combat and the Break-bar patches (not modified).</param>
    /// <param name="stopEffectIds">The stop effect ids, or null for none (the state is then returned as is).</param>
    /// <param name="abilities">Abilities by id.</param>
    /// <param name="tick">The tick just stepped (the events' tick).</param>
    /// <param name="emit">Event sink.</param>
    /// <param name="stunRules">Which stops are stuns, who is stun-immune, and this tick's Twins-bar staggered actors;
    /// null = no stop is a stun (the s9.3 interruptible-only rule).</param>
    /// <returns><paramref name="after"/> itself when nothing stopped, else a clone with the stopped casts cleared.</returns>
    public static SimState Apply(
        SimState before,
        SimState after,
        IReadOnlyList<string>? stopEffectIds,
        IReadOnlyDictionary<string, AbilityDef> abilities,
        int tick,
        Action<SimEvent> emit,
        StunRules? stunRules = null)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(abilities);
        ArgumentNullException.ThrowIfNull(emit);
        if (stopEffectIds is null || stopEffectIds.Count == 0)
        {
            return after;
        }
        var stops = new HashSet<string>(stopEffectIds, StringComparer.Ordinal);
        var stuns = new HashSet<string>(stunRules?.StunEffectIds ?? Array.Empty<string>(), StringComparer.Ordinal);
        stuns.IntersectWith(stops);
        var immune = new HashSet<string>(stunRules?.StunImmuneClassIds ?? Array.Empty<string>(), StringComparer.Ordinal);
        var staggered = new HashSet<int>(stunRules?.StaggeredActorIds ?? Array.Empty<int>());
        SimState? next = null;
        foreach (var id in after.Actors.Keys.ToList())
        {
            var actor = after.Actors[id];
            if (actor.CastId is not { } castId || !abilities.TryGetValue(castId, out var def))
            {
                continue;
            }
            // f-c63b43bf: a stun stops every cast of a stunnable actor (uninterruptible = kick-immune only); a stun-immune
            // actor ignores stuns; any other stop (a silence) needs an interruptible cast. A Twins-bar staggered actor's
            // uninterruptible cast is left to the #344 stagger path (RunWiring.StopCasts), which ends it this same tick.
            var stunnable = !immune.Contains(actor.ClassId);
            bool Stops(EffectInstance e) => stuns.Contains(e.EffectId)
                ? stunnable && (def.Interruptible || !staggered.Contains(id))
                : def.Interruptible;
            before.Actors.TryGetValue(id, out var prior);
            var landed = actor.Effects.FirstOrDefault(e => stops.Contains(e.EffectId) && !HeldBefore(prior, e) && Stops(e));
            if (landed is null)
            {
                continue;
            }
            next ??= after.Clone();
            var target = next.Actors[id];
            target.CastId = null;
            target.CastStartTick = null;
            target.CastEndsTick = null;
            target.CastIsOverheat = false;
            emit(new SimEvent(
                tick,
                "interrupt",
                ("sourceId", JsValue.Int(landed.SourceId)),
                ("targetId", JsValue.Int(id)),
                ("abilityId", JsValue.Str(castId)),
                ("school", JsValue.Str(Abilities.AbilitySchool(def))),
                ("stop", JsValue.Str(landed.EffectId))));
        }
        return next ?? after;
    }

    /// <summary>
    /// STUN-STOPS-EVERY-CAST (f-c63b43bf) inputs to <see cref="Apply"/>.
    /// </summary>
    /// <param name="StunEffectIds">The stops that are stuns (<see cref="RunContent.StunEffectIds"/>), or null for none.</param>
    /// <param name="StunImmuneClassIds">Stun-immune actor class ids (<see cref="RunContent.StunImmuneClassIds"/>), or null.</param>
    /// <param name="StaggeredActorIds">This tick's armed-boss Twins-bar staggered actors (EncounterStep.Staggered), whose
    /// uninterruptible casts the stagger path ends, or null.</param>
    public sealed record StunRules(
        IReadOnlyList<string>? StunEffectIds,
        IReadOnlyList<string>? StunImmuneClassIds,
        IReadOnlyList<int>? StaggeredActorIds);

    /// <summary>True when <paramref name="prior"/> already held this stop from the same source, ending no earlier.</summary>
    private static bool HeldBefore(ActorState? prior, EffectInstance instance) =>
        prior is not null && prior.Effects.Any(e =>
            e.EffectId == instance.EffectId && e.SourceId == instance.SourceId && e.EndsTick >= instance.EndsTick);
}
