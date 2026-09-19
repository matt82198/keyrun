/**
 * Integer damage pipeline: base -> Pact clause weighting (bp) -> source modifiers (bp) -> target
 * modifiers (bp) -> absorb -> health, floored at 0. Emits a "damage" event always, and a "death"
 * event the first time health reaches 0. All amounts are integers; basis-point modifiers are
 * applied as amount*(10000+bp)/10000, truncated toward zero (integer division), never negative.
 *
 * PACT (design/BIBLE.md sec 8, sim/encounter/clauses.ts's `applyPactDamageBp`): every call folds
 * in the active run's Pact weighting via sim/combat/clause-hooks.ts's module-scoped
 * `getActiveEncounterRules()`/`isComboActive()` (see that file's docstring for why -- rules.ts is
 * outside this lane's edit scope) -- a no-op when no run is active or Pact is not the active
 * Clause, so every existing call site (and every golden replay) is byte-identical unless Pact is
 * actually picked. `isComboActive()` is true only while payoff.ts's `runAsCombo` brackets a
 * Payoff's combo effects, so a combo hit gets `comboDamageBp` and every other hit (a normal cast,
 * a periodic DoT tick) gets `soloDamageBp`.
 *
 * Usage: applyDamage(state, target, 100, sourceModBp, targetModBp, effectDefs, emit, sourceId);
 */
import type { ActorId, ActorState, SimEvent, Tick } from "./types";
import type { EffectDef } from "./effects";
import { absorbPool, consumeAbsorb, BP_DENOM } from "./effects";
import { applyPactDamageBp } from "../encounter/clauses";
import { getActiveEncounterRules, isComboActive } from "./clause-hooks";

export function applyModifiers(base: number, sourceModBp: number, targetModBp: number): number {
  const afterSource = Math.trunc((base * (BP_DENOM + sourceModBp)) / BP_DENOM);
  const afterTarget = Math.trunc((afterSource * (BP_DENOM + targetModBp)) / BP_DENOM);
  return Math.max(0, afterTarget);
}

export interface DamageResult {
  rawAmount: number;
  modifiedAmount: number;
  absorbed: number;
  healthDamage: number;
  died: boolean;
}

export function applyDamage(
  state: { tick: Tick },
  target: ActorState,
  rawAmount: number,
  sourceModBp: number,
  targetModBp: number,
  defs: Record<string, EffectDef>,
  emit: (e: SimEvent) => void,
  sourceId: ActorId,
): DamageResult {
  const rules = getActiveEncounterRules();
  const pactAmount = rules ? applyPactDamageBp(rawAmount, isComboActive(), rules) : rawAmount;
  const modifiedAmount = applyModifiers(pactAmount, sourceModBp, targetModBp);
  const pool = absorbPool(target, defs);
  const absorbed = pool > 0 ? consumeAbsorb(target, defs, Math.min(pool, modifiedAmount)) : 0;
  const afterAbsorb = Math.max(0, modifiedAmount - absorbed);
  const healthDamage = Math.min(target.health, afterAbsorb);
  const wasAlive = target.alive;
  target.health = Math.max(0, target.health - healthDamage);
  const died = wasAlive && target.health === 0;
  if (died) target.alive = false;
  emit({
    tick: state.tick,
    type: "damage",
    sourceId,
    targetId: target.id,
    rawAmount,
    modifiedAmount,
    absorbed,
    healthDamage,
  });
  if (died) emit({ tick: state.tick, type: "death", actorId: target.id, sourceId });
  return { rawAmount, modifiedAmount, absorbed, healthDamage, died };
}

export function applyHeal(
  state: { tick: Tick },
  target: ActorState,
  amount: number,
  sourceId: ActorId,
  emit: (e: SimEvent) => void,
): number {
  const applied = Math.max(0, Math.min(amount, target.maxHealth - target.health));
  target.health += applied;
  emit({ tick: state.tick, type: "heal", sourceId, targetId: target.id, amount: applied });
  return applied;
}
