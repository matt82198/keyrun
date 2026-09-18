/**
 * EffectDef schema, effect instance lifecycle (apply/refresh/stack, expiry, periodic ticks),
 * and modifier aggregation as basis points on damageDone/damageTaken/haste/movement. `absorb`
 * is a schema extension beyond the base modifier list (a flat health amount per stack) needed to
 * implement the damage pipeline's absorb stage; see damage.ts.
 * Usage: applyEffectInstance(actor, def, sourceId, durationTicks, stacks, now);
 *        for (const inst of duePeriodicInstances(actor, now)) { ...; advancePeriodic(inst, def, now); }
 */
import type { ActorId, ActorState, EffectInstance, Tick } from "./types";
import type { EffectOp } from "./abilities";

export interface EffectModifiers {
  damageDoneBp?: number;
  damageTakenBp?: number;
  hasteBp?: number;
  movementBp?: number;
  absorb?: number;
}

export interface EffectDef {
  id: string;
  maxStacks: number;
  dispellableTag: string | null;
  modifiers: EffectModifiers;
  periodic: { everyTicks: Tick; op: EffectOp } | null;
}

export const BP_DENOM = 10000;

export function applyEffectInstance(
  actor: ActorState,
  def: EffectDef,
  sourceId: ActorId,
  durationTicks: Tick,
  stacks: number,
  now: Tick,
): void {
  const existing = actor.effects.find((e) => e.effectId === def.id && e.sourceId === sourceId);
  if (existing) {
    existing.stacks = Math.min(def.maxStacks, existing.stacks + stacks);
    existing.endsTick = now + durationTicks;
    if (def.periodic && existing.nextPeriodicTick === null) existing.nextPeriodicTick = now + def.periodic.everyTicks;
    return;
  }
  actor.effects.push({
    effectId: def.id,
    sourceId,
    stacks: Math.min(def.maxStacks, Math.max(1, stacks)),
    endsTick: now + durationTicks,
    nextPeriodicTick: def.periodic ? now + def.periodic.everyTicks : null,
  });
}

export function removeEffectInstances(actor: ActorState, effectId: string): void {
  actor.effects = actor.effects.filter((e) => e.effectId !== effectId);
}

export function expireEffects(actor: ActorState, now: Tick): void {
  actor.effects = actor.effects.filter((e) => e.endsTick > now);
}

export function duePeriodicInstances(actor: ActorState, now: Tick): EffectInstance[] {
  return actor.effects.filter((e) => e.nextPeriodicTick !== null && e.nextPeriodicTick <= now);
}

export function advancePeriodic(instance: EffectInstance, def: EffectDef, now: Tick): void {
  if (!def.periodic) return;
  instance.nextPeriodicTick = now + def.periodic.everyTicks;
}

export interface ModifierTotals {
  damageDoneBp: number;
  damageTakenBp: number;
  hasteBp: number;
  movementBp: number;
}

export function aggregateModifiers(actor: ActorState, defs: Record<string, EffectDef>): ModifierTotals {
  const totals: ModifierTotals = { damageDoneBp: 0, damageTakenBp: 0, hasteBp: 0, movementBp: 0 };
  for (const inst of actor.effects) {
    const def = defs[inst.effectId];
    if (!def) continue;
    const m = def.modifiers;
    totals.damageDoneBp += (m.damageDoneBp ?? 0) * inst.stacks;
    totals.damageTakenBp += (m.damageTakenBp ?? 0) * inst.stacks;
    totals.hasteBp += (m.hasteBp ?? 0) * inst.stacks;
    totals.movementBp += (m.movementBp ?? 0) * inst.stacks;
  }
  return totals;
}

export function absorbPool(actor: ActorState, defs: Record<string, EffectDef>): number {
  let total = 0;
  for (const inst of actor.effects) {
    const def = defs[inst.effectId];
    if (!def || !def.modifiers.absorb) continue;
    total += def.modifiers.absorb * inst.stacks;
  }
  return total;
}

/** Consumes up to `amount` of absorb from the actor's absorb-bearing effects; returns amount absorbed. */
export function consumeAbsorb(actor: ActorState, defs: Record<string, EffectDef>, amount: number): number {
  let remaining = amount;
  for (const inst of actor.effects) {
    if (remaining <= 0) break;
    const def = defs[inst.effectId];
    const perStack = def?.modifiers.absorb ?? 0;
    if (perStack <= 0) continue;
    const pool = perStack * inst.stacks;
    const used = Math.min(pool, remaining);
    remaining -= used;
    inst.stacks -= Math.ceil(used / perStack);
  }
  actor.effects = actor.effects.filter((e) => e.stacks > 0);
  return amount - remaining;
}
