/**
 * content/schema/facet.ts
 * BIBLE.md Sec 7 "Gear and progression": every item has one Tier (flat power, capped at 10) and
 * one Facet (function). "Facets change function ... a Facet retags an ability's Window, adds a
 * second Payoff to a neutral ability, changes a floor patch's shape, or alters the resource
 * loop." This module is the data shape for a Facet and the gear Item that carries one.
 * Data-shape only, no logic  -  see content/apply.ts for how patches are applied.
 *
 * NOTE on drift vs screen/src/types.ts: that module's `GearItem`/`GearSlot`
 * (weapon/armor/trinket/ring1/ring2, `{ id, name, ilvl, acquiredAt }`) is a placeholder shape
 * from before Facets existed and does not carry a Facet or a Tier. It is out of this lane's
 * ownership (screen/**). The `Item`/`GearSlotId` types below follow BIBLE.md Sec 7's five named
 * slots (Weapon, Armor, Charm, Ring, Keystone) and the dispatch-specified loadout shape instead.
 * Reconciling screen's GearItem with this Item is future work for whoever wires gear into the
 * character screen  -  flagged here, not silently patched over (content/CLAUDE.md's KNOWN DRIFT
 * convention).
 */

import type { AbilityCost, AbilityDef, EffectOp, ShapeDef } from "./ability";

/** BIBLE.md Sec 7: "Five slots: Weapon, Armor, Charm, Ring, Keystone." */
export type GearSlotId = "weapon" | "armor" | "charm" | "ring" | "keystone";

export const GEAR_SLOT_IDS: readonly GearSlotId[] = ["weapon", "armor", "charm", "ring", "keystone"];

/**
 * The structured half of a Facet's patch: append/retag operations rather than wholesale field
 * replacement. `tagAdd`/`tagRemove` merge into the base ability's tags; `effectsAppend` appends
 * onto the base ability's effects. `shape`/`cooldownTicks`/`cost` replace the corresponding base
 * field wholesale, same as a raw `Partial<AbilityDef>` patch would (see content/apply.ts for the
 * full deep-merge contract).
 */
export interface FacetStructuredPatch {
  tagAdd?: string[];
  tagRemove?: string[];
  shape?: ShapeDef;
  cooldownTicks?: number;
  cost?: AbilityCost;
  effectsAppend?: EffectOp[];
}

/**
 * A Facet's patch is either a raw field-replacement patch (same shape a talent option uses) or
 * the structured append/retag form above. Never a flat number  -  every Facet changes function.
 */
export type FacetPatch = Partial<AbilityDef> | FacetStructuredPatch;

export interface FacetDef {
  id: string;
  slot: GearSlotId;
  /** The ability this Facet retags/reshapes when socketed. Must resolve to a real ability id. */
  abilityId: string;
  text: string;
  patch: FacetPatch;
}

/** BIBLE.md Sec 7: "Crafting is two charges per item: reroll the Facet ... twice." */
export interface Item {
  id: string;
  slot: GearSlotId;
  /** 1-10; BIBLE.md Sec 7: "Tier is power and it is capped at 10." Not consumed by applyLoadout. */
  tier: number;
  facet: { id: string; text: string; abilityId: string } | null;
  /** Remaining reroll charges, 0-2. Not consumed by applyLoadout. */
  charges: number;
}
