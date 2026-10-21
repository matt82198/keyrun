/**
 * bots/src/kit-charges.ts
 *
 * BANKED-CHARGE (ruling R-PILE-7c, finding f-f44791bc): a class kit's slotted multi-charge abilities, so bot readiness
 * (roles/shared.ts hasCharge) counts a banked charge exactly as the sim's chargesAvailable does
 * (sim/combat/cooldowns.ts). C# twin: game/bots/Keyrun.Bots/Kits.cs ChargedIds.
 *
 * Usage: `import { chargedIds, type AbilityCharges } from "./kit-charges.js"`
 */
import type { AbilityDef } from "@content/schema/ability";
import type { ClassDef } from "@content/schema/class";

/** A multi-charge ability's charge count and per-charge cooldown (the sim's AbilityDef charges / cooldownTicks). */
export interface AbilityCharges {
  readonly charges: number;
  readonly cooldownTicks: number;
}

const SLOTS = ["rot1", "rot2", "rot3", "rot4", "rot5", "interrupt", "defensive", "utility", "movement"] as const;

/** The slotted abilities (rot1-5, interrupt, defensive, utility, movement) with more than one charge. */
export function chargedIds(cls: ClassDef, abilities: ReadonlyMap<string, AbilityDef>): Record<string, AbilityCharges> {
  const map: Record<string, AbilityCharges> = {};
  for (const slot of SLOTS) {
    const id = cls.abilities[slot];
    const def = abilities.get(id);
    if (def && def.charges > 1 && !(id in map)) map[id] = { charges: def.charges, cooldownTicks: def.cooldownTicks };
  }
  return map;
}
