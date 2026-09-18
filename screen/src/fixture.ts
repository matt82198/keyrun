/**
 * Usage: import { createFixtureCharacter } from "./fixture.js";
 *
 * Placeholder POC character: class id, level, five abilities with keybinds from the small inline
 * table (types.ts KEYBIND_TABLE), per-ability talent picks, five gear slots at tier 1 with the
 * slot's first facet and full crafting charges, zero Ash, and empty run history -- all placeholder
 * values, deterministic given `now`.
 */
import {
  FACET_POOLS,
  GEAR_SLOTS,
  STARTING_CHARGES,
  type Ability,
  type Character,
  type GearItem,
  type GearSlot,
  KEYBIND_TABLE,
} from "./types.js";

function makeAbility(index: number): Ability {
  const keybind = KEYBIND_TABLE[index] ?? "?";
  return { id: `ability-${index + 1}`, name: `Ability ${index + 1}`, keybind, talentId: `talent-${index + 1}a` };
}

function makeGearItem(slot: GearSlot): GearItem {
  const facet = FACET_POOLS[slot][0]!;
  return { id: `${slot}-fixture`, slot, tier: 1, facet, charges: STARTING_CHARGES };
}

export function createFixtureCharacter(now: number = Date.now()): Character {
  const dayMs = 24 * 60 * 60 * 1000;
  const gear = {} as Record<GearSlot, GearItem>;
  for (const slot of GEAR_SLOTS) gear[slot] = makeGearItem(slot);

  return {
    classId: "warden",
    level: 10,
    abilities: [0, 1, 2, 3, 4].map(makeAbility),
    gear,
    compareGear: {},
    recentlyEquipped: [],
    ash: 0,
    keyLevel: 5,
    queued: false,
    runHistory: [],
    rewardChoices: null,
    lastSeenAt: now - 2 * dayMs,
  };
}
