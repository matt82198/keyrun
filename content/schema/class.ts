/**
 * content/schema/class.ts
 * ClassDef has no sim equivalent (sim/combat only knows an actor's bare `classId` string; the
 * loop/ability-slot/talent structure is entirely a content concern), so it stays content-only.
 * `TalentOption.patch` is `Partial<AbilityDef>` against the real sim contract (imported from
 * ./ability) so a patch can only ever set fields the sim ability pipeline actually understands.
 */

import type { AbilityDef } from "./ability";

export type LoopKind = "builder-spender" | "heat-overheat" | "stance";

export interface ClassLoop {
  resource: string;
  max: number;
  kind: LoopKind;
}

/** Exactly five rotational slots (BIBLE.md D4/Sec 5) plus the four fixed utility keys. */
export interface ClassAbilitySlots {
  rot1: string;
  rot2: string;
  rot3: string;
  rot4: string;
  rot5: string;
  interrupt: string;
  defensive: string;
  utility: string;
  movement: string;
}

export interface TalentOption {
  id: string;
  name: string;
  text: string;
  /** Must change function (tags/shape/cost/cooldown/effects)  -  never a flat percentage. */
  patch: Partial<AbilityDef>;
}

export interface TalentFork {
  abilityId: string;
  options: TalentOption[];
}

export interface ClassDef {
  id: string;
  name: string;
  loop: ClassLoop;
  abilities: ClassAbilitySlots;
  talents: TalentFork[];
}
