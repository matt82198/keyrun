/**
 * Cast lifecycle: startCast (validates gcd, cooldown/charges, cost, range, target validity, and
 * school lockout), completeCast (applies the ability's effects to shape-resolved targets at
 * castEndsTick; cone/line hits are resolved against the caster's *current* facing at completion
 * time, which is how "facing for cones" is enforced), stopcast (cancel, no effects), and
 * interruptCast (cancels an active interruptible cast and locks its school).
 *
 * Interrupt resolution (see rules.ts for the tick-level ordering that makes this hold): an
 * interrupt that lands in a tick strictly before the target's castEndsTick wins, because that
 * earlier tick clears castId before the completion tick is ever reached. An interrupt landing
 * exactly at castEndsTick loses: rules.ts always completes due casts before processing that
 * tick's commands, so by the time the interrupt command runs, completeCast has already fired.
 * Same-tick interrupt-vs-interrupt ties are resolved by command array order.
 *
 * Usage: const err = startCast(state, actor, def, targetId, point, now);
 *        if (!err && def.castTicks === 0) completeCast(state, actor, ctx, emit);
 */
import type { ActorId, ActorState, SimEvent, SimState, Tick, Vec3 } from "./types";
import type { AbilityDef, EffectOp } from "./abilities";
import { abilitySchool } from "./abilities";
import type { EffectDef } from "./effects";
import { applyEffectInstance, removeEffectInstances } from "./effects";
import { removeDispellableEffects } from "./effects-remove";
import { isAbilityReady, startGcd, consumeCharge } from "./cooldowns";
import { applyDamage, applyHeal } from "./damage";
import { addThreat } from "./threat";
import { resolveTargets } from "./targeting";
import { isqrt } from "./movement";

export interface CastContext {
  abilities: Record<string, AbilityDef>;
  effects: Record<string, EffectDef>;
  lockoutTicks: Tick;
}

export function rangeMm(a: ActorState, b: ActorState): number {
  const dx = a.pos.x - b.pos.x;
  const dz = a.pos.z - b.pos.z;
  return isqrt(dx * dx + dz * dz);
}

function lockoutEffectId(school: string): string {
  return `lockout:${school}`;
}

function isSchoolLocked(actor: ActorState, school: string, now: Tick): boolean {
  return actor.effects.some((e) => e.effectId === lockoutEffectId(school) && e.endsTick > now);
}

/** Returns null on success, or an error code string describing why the cast could not start. */
export function startCast(
  state: SimState,
  actor: ActorState,
  def: AbilityDef,
  targetId: ActorId | undefined,
  _point: Vec3 | undefined,
  now: Tick,
): string | null {
  if (!isAbilityReady(actor, def, now)) return "not_ready";
  if (actor.resource < def.cost.amount) return "insufficient_resource";
  if (isSchoolLocked(actor, abilitySchool(def), now)) return "school_locked";
  if (def.shape.kind === "target") {
    if (targetId === undefined) return "target_required";
    const target = state.actors[targetId];
    if (!target || !target.alive) return "invalid_target";
    if (rangeMm(actor, target) > def.range) return "out_of_range";
  }
  actor.resource -= def.cost.amount;
  consumeCharge(actor, def, now);
  startGcd(actor, now, def.gcdTicks);
  actor.castId = def.id;
  actor.castStartTick = now;
  actor.castEndsTick = now + def.castTicks;
  if (targetId !== undefined) actor.targetId = targetId;
  return null;
}

function clearCast(actor: ActorState): void {
  actor.castId = null;
  actor.castStartTick = null;
  actor.castEndsTick = null;
}

export function stopcast(actor: ActorState, emit: (e: SimEvent) => void, tick: Tick): void {
  if (actor.castId === null) return;
  emit({ tick, type: "cast_stopped", actorId: actor.id, abilityId: actor.castId });
  clearCast(actor);
}

export function completeCast(state: SimState, actor: ActorState, ctx: CastContext, emit: (e: SimEvent) => void): void {
  const def = actor.castId ? ctx.abilities[actor.castId] : undefined;
  if (!def) {
    clearCast(actor);
    return;
  }
  for (const target of resolveTargets(state, actor, def)) {
    applyEffects(state, actor, target, def.effects, ctx, emit);
  }
  emit({ tick: state.tick, type: "cast_complete", actorId: actor.id, abilityId: def.id });
  clearCast(actor);
}

function applyEffects(
  state: SimState,
  source: ActorState,
  target: ActorState,
  ops: EffectOp[],
  ctx: CastContext,
  emit: (e: SimEvent) => void,
): void {
  for (const op of ops) {
    if (op.op === "damage") {
      applyDamage(state, target, op.amount, 0, 0, ctx.effects, emit, source.id);
    } else if (op.op === "heal") {
      applyHeal(state, target, op.amount, source.id, emit);
    } else if (op.op === "applyEffect") {
      const effDef = ctx.effects[op.effectId];
      if (effDef) applyEffectInstance(target, effDef, source.id, op.durationTicks, op.stacks, state.tick);
    } else if (op.op === "removeEffect") {
      if (op.effectId === "*") removeDispellableEffects(source, target, ctx.effects);
      else removeEffectInstances(target, op.effectId);
    } else if (op.op === "resource") {
      target.resource = Math.max(0, Math.min(target.maxResource, target.resource + op.delta));
    } else if (op.op === "threat") {
      addThreat(target, source.id, op.amount ?? 0, op.multiplierBp);
    } else if (op.op === "interrupt") {
      interruptCast(state, target, source.id, ctx, emit);
    }
  }
}

/** No-op if the target is not currently casting, or its cast is not interruptible. */
export function interruptCast(
  state: SimState,
  target: ActorState,
  sourceId: ActorId,
  ctx: CastContext,
  emit: (e: SimEvent) => void,
): void {
  if (target.castId === null) return;
  const castDef = ctx.abilities[target.castId];
  if (!castDef || !castDef.interruptible) return;
  const school = abilitySchool(castDef);
  const abilityId = target.castId;
  clearCast(target);
  target.effects.push({
    effectId: lockoutEffectId(school),
    sourceId,
    stacks: 1,
    endsTick: state.tick + ctx.lockoutTicks,
    nextPeriodicTick: null,
  });
  emit({ tick: state.tick, type: "interrupt", sourceId, targetId: target.id, abilityId, school });
}
