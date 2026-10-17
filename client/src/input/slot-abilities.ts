/**
 * client/src/input/slot-abilities.ts
 *
 * THE single authority for "which ability does this key slot cast" (P0, D10). A keybind action
 * (ability1..ability5, abilityQ/E/R/F) names a SLOT, never an ability id; the ability in that slot
 * comes from the human's class kit (content/schema/class.ts's `ClassAbilitySlots`, per
 * design/BIBLE.md: 1-5 rotational, Q interrupt, E defensive, R utility, F movement).
 *
 * `resolveSlotAbilities` runs exactly once per run, in sim-host/run-setup.ts's `buildRunSetup`
 * (`RunSetup.slotAbilities`); the worker ships the result to the main thread in its `ready`
 * message, and both readers use that one map: input/commands.ts's `deriveAbilityCommands` (the
 * cast a key press sends) and render/ability-bar-model.ts (which slot of the bar sweeps). Talents
 * and gear (content/apply.ts) patch an ability's definition but never its id, so the class kit is
 * the whole answer for a loadout. A slot the kit does not fill throws: fail closed, never a
 * fallback that casts something else.
 *
 * Usage: `const slots = resolveSlotAbilities(registry.classes.get("warden")!); slots.abilityE // "warden.brace"`
 */
import type { ClassAbilitySlots, ClassDef } from "../../../content/schema/class.js";
import type { KeybindAction } from "./keybinds.js";

export type AbilityAction = Exclude<KeybindAction, "targetCycle" | "ping">;

/** Keybind action -> the class-kit slot it fires. Order is the ability bar's left-to-right order. */
export const ACTION_CLASS_SLOT: Readonly<Record<AbilityAction, keyof ClassAbilitySlots>> = {
  ability1: "rot1",
  ability2: "rot2",
  ability3: "rot3",
  ability4: "rot4",
  ability5: "rot5",
  abilityQ: "interrupt",
  abilityE: "defensive",
  abilityR: "utility",
  abilityF: "movement",
};

export const ABILITY_ACTIONS = Object.keys(ACTION_CLASS_SLOT) as readonly AbilityAction[];

/** Resolved ability id per ability keybind action, for one human's class kit. */
export type SlotAbilities = Readonly<Record<AbilityAction, string>>;

export function isAbilityAction(action: KeybindAction): action is AbilityAction {
  return action in ACTION_CLASS_SLOT;
}

export function resolveSlotAbilities(classDef: ClassDef): SlotAbilities {
  const out = {} as Record<AbilityAction, string>;
  for (const action of ABILITY_ACTIONS) {
    const slot = ACTION_CLASS_SLOT[action];
    const abilityId = classDef.abilities[slot];
    if (typeof abilityId !== "string" || abilityId.length === 0) {
      throw new Error(`resolveSlotAbilities: class "${classDef.id}" has no ability in slot "${slot}" (${action})`);
    }
    out[action] = abilityId;
  }
  return out;
}
