/**
 * content/apply-types.ts
 * Shared ability-slot layout, result/choice types, and small tag predicates for
 * content/apply.ts's talent+facet loadout merge. Split out of apply.ts (and its apply-patch.ts /
 * apply-describe.ts siblings) so every file stays under the sim/CLAUDE.md 190 LOC cap.
 */
import type { AbilityDef } from "./schema/ability";
import type { ClassDef } from "./schema/class";

export const ROTATIONAL_SLOTS = ["rot1", "rot2", "rot3", "rot4", "rot5"] as const;
export const ALL_SLOTS = [...ROTATIONAL_SLOTS, "interrupt", "defensive", "utility", "movement"] as const;

/** `talentChoices` maps a forked ability's id to the chosen `TalentOption.id`. One per ability. */
export type TalentChoices = Record<string, string>;

export interface LoadoutResult {
  /** Every ability id the class uses (rotational + interrupt/defensive/utility/movement). */
  abilities: Record<string, AbilityDef>;
  /** One line per ability that ended up different from its base definition. */
  log: string[];
}

export function slotAbilityMap(classDef: ClassDef): Record<string, string> {
  return classDef.abilities as unknown as Record<string, string>;
}

export function isSetupTag(tag: string): boolean {
  return tag.startsWith("setup:");
}

export function isPayoffTag(tag: string): boolean {
  return tag === "payoff";
}
