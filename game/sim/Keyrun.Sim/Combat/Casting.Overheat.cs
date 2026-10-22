using System;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Combat;

/// <summary>
/// BIBLE section 5 (Cinder): "Heat rises per cast. At 100 the next cast is a free Overheat that dumps Heat to 0 and
/// applies Chill (slower casts for 3 s)." Heat is the class's loop resource; each ability's own resource ops raise it.
/// </summary>
/// <param name="Threshold">Heat at or above which the next cast is the Overheat (the class loop max, 100).</param>
/// <param name="ChillEffectId">The effect the Overheat applies to the caster (content effects "chill", hasteBp -3000).</param>
/// <param name="ChillTicks">Chill duration, ticks (60 = 3 s at 20 Hz).</param>
public sealed record OverheatRule(long Threshold, string ChillEffectId, int ChillTicks);

public static partial class Casting
{
    private static OverheatRule? OverheatRuleOf(CastContext? ctx, ActorState actor) =>
        ctx?.Overheat is { } rules && rules.TryGetValue(actor.ClassId, out var rule) ? rule : null;

    /// <summary>
    /// The Overheat's completion (BIBLE section 5): Heat dumps to 0 (after the cast's own resource ops, so its Heat
    /// gain is dumped too), the caster gains its rule's Chill for ChillTicks, and an "overheat" event is emitted.
    /// </summary>
    private static void CompleteOverheat(SimState state, ActorState actor, AbilityDef def, CastContext ctx, Action<SimEvent> emit)
    {
        var rule = OverheatRuleOf(ctx, actor);
        if (rule is null)
        {
            return;
        }
        actor.Resource = 0;
        if (ctx.Effects.TryGetValue(rule.ChillEffectId, out var chill))
        {
            Effects.ApplyEffectInstance(actor, chill, actor.Id, rule.ChillTicks, 1, state.Tick);
        }
        emit(new SimEvent(state.Tick, "overheat", ("actorId", JsValue.Int(actor.Id)), ("abilityId", JsValue.Str(def.Id))));
    }
}
