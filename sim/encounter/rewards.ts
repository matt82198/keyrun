/**
 * sim/encounter/rewards.ts -- end-of-run choice-of-three (design/BIBLE.md section 7).
 *
 * `rollRewardChoices(seed, keyLevel, result, slotsOwned)` is a pure, deterministic function of its
 * inputs: the same seed + keyLevel + result + slotsOwned always produces the same three items, via
 * the shared xoshiro128** integer PRNG (sim/core/prng.ts) -- no Math.random, no floats in the roll
 * itself. Tier bands widen as the key level rises; the top two tiers (9-10, the "hard BiS" per the
 * bible) only ever appear on a "timed" clear at key >= HARD_BIS_MIN_KEY. The three choices are never
 * identical (slot + facet + tier triple), re-drawing on a collision up to a small bounded retry count.
 *
 * Local copy of the GearSlot/Facet/Item shapes: sim/ never imports content/ or screen/ (sim/CLAUDE.md
 * "Contracts other domains rely on"). screen/src/types.ts's GearItem is structurally identical to
 * `Item` here, so callers use these return values directly as store gear items with no cast needed.
 *
 * Usage: `const [a, b, c] = rollRewardChoices(seed, keyLevel, "timed", ["weapon","armor","charm","ring","keystone"]);`
 */
import { nextInt, seedState, type PrngState } from "../core/prng";
import type { RunResult } from "./timer";

export type GearSlot = "weapon" | "armor" | "charm" | "ring" | "keystone";

export interface Facet {
  id: string;
  text: string;
  abilityId: string;
}

export interface Item {
  id: string;
  slot: GearSlot;
  tier: number;
  facet: Facet;
  charges: number;
}

export const GEAR_SLOTS: readonly GearSlot[] = ["weapon", "armor", "charm", "ring", "keystone"];

// TUNE: crafting charges a freshly-rolled item starts with (design/BIBLE.md section 7: "two charges").
export const STARTING_CHARGES = 2;
// TUNE: minimum key level, on a timed clear, that can drop the top two tiers (the "hard BiS" rule).
export const HARD_BIS_MIN_KEY = 15;
const MAX_TIER = 10;
const SOFT_TIER_CAP = 8;

// Placeholder POC facet pool: three text/abilityId options per gear slot (research/05 -- facets retag
// function, not just numbers). abilityId lines up with the screen fixture's five ability ids.
const FACET_POOLS: Record<GearSlot, Facet[]> = {
  weapon: [
    { id: "weapon-edge", text: "Strike also applies Sundered to a second target", abilityId: "ability-1" },
    { id: "weapon-haft", text: "Strike's Window lasts 1s longer", abilityId: "ability-1" },
    { id: "weapon-brand", text: "Strike marks the target: next ally ability crits", abilityId: "ability-1" },
  ],
  armor: [
    { id: "armor-brace", text: "Brace also grants 10% damage reduction to one ally", abilityId: "ability-2" },
    { id: "armor-anchor", text: "Brace's Window radius is doubled", abilityId: "ability-2" },
    { id: "armor-ward", text: "Brace refunds its cost on a successful interrupt", abilityId: "ability-2" },
  ],
  charm: [
    { id: "charm-ember", text: "Heat builds 20% faster", abilityId: "ability-3" },
    { id: "charm-flare", text: "Detonate's Payoff also applies to adjacent Combos", abilityId: "ability-3" },
    { id: "charm-cinder", text: "Detonate no longer resets Heat on a failed push-your-luck", abilityId: "ability-3" },
  ],
  ring: [
    { id: "ring-verse", text: "Stance swaps cost no GCD", abilityId: "ability-4" },
    { id: "ring-chorus", text: "Verse effects also apply to the lowest-health ally", abilityId: "ability-4" },
    { id: "ring-coda", text: "The final Verse note deals bonus damage to Windowed targets", abilityId: "ability-4" },
  ],
  keystone: [
    { id: "keystone-echo", text: "Combos landed inside a Window grant bonus Ash", abilityId: "ability-5" },
    { id: "keystone-lock", text: "Interrupting a boss cast extends the next Window by 1s", abilityId: "ability-5" },
    { id: "keystone-tempo", text: "Slack decays 10% slower", abilityId: "ability-5" },
  ],
};

function clampKey(keyLevel: number): number {
  if (!Number.isFinite(keyLevel)) return 2;
  return Math.min(20, Math.max(2, Math.round(keyLevel)));
}

/** Higher key -> higher tier band; tiers 9-10 unlock only for a timed clear at key >= HARD_BIS_MIN_KEY. */
function tierBand(keyLevel: number, result: RunResult): { min: number; max: number } {
  const key = clampKey(keyLevel);
  const base = Math.min(SOFT_TIER_CAP, Math.floor((key - 2) / 2) + 1);
  const min = Math.max(1, base - 1);
  const hardBis = result === "timed" && key >= HARD_BIS_MIN_KEY;
  const max = hardBis ? MAX_TIER : Math.min(SOFT_TIER_CAP, base + 1);
  return { min, max };
}

function drawItem(
  state: PrngState,
  pool: readonly GearSlot[],
  band: { min: number; max: number },
  suffix: number
): [Item, PrngState] {
  const [slotIdx, s1] = nextInt(state, pool.length);
  const slot = pool[slotIdx]!;
  const facets = FACET_POOLS[slot];
  const [facetIdx, s2] = nextInt(s1, facets.length);
  const facet = facets[facetIdx]!;
  const span = band.max - band.min + 1;
  const [tierRoll, s3] = nextInt(s2, span);
  const tier = band.min + tierRoll;
  const item: Item = {
    id: `reward-${slot}-${facet.id}-t${tier}-${suffix}`,
    slot,
    tier,
    facet,
    charges: STARTING_CHARGES,
  };
  return [item, s3];
}

function itemKey(item: Item): string {
  return `${item.slot}:${item.facet.id}:${item.tier}`;
}

/** Deterministic choice-of-three: same inputs -> same three items, never two identical (slot+facet+tier). */
export function rollRewardChoices(
  seed: number,
  keyLevel: number,
  result: RunResult,
  slotsOwned: readonly GearSlot[]
): [Item, Item, Item] {
  const pool = slotsOwned.length > 0 ? slotsOwned : GEAR_SLOTS;
  const band = tierBand(keyLevel, result);
  let state = seedState(seed);
  const seen = new Set<string>();
  const items: Item[] = [];

  for (let i = 0; i < 3; i += 1) {
    let chosen: Item | undefined;
    for (let attempt = 0; attempt < 20; attempt += 1) {
      const [drawn, next] = drawItem(state, pool, band, i);
      state = next;
      if (!seen.has(itemKey(drawn))) {
        chosen = drawn;
        break;
      }
    }
    const item = chosen ?? drawItem(state, pool, band, i)[0];
    seen.add(itemKey(item));
    items.push(item);
  }

  return [items[0]!, items[1]!, items[2]!];
}
