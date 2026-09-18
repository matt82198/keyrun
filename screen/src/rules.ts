/**
 * Usage: import { clampKeyLevel, isKeyLocked, clampItemTier, autoEquip, applyGearPick, equipCompare,
 *                  discardCompare, rerollFacet, ashForResult, getNewGearSlots, markLoadoutSeen,
 *                  recordRun, completeRun, pickReward, computeRunDelta, buildPlayUrl } from "./rules.js";
 *
 * Pure business rules for the character + queue screen: gear tier clamping, the auto-equip/compare
 * rule, crafting (facet reroll), Ash accrual, the end-of-run reward roll and pick, run-history
 * capping, the "since last run" delta line, and the Play URL. Nothing here touches the DOM or
 * localStorage directly -- panels call these and then `saveStore`.
 */
import { rollRewardChoices } from "../../sim/encounter/rewards.js";
import {
  FACET_POOLS,
  GEAR_SLOTS,
  MAX_ITEM_TIER,
  STARTING_CHARGES,
  type Character,
  type Facet,
  type GearItem,
  type GearSlot,
  type RunRecord,
  type RunResult,
} from "./types.js";

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
export function normalizeGearItem(item: GearItem): GearItem {
  const tier = clampItemTier(item.tier);
  return tier === item.tier ? item : { ...item, tier };
}

export interface AutoEquipResult {
  gear: Record<GearSlot, GearItem>;
  compare: GearItem | null;
  equipped: boolean;
}

/**
 * Higher tier wins. Equal tier keeps the current item and flags the incoming one as "compare" --
 * auto-equip never overwrites a facet the player chose. Lower tier is discarded.
 */
export function autoEquip(gear: Record<GearSlot, GearItem>, incoming: GearItem): AutoEquipResult {
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

function withRecentlyEquipped(slots: GearSlot[], slot: GearSlot): GearSlot[] {
  return slots.includes(slot) ? slots : [...slots, slot];
}

/** Runs an incoming item through autoEquip and applies the result (equip, or park as a compare). */
export function applyGearPick(character: Character, incoming: GearItem): Character {
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
export function equipCompare(character: Character, slot: GearSlot): Character {
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
export function discardCompare(character: Character, slot: GearSlot): Character {
  if (!character.compareGear[slot]) return character;
  const { [slot]: _removed, ...restCompare } = character.compareGear;
  return { ...character, compareGear: restCompare };
}

function findOwnedItem(character: Character, itemId: string): GearItem | undefined {
  for (const slot of GEAR_SLOTS) {
    if (character.gear[slot].id === itemId) return character.gear[slot];
    const pending = character.compareGear[slot];
    if (pending && pending.id === itemId) return pending;
  }
  return undefined;
}

function replaceOwnedItem(character: Character, next: GearItem): Character {
  if (character.gear[next.slot].id === next.id) {
    return { ...character, gear: { ...character.gear, [next.slot]: next } };
  }
  if (character.compareGear[next.slot]?.id === next.id) {
    return { ...character, compareGear: { ...character.compareGear, [next.slot]: next } };
  }
  return character;
}

/**
 * Crafting: consumes one of two charges and rerolls the item's facet from its slot's pool. Refuses
 * at zero. The pick is seeded from the store itself -- classId, item id, and the item's own charge
 * count (which changes on every call) -- so no caller-supplied randomness is needed and the result
 * is still deterministic given the same character + item state.
 */
export function rerollFacet(character: Character, itemId: string): Character {
  const item = findOwnedItem(character, itemId);
  if (!item || item.charges <= 0) return character;
  const pool = FACET_POOLS[item.slot];
  const seed = hashString(`${character.classId}:${item.id}:${item.charges}`);
  const index = seededPick(seed, pool.length);
  const facet: Facet = pool[index] ?? item.facet;
  const next: GearItem = { ...item, facet, charges: item.charges - 1 };
  return replaceOwnedItem(character, next);
}

/** Tiny deterministic integer pick, seed -> [0, n). Local to screen/ (sim/core's PRNG is for sim/ state). */
function seededPick(seed: number, n: number): number {
  if (n <= 0) return 0;
  const mixed = Math.imul(seed ^ 0x9e3779b9, 0x85ebca6b) >>> 0;
  return mixed % n;
}

export function getNewGearSlots(character: Pick<Character, "recentlyEquipped">): GearSlot[] {
  return [...character.recentlyEquipped];
}

export function markLoadoutSeen(character: Character, _now: number = Date.now()): Character {
  return { ...character, recentlyEquipped: [] };
}

export function recordRun(history: RunRecord[], entry: RunRecord): RunRecord[] {
  return [entry, ...history].slice(0, MAX_RUN_HISTORY);
}

// TUNE: Ash awarded per run by result, win or lose (design/BIBLE.md section 7): timed > depleted > abandoned.
export const ASH_TIMED = 50;
export const ASH_DEPLETED = 30;
export const ASH_ABANDONED = 10;

export function ashForResult(result: RunResult): number {
  if (result === "timed") return ASH_TIMED;
  if (result === "depleted") return ASH_DEPLETED;
  return ASH_ABANDONED;
}

export function deriveSeed(
  character: Pick<Character, "classId" | "level" | "runHistory">,
  keyLevel: number,
): number {
  const basis = `${character.classId}:${character.level}:${keyLevel}:${character.runHistory.length}`;
  return hashString(basis);
}

function deriveRewardSeed(
  character: Pick<Character, "classId" | "level">,
  params: { result: RunResult; keyLevel: number },
  now: number,
): number {
  const basis = `${character.classId}:${character.level}:${params.keyLevel}:${params.result}:${now}`;
  return hashString(basis);
}

function hashString(basis: string): number {
  let hash = 0;
  for (let i = 0; i < basis.length; i += 1) {
    hash = (hash * 31 + basis.charCodeAt(i)) >>> 0;
  }
  return hash;
}

export interface CompleteRunParams {
  result: RunResult;
  keyLevel: number;
  durationSec: number;
}

export interface CompleteRunOutcome {
  character: Character;
  rewardChoices: GearItem[];
}

/**
 * Ends a run: banks Ash by result, rolls the deterministic choice-of-three (sim/encounter/rewards.ts),
 * and appends a history entry (reward left null until pickReward resolves it).
 */
export function completeRun(character: Character, params: CompleteRunParams, now: number = Date.now()): CompleteRunOutcome {
  const ash = ashForResult(params.result);
  const seed = deriveRewardSeed(character, params, now);
  const rewardChoices: GearItem[] = rollRewardChoices(seed, params.keyLevel, params.result, GEAR_SLOTS).map(
    normalizeGearItem,
  );
  const record: RunRecord = {
    id: `run-${now}`,
    dungeon: "Hollow Vault",
    keyLevel: params.keyLevel,
    result: params.result,
    durationSec: params.durationSec,
    completedAt: now,
    rewardPicked: null,
    ashGained: ash,
  };
  const character2: Character = {
    ...character,
    ash: character.ash + ash,
    runHistory: recordRun(character.runHistory, record),
    rewardChoices,
  };
  return { character: character2, rewardChoices };
}

/** Pick button: equips (or parks as compare) the chosen reward and attaches it to the latest run record. */
export function pickReward(character: Character, itemId: string): Character {
  const choices = character.rewardChoices ?? [];
  const picked = choices.find((item) => item.id === itemId);
  if (!picked) return character;

  const withGear = applyGearPick(character, picked);
  const [latest, ...rest] = withGear.runHistory;
  const runHistory = latest ? [{ ...latest, rewardPicked: picked }, ...rest] : withGear.runHistory;

  return { ...withGear, runHistory, rewardChoices: null };
}

export interface RunDelta {
  newBestTime: boolean;
  newTier: boolean;
  ashGained: number;
  text: string;
}

/** "Since last run": did this run beat the prior best time, land a new tier, and how much Ash it banked. */
export function computeRunDelta(character: Pick<Character, "runHistory">): RunDelta | null {
  const [latest, ...rest] = character.runHistory;
  if (!latest) return null;

  const priorBest = rest.length > 0 ? Math.min(...rest.map((run) => run.durationSec)) : Number.POSITIVE_INFINITY;
  const newBestTime = latest.durationSec < priorBest;
  const newTier = latest.rewardPicked !== null;

  const parts: string[] = [];
  if (newBestTime) parts.push("new best time");
  if (newTier) parts.push(`new tier ${latest.rewardPicked!.tier}`);
  parts.push(`+${latest.ashGained} Ash`);

  return { newBestTime, newTier, ashGained: latest.ashGained, text: parts.join(" · ") };
}

export function buildPlayUrl(
  character: Pick<Character, "classId" | "level" | "runHistory">,
  keyLevel: number,
): string {
  const key = clampKeyLevel(keyLevel);
  const seed = deriveSeed(character, key);
  return `../client/?seed=${seed}&key=${key}`;
}
