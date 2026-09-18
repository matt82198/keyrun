/**
 * bots/src/kit-priority.ts
 *
 * The tag-based tiering kits.ts's buildClassKit() uses to derive a base damage-rotation
 * priority order. Split out of kits.ts to stay under the repo's <=190 LOC/file cap; no
 * behaviour change.
 *
 * Usage: `import { buildRotationPriority, abilityTags, SETUP_TAG_PREFIX, PAYOFF_TAG } from "./kit-priority.js"`
 */
import type { AbilityDef } from "@content/schema/ability";
import { costAmount } from "./kit-resources.js";

export const SETUP_TAG_PREFIX = "setup:";
export const PAYOFF_TAG = "payoff";

export function abilityTags(def: AbilityDef): readonly string[] {
  return def.tags ?? [];
}

/** Ability "weight" tier: lower sorts first. Cost-paying finishers > Payoffs > Setups > everything else. */
function tier(def: AbilityDef, resource: string): number {
  if (costAmount(def, resource) > 0) return 0;
  if (abilityTags(def).includes(PAYOFF_TAG)) return 1;
  if (abilityTags(def).some((t) => t.startsWith(SETUP_TAG_PREFIX))) return 2;
  return 3;
}

/**
 * Baseline damage-rotation priority: resource-cost finishers first (spend before you cap), then
 * Payoffs, then Setups, then filler -- ties broken by longer cooldown first (use the scarce
 * cooldown before it idles), then ability id for determinism. This is the fallback order
 * pickRotationAbility() cycles when no Window/Payoff-ally override (roles/damage.ts) applies.
 */
export function buildRotationPriority(rotationalDefs: readonly AbilityDef[], resource: string): string[] {
  return [...rotationalDefs]
    .sort((a, b) => {
      const ta = tier(a, resource);
      const tb = tier(b, resource);
      if (ta !== tb) return ta - tb;
      if (a.cooldownTicks !== b.cooldownTicks) return b.cooldownTicks - a.cooldownTicks;
      return a.id < b.id ? -1 : a.id > b.id ? 1 : 0;
    })
    .map((d) => d.id);
}
