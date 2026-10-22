using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Combat;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Encounter;

/// <summary>R-BOSS-4: which cast lays the mark, which effect it is, and how long a stack lasts (refreshed on each hit).</summary>
/// <param name="AbilityId">The boss's Cleave ability id.</param>
/// <param name="EffectId">The stacking effect (its damageTakenBp is per stack, its maxStacks the cap).</param>
/// <param name="DurationTicks">How long the mark lasts after the latest hit, ticks.</param>
public sealed record CleaveMarkRule(string AbilityId, string EffectId, int DurationTicks);

/// <summary>
/// R-BOSS-4 Cleaved (ruling on #573; BIBLE test 1, hard by what enemies do): a Cleave that lands on a seat other than the
/// cast's own target was not answered (the tank faces it away and nobody else stands in the cone), so each such seat gains
/// one stack of the rule's effect from the caster (more damage taken per stack, the duration refreshed on each hit,
/// <see cref="Effects.ApplyEffectInstance"/>) and <c>{tick, type:"cleaved", actorId, sourceId, stacks}</c> fires. A
/// second unanswered Cleave inside the duration therefore hits harder than a full health bar, so standing in it escalates
/// past healing. The cast's target (the tank) never gains a stack. Integers only; with no rule nothing happens.
/// </summary>
public static class CleaveMark
{
    /// <summary>
    /// Lays the mark for this tick's Cleaves, in place on <paramref name="after"/> (the run root owns this freshly stepped
    /// state): for each <c>cast_complete</c> of the rule's ability in <paramref name="events"/>, every same-tick damage event
    /// from that caster on a living player who is not the cast's target in <paramref name="before"/>.
    /// </summary>
    /// <param name="before">The pre-combat state (the cast's target).</param>
    /// <param name="after">The post-combat state.</param>
    /// <param name="events">This tick's combat events.</param>
    /// <param name="rule">The rule.</param>
    /// <param name="effects">The run's effect definitions (must hold the rule's effect).</param>
    /// <param name="emit">Event sink.</param>
    public static void Apply(SimState before, SimState after, IReadOnlyList<SimEvent> events, CleaveMarkRule rule, IReadOnlyDictionary<string, EffectDef> effects, Action<SimEvent> emit)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(effects);
        ArgumentNullException.ThrowIfNull(emit);
        if (!effects.TryGetValue(rule.EffectId, out var def))
        {
            throw new ArgumentException($"cleave mark effect \"{rule.EffectId}\" is not a run effect", nameof(effects));
        }
        foreach (var cast in events.Where(e => e.Type == "cast_complete" && e.Get("abilityId") is JsString { Value: var id } && id == rule.AbilityId))
        {
            if (cast.Get("actorId") is not JsInt { Value: var casterId } || !before.Actors.TryGetValue((int)casterId, out var caster))
            {
                continue;
            }
            var target = caster.CastTargetId ?? caster.TargetId;
            var hit = events
                .Where(e => e.Type == "damage" && e.Tick == cast.Tick && e.Get("sourceId") is JsInt { Value: var s } && s == casterId)
                .Select(e => e.Get("targetId") is JsInt { Value: var t } ? (int)t : -1)
                .Where(t => t != target)
                .Distinct()
                .OrderBy(t => t);
            foreach (var seatId in hit)
            {
                if (!after.Actors.TryGetValue(seatId, out var seat) || seat.Kind != ActorKind.Player || !seat.Alive)
                {
                    continue;
                }
                Effects.ApplyEffectInstance(seat, def, caster.Id, rule.DurationTicks, 1, after.Tick);
                var stacks = seat.Effects.First(e => e.EffectId == def.Id && e.SourceId == caster.Id).Stacks;
                emit(new SimEvent(after.Tick, "cleaved", ("actorId", JsValue.Int(seat.Id)), ("sourceId", JsValue.Int(caster.Id)), ("stacks", JsValue.Int(stacks))));
            }
        }
    }
}
