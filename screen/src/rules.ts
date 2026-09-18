/**
 * Usage: import { clampKeyLevel, isKeyLocked, clampItemTier, normalizeGearItem, autoEquip,
 *                  applyGearPick, equipCompare, discardCompare, findOwnedItem, replaceOwnedItem,
 *                  recordRun } from "./rules.js";
 *
 * Core, content-independent progression rules: gear tier clamping, the auto-equip/compare rule,
 * and run-history capping. Crafting (craft.ts), Ash/reward economy (progression.ts) and the
 * Clause pick (clauses.ts) are split out so no one file crosses the 190-line repo LOC rule.
 * Nothing here touches the DOM or localStorage directly -- panels call these and then `saveStore`.
 */
import { GEAR_SLOT_IDS, MAX_ITEM_TIER, type Character, type GearSlotId, type Item, type RunRecord } from "./types.js";

const MIN_KEY_LEVEL = 2;
const MAX_KEY_LEVEL = 20;
const MAX_RUN_HISTORY = 5;

export function clampKeyLevel(level: number): number {
  if (!Number.isFinite(level)) return MIN_KEY_LEVEL;
  return Math.min(MAX_KEY_LEVEL, Math.max(MIN_KEY_LEVEL, Math.round(level)));
}

export function isKeyLocked(character: Pick<Character, "queued">): boolean {
  return character.queued === true;
}

/** Tier is capped at MAX_ITEM_TIER; any write above it clamps and logs a warning (design/BIBLE.md section 7). */
export function clampItemTier(tier: number): number {
  const rounded = Number.isFinite(tier) ? Math.round(tier) : 1;
  const clamped = Math.min(MAX_ITEM_TIER, Math.max(1, rounded));
  if (clamped !== tier) {
    console.warn(`[keyrun/screen] item tier ${tier} clamped to ${clamped} (cap ${MAX_ITEM_TIER})`);
  }
  return clamped;
}

/** Normalizes a gear item before it ever enters the store: clamps its tier (logging on clamp). */
export function normalizeGearItem(item: Item): Item {
  const tier = clampItemTier(item.tier);
  return tier === item.tier ? item : { ...item, tier };
}

export interface AutoEquipResult {
  gear: Record<GearSlotId, Item>;
  compare: Item | null;
  equipped: boolean;
}

/**
 * Higher tier wins. Equal tier keeps the current item and flags the incoming one as "compare" --
 * auto-equip never overwrites a facet the player chose. Lower tier is discarded.
 */
export function autoEquip(gear: Record<GearSlotId, Item>, incoming: Item): AutoEquipResult {
  const item = normalizeGearItem(incoming);
  const current = gear[item.slot];
  if (item.tier > current.tier) {
    return { gear: { ...gear, [item.slot]: item }, compare: null, equipped: true };
  }
  if (item.tier === current.tier) {
    return { gear, compare: item, equipped: false };
  }
  return { gear, compare: null, equipped: false };
}

function withRecentlyEquipped(slots: GearSlotId[], slot: GearSlotId): GearSlotId[] {
  return slots.includes(slot) ? slots : [...slots, slot];
}

/** Runs an incoming item through autoEquip and applies the result (equip, or park as a compare). */
export function applyGearPick(character: Character, incoming: Item): Character {
  const { gear, compare, equipped } = autoEquip(character.gear, incoming);
  if (equipped) {
    return {
      ...character,
      gear,
      recentlyEquipped: withRecentlyEquipped(character.recentlyEquipped, incoming.slot),
    };
  }
  if (compare) {
    return { ...character, compareGear: { ...character.compareGear, [incoming.slot]: compare } };
  }
  return character;
}

/** Player resolves a parked compare item by equipping it over the current item in that slot. */
export function equipCompare(character: Character, slot: GearSlotId): Character {
  const pending = character.compareGear[slot];
  if (!pending) return character;
  const { [slot]: _removed, ...restCompare } = character.compareGear;
  return {
    ...character,
    gear: { ...character.gear, [slot]: pending },
    compareGear: restCompare,
    recentlyEquipped: withRecentlyEquipped(character.recentlyEquipped, slot),
  };
}

/** Player resolves a parked compare item by discarding it, keeping the currently equipped item. */
export function discardCompare(character: Character, slot: GearSlotId): Character {
  if (!character.compareGear[slot]) return character;
  const { [slot]: _removed, ...restCompare } = character.compareGear;
  return { ...character, compareGear: restCompare };
}

/** Finds an item (equipped or parked as a compare) by id, wherever it lives in the five slots. */
export function findOwnedItem(character: Character, itemId: string): Item | undefined {
  for (const slot of GEAR_SLOT_IDS) {
    if (character.gear[slot].id === itemId) return character.gear[slot];
    const pending = character.compareGear[slot];
    if (pending && pending.id === itemId) return pending;
  }
  return undefined;
}

/** Replaces an owned item in place (equipped slot or parked compare), by matching id. */
export function replaceOwnedItem(character: Character, next: Item): Character {
  if (character.gear[next.slot].id === next.id) {
    return { ...character, gear: { ...character.gear, [next.slot]: next } };
  }
  if (character.compareGear[next.slot]?.id === next.id) {
    return { ...character, compareGear: { ...character.compareGear, [next.slot]: next } };
  }
  return character;
}

export function getNewGearSlots(character: Pick<Character, "recentlyEquipped">): GearSlotId[] {
  return [...character.recentlyEquipped];
}

export function markLoadoutSeen(character: Character, _now: number = Date.now()): Character {
  return { ...character, recentlyEquipped: [] };
}

export function recordRun(history: RunRecord[], entry: RunRecord): RunRecord[] {
  return [entry, ...history].slice(0, MAX_RUN_HISTORY);
}
