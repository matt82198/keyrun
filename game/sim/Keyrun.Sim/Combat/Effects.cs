using System;
using System.Collections.Generic;
using System.Linq;
using Keyrun.Sim.Core;

namespace Keyrun.Sim.Combat;

/// <summary>Summed bp modifiers over an actor's effects (sim/combat/effects.ts:90-95 ModifierTotals).</summary>
/// <param name="DamageDoneBp">Damage done change.</param>
/// <param name="DamageTakenBp">Damage taken change.</param>
/// <param name="HasteBp">Haste change.</param>
/// <param name="MovementBp">Movement change.</param>
public readonly record struct ModifierTotals(long DamageDoneBp, long DamageTakenBp, long HasteBp, long MovementBp);

/// <summary>
/// Port of sim/combat/effects.ts: effect instance lifecycle (apply/refresh/stack, expiry, periodic ticks),
/// modifier aggregation in basis points, and the absorb pool the damage pipeline consumes. Instances keep
/// insertion order, which every iteration below relies on for determinism.
/// </summary>
public static class Effects
{
    /// <summary>Basis-point denominator (sim/combat/effects.ts:31 BP_DENOM).</summary>
    public const long BpDenom = 10000;

    /// <summary>
    /// sim/combat/effects.ts:33-55 applyEffectInstance. The same effect from the same source stacks (capped at
    /// maxStacks) and refreshes its end tick, arming a missing periodic timer (:41-46); otherwise a new instance
    /// gets <c>min(maxStacks, max(1, stacks))</c> stacks (:51).
    /// <para>
    /// C#-only rule (finding f-0fb803dc, no TS counterpart; sim/ is frozen, STATE.md D12): an application whose
    /// duration does not outlast its first periodic tick (<c>durationTicks &lt;= periodic.everyTicks</c>) is a delayed
    /// one-shot event, e.g. Encore's chorusEcho, which heals once at its end. Refreshing such an instance can only lose a
    /// pending event (the old timer is kept and the new end tick passes before the next tick), so each application gets
    /// its own instance and fires on its own timer, as a WoW delayed heal echo does. No other content effect is applied
    /// this way (every other periodic lasts at least two of its intervals), so oracle runs are unchanged.
    /// </para>
    /// </summary>
    /// <param name="actor">Holder.</param>
    /// <param name="def">Effect definition.</param>
    /// <param name="sourceId">Applier.</param>
    /// <param name="durationTicks">Duration.</param>
    /// <param name="stacks">Stacks to add.</param>
    /// <param name="now">Current tick.</param>
    public static void ApplyEffectInstance(ActorState actor, EffectDef def, int sourceId, int durationTicks, long stacks, int now)
    {
        var delayedEvent = def.Periodic is not null && durationTicks <= def.Periodic.EveryTicks;
        var existing = delayedEvent ? null : actor.Effects.Find(e => e.EffectId == def.Id && e.SourceId == sourceId);
        if (existing is not null)
        {
            existing.Stacks = Math.Min(def.MaxStacks, existing.Stacks + stacks);
            existing.EndsTick = now + durationTicks;
            if (def.Periodic is not null && existing.NextPeriodicTick is null)
            {
                existing.NextPeriodicTick = now + def.Periodic.EveryTicks;
            }
            return;
        }
        actor.Effects.Add(new EffectInstance
        {
            EffectId = def.Id,
            SourceId = sourceId,
            Stacks = Math.Min(def.MaxStacks, Math.Max(1, stacks)),
            EndsTick = now + durationTicks,
            NextPeriodicTick = def.Periodic is not null ? now + def.Periodic.EveryTicks : null,
        });
    }

    /// <summary>sim/combat/effects.ts:57-59 removeEffectInstances: drops every instance of <paramref name="effectId"/>.</summary>
    /// <param name="actor">Holder.</param>
    /// <param name="effectId">Effect id (literal, not "*").</param>
    public static void RemoveEffectInstances(ActorState actor, string effectId) =>
        actor.Effects = actor.Effects.Where(e => e.EffectId != effectId).ToList();

    /// <summary>
    /// sim/combat/effects.ts:68-70 pushRawEffectInstance: appends a pre-built instance verbatim (windows.ts
    /// bookkeeping with synthetic ids no def matches, so aggregation and periodic ticks ignore it).
    /// </summary>
    /// <param name="actor">Holder.</param>
    /// <param name="instance">The instance to append.</param>
    public static void PushRawEffectInstance(ActorState actor, EffectInstance instance) => actor.Effects.Add(instance);

    /// <summary>sim/combat/effects.ts:73-75 removeEffectInstanceWhere.</summary>
    /// <param name="actor">Holder.</param>
    /// <param name="pred">Instances matching it are removed.</param>
    public static void RemoveEffectInstanceWhere(ActorState actor, Predicate<EffectInstance> pred) =>
        actor.Effects = actor.Effects.Where(e => !pred(e)).ToList();

    /// <summary>sim/combat/effects.ts:77-79 expireEffects: keeps instances with <c>endsTick &gt; now</c>.</summary>
    /// <param name="actor">Holder.</param>
    /// <param name="now">Current tick.</param>
    public static void ExpireEffects(ActorState actor, int now) =>
        actor.Effects = actor.Effects.Where(e => e.EndsTick > now).ToList();

    /// <summary>sim/combat/effects.ts:81-83 duePeriodicInstances: instances whose next periodic tick has arrived (live references).</summary>
    /// <param name="actor">Holder.</param>
    /// <param name="now">Current tick.</param>
    /// <returns>The due instances, in insertion order.</returns>
    public static List<EffectInstance> DuePeriodicInstances(ActorState actor, int now) =>
        actor.Effects.Where(e => e.NextPeriodicTick is not null && e.NextPeriodicTick.Value <= now).ToList();

    /// <summary>sim/combat/effects.ts:85-88 advancePeriodic: next tick = now + everyTicks (no-op without a periodic).</summary>
    /// <param name="instance">The instance that ticked.</param>
    /// <param name="def">Its definition.</param>
    /// <param name="now">Current tick.</param>
    public static void AdvancePeriodic(EffectInstance instance, EffectDef def, int now)
    {
        if (def.Periodic is null)
        {
            return;
        }
        instance.NextPeriodicTick = now + def.Periodic.EveryTicks;
    }

    /// <summary>sim/combat/effects.ts:97-109 aggregateModifiers: per-stack bp summed over every instance with a def.</summary>
    /// <param name="actor">Holder.</param>
    /// <param name="defs">Effect definitions by id.</param>
    /// <returns>The totals.</returns>
    public static ModifierTotals AggregateModifiers(ActorState actor, IReadOnlyDictionary<string, EffectDef> defs)
    {
        long done = 0, taken = 0, haste = 0, movement = 0;
        foreach (var inst in actor.Effects)
        {
            if (!defs.TryGetValue(inst.EffectId, out var def))
            {
                continue;
            }
            var m = def.Modifiers;
            done += (m.DamageDoneBp ?? 0) * inst.Stacks;
            taken += (m.DamageTakenBp ?? 0) * inst.Stacks;
            haste += (m.HasteBp ?? 0) * inst.Stacks;
            movement += (m.MovementBp ?? 0) * inst.Stacks;
        }
        return new ModifierTotals(done, taken, haste, movement);
    }

    /// <summary>
    /// sim/combat/effects.ts:111-119 absorbPool: total absorb over instances. The TS skips a def whose absorb is
    /// absent or 0 (<c>!def.modifiers.absorb</c>, :115), so a negative absorb is still counted, as here.
    /// </summary>
    /// <param name="actor">Holder.</param>
    /// <param name="defs">Effect definitions by id.</param>
    /// <returns>The pool.</returns>
    public static long AbsorbPool(ActorState actor, IReadOnlyDictionary<string, EffectDef> defs)
    {
        long total = 0;
        foreach (var inst in actor.Effects)
        {
            if (!defs.TryGetValue(inst.EffectId, out var def) || (def.Modifiers.Absorb ?? 0) == 0)
            {
                continue;
            }
            total += def.Modifiers.Absorb!.Value * inst.Stacks;
        }
        return total;
    }

    /// <summary>
    /// sim/combat/effects.ts:122-136 consumeAbsorb: drains absorb-bearing instances in insertion order, spending
    /// <c>ceil(used / perStack)</c> whole stacks per instance (:132, CeilDiv), then drops instances at 0 stacks.
    /// </summary>
    /// <param name="actor">Holder.</param>
    /// <param name="defs">Effect definitions by id.</param>
    /// <param name="amount">Requested amount.</param>
    /// <returns>The amount absorbed.</returns>
    public static long ConsumeAbsorb(ActorState actor, IReadOnlyDictionary<string, EffectDef> defs, long amount)
    {
        var remaining = amount;
        foreach (var inst in actor.Effects)
        {
            if (remaining <= 0)
            {
                break;
            }
            var perStack = defs.TryGetValue(inst.EffectId, out var def) ? def.Modifiers.Absorb ?? 0 : 0;
            if (perStack <= 0)
            {
                continue;
            }
            var pool = perStack * inst.Stacks;
            var used = Math.Min(pool, remaining);
            remaining -= used;
            inst.Stacks -= IntMath.CeilDiv(used, perStack);
        }
        actor.Effects = actor.Effects.Where(e => e.Stacks > 0).ToList();
        return amount - remaining;
    }
}
