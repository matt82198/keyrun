/**
 * The single place a damage op picks up its basis-point modifiers, and the single place a
 * "resource" op is credited. Both used to be inlined at three call sites (casting.ts
 * completeCast, combos.ts applyComboEffects, rules.ts runPeriodicOp) with applyDamage's modifier
 * arguments hard-coded to 0,0 -- which made every damageTakenBp/damageDoneBp effect in content
 * (Brace, Bulwark, Vigil, Aegis, Ashen Skin, Ward Shield, the Lock's shield) inert -- and with
 * the resource delta applied to the op's TARGET instead of its caster.
 *
 * Modifier source: aggregateModifiers (effects.ts), the same totals movement reads for
 * movementBp. The attacker's damageDoneBp is applied when the attacker still exists in state
 * (a DoT whose source has been removed falls back to 0); the target's damageTakenBp always is.
 *
 * Resource recipient: EffectOp's "resource" variant (abilities.ts) carries no target field, so
 * the actor that performed the cast (or the combo payoff) is the only recipient.
 *
 * Usage: dealDamage(state, source, target, 70, effectDefs, emit, source.id);
 *        creditResource(caster, op.delta);
 */
import type { ActorId, ActorState, SimEvent, Tick } from "./types";
import type { EffectDef } from "./effects";
import { aggregateModifiers } from "./effects";
import { applyDamage, type DamageResult } from "./damage";

export function dealDamage(
  state: { tick: Tick },
  source: ActorState | undefined,
  target: ActorState,
  rawAmount: number,
  defs: Record<string, EffectDef>,
  emit: (e: SimEvent) => void,
  sourceId: ActorId,
): DamageResult {
  const sourceModBp = source ? aggregateModifiers(source, defs).damageDoneBp : 0;
  const targetModBp = aggregateModifiers(target, defs).damageTakenBp;
  return applyDamage(state, target, rawAmount, sourceModBp, targetModBp, defs, emit, sourceId);
}

/** Adds `delta` to the actor's resource, clamped to [0, maxResource]. */
export function creditResource(actor: ActorState, delta: number): void {
  actor.resource = Math.max(0, Math.min(actor.maxResource, actor.resource + delta));
}
