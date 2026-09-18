/**
 * bots/src/kit-resources.ts
 *
 * Loop.kind-agnostic resource-economics helpers kits.ts's buildClassKit() uses to classify each
 * rotational ability: what it costs, what it gives back, and whether it heals. Split out of
 * kits.ts to stay under the repo's <=190 LOC/file cap; no behaviour change.
 *
 * Usage: `import { resourceEffectDelta, costAmount, healsOnCast } from "./kit-resources.js"`
 */
import type { AbilityDef } from "@content/schema/ability";

/**
 * Net of every explicit `{op:"resource"}` delta in the ability's base effects (the real
 * sim/combat/abilities.ts contract carries one implicit resource per actor -- no per-op resource
 * name to filter on). Variants (content/schema/ability.ts: a per-stance *replacement* for
 * `effects`, never additive) are deliberately not summed in here -- every current variant pair
 * (Chanter's mend/ward) agrees with its base resource delta anyway, so this stays correct without
 * double-counting.
 */
export function resourceEffectDelta(def: AbilityDef): number {
  let delta = 0;
  for (const op of def.effects) {
    if (op.op === "resource") delta += op.delta;
  }
  return delta;
}

export function costAmount(def: AbilityDef, resource: string): number {
  return def.cost && def.cost.resource === resource ? def.cost.amount : 0;
}

export function healsOnCast(def: AbilityDef): boolean {
  if (def.effects.some((op) => op.op === "heal")) return true;
  return Object.values(def.variants ?? {}).some((variant) => variant.some((op) => op.op === "heal"));
}
