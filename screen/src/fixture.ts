/**
 * Usage: import { createFixtureCharacter } from "./fixture.js";
 *
 * Placeholder POC character: class id, level, no talent picks yet (every forked ability starts
 * at its base definition -- free respec, design/BIBLE.md sec 6), five gear slots at tier 1 with
 * no Facet chosen yet (Item.facet is nullable -- content/schema/facet.ts -- for exactly this:
 * freshly rolled gear before a pick) and full crafting charges, zero Ash, a valid default Clause
 * pick for the starting key level, and empty run history -- all placeholder values, deterministic
 * given `now`. Starting every Facet at null also guarantees applyLoadout's 2-setup/2-payoff
 * invariant holds on a fresh character: some real Facets (e.g. Warden's Eternal Vigil) retag a
 * neutral ability as a Payoff, and content/apply.ts skips a null Facet rather than applying one.
 */
import { pickClauseOffers } from "./clauses.js";
import { GEAR_SLOT_IDS, STARTING_CHARGES, type Character, type GearSlotId, type Item } from "./types.js";

const FIXTURE_CLASS_ID = "warden";
const FIXTURE_KEY_LEVEL = 5;

function makeGearItem(slot: GearSlotId): Item {
  return { id: `${slot}-fixture`, slot, tier: 1, facet: null, charges: STARTING_CHARGES };
}

export function createFixtureCharacter(now: number = Date.now()): Character {
  const dayMs = 24 * 60 * 60 * 1000;
  const gear = {} as Record<GearSlotId, Item>;
  for (const slot of GEAR_SLOT_IDS) gear[slot] = makeGearItem(slot);

  return {
    classId: FIXTURE_CLASS_ID,
    level: 10,
    talentChoices: {},
    gear,
    compareGear: {},
    recentlyEquipped: [],
    ash: 0,
    keyLevel: FIXTURE_KEY_LEVEL,
    clauseId: pickClauseOffers(FIXTURE_KEY_LEVEL)[0].id,
    queued: false,
    runHistory: [],
    rewardChoices: null,
    lastSeenAt: now - 2 * dayMs,
  };
}
