/**
 * Usage: import { rerollFacet, pickFacet } from "./craft.js";
 *
 * Gear Facet crafting (design/BIBLE.md sec 7): `rerollFacet` is the charge-gated random reroll
 * ("crafting is two charges per item ... twice"), sourced from content's real per-class Facet
 * pool (content/data/facets/*.json via screen/src/content.ts) instead of the old screen-local
 * FACET_POOLS mirror. `pickFacet` is the Loadout band's facet picker: an explicit, free,
 * unlimited choice among the same pool -- "crafting charges/Ash gate ACQUIRING [a fresh random
 * roll], not swapping [to a Facet the pool already publishes]" (dispatch); it never touches
 * `charges`, matching the free-respec spirit BIBLE.md sec 6 states for talents.
 */
import { getFacetsForSlot } from "./content.js";
import { hashString, seededPick } from "./hash.js";
import { findOwnedItem, replaceOwnedItem } from "./rules.js";
import type { Character, Item } from "./types.js";

/**
 * Crafting: consumes one of two charges and rerolls the item's facet from its class's slot pool.
 * Refuses at zero. The pick is seeded from the store itself -- classId, item id, and the item's
 * own charge count (which changes on every call) -- so no caller-supplied randomness is needed
 * and the result is still deterministic given the same character + item state.
 */
export function rerollFacet(character: Character, itemId: string): Character {
  const item = findOwnedItem(character, itemId);
  if (!item || item.charges <= 0) return character;
  const pool = getFacetsForSlot(character.classId, item.slot);
  if (pool.length === 0) return character;
  const seed = hashString(`${character.classId}:${item.id}:${item.charges}`);
  const index = seededPick(seed, pool.length);
  const facetDef = pool[index] ?? pool[0]!;
  const next: Item = {
    ...item,
    facet: { id: facetDef.id, text: facetDef.text, abilityId: facetDef.abilityId },
    charges: item.charges - 1,
  };
  return replaceOwnedItem(character, next);
}

/**
 * The Loadout band's facet picker: sets the equipped (or parked-compare) item's Facet directly to
 * `facetId`, provided it belongs to that item's slot's class pool. Free -- no charge cost, no Ash.
 * A no-op for an unknown item id or a facet id outside the slot's class pool.
 */
export function pickFacet(character: Character, itemId: string, facetId: string): Character {
  const item = findOwnedItem(character, itemId);
  if (!item) return character;
  const pool = getFacetsForSlot(character.classId, item.slot);
  const facetDef = pool.find((f) => f.id === facetId);
  if (!facetDef) return character;
  const next: Item = { ...item, facet: { id: facetDef.id, text: facetDef.text, abilityId: facetDef.abilityId } };
  return replaceOwnedItem(character, next);
}
