/**
 * Usage: import { createFixtureCharacter } from "./fixture.js";
 *
 * Placeholder POC character: class id, level, five abilities with keybinds
 * from the small inline table (types.ts KEYBIND_TABLE), per-ability talent
 * picks, and five gear slots -- all placeholder values, deterministic given
 * `now`.
 */
import { KEYBIND_TABLE, type Ability, type Character, type GearItem, type GearSlot } from "./types.js";

function makeAbility(index: number): Ability {
  const keybind = KEYBIND_TABLE[index] ?? "?";
  return { id: `ability-${index + 1}`, name: `Ability ${index + 1}`, keybind, talentId: `talent-${index + 1}a` };
}

function makeGearItem(slot: GearSlot, acquiredAt: number): GearItem {
  return { id: `${slot}-fixture`, name: `Placeholder ${slot}`, ilvl: 100, acquiredAt };
}

export function createFixtureCharacter(now: number = Date.now()): Character {
  const dayMs = 24 * 60 * 60 * 1000;
  return {
    classId: "warden",
    level: 10,
    abilities: [0, 1, 2, 3, 4].map(makeAbility),
    gear: {
      weapon: makeGearItem("weapon", now - 5 * dayMs),
      armor: makeGearItem("armor", now - 5 * dayMs),
      trinket: makeGearItem("trinket", now - 5 * dayMs),
      ring1: makeGearItem("ring1", now - 1 * dayMs), // recently acquired -> shows as "new"
      ring2: makeGearItem("ring2", now - 5 * dayMs),
    },
    keyLevel: 5,
    queued: false,
    runHistory: [],
    lastSeenAt: now - 2 * dayMs,
  };
}
