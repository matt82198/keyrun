/**
 * Usage: import { clampKeyLevel, isKeyLocked, getNewGearSlots, markLoadoutSeen,
 *                  recordRun, buildPlayUrl } from "./rules.js";
 *
 * Pure business rules for the character + queue screen: key-level clamping
 * and lock, loadout "new" delta flags, run-history capping, and the Play URL.
 */
import { GEAR_SLOTS, type Character, type GearSlot, type RunRecord } from "./types.js";

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

export function getNewGearSlots(character: Pick<Character, "gear" | "lastSeenAt">): GearSlot[] {
  return GEAR_SLOTS.filter((slot) => character.gear[slot].acquiredAt > character.lastSeenAt);
}

export function markLoadoutSeen(character: Character, now: number = Date.now()): Character {
  return { ...character, lastSeenAt: now };
}

export function recordRun(history: RunRecord[], entry: RunRecord): RunRecord[] {
  return [entry, ...history].slice(0, MAX_RUN_HISTORY);
}

export function deriveSeed(
  character: Pick<Character, "classId" | "level" | "runHistory">,
  keyLevel: number,
): number {
  const basis = `${character.classId}:${character.level}:${keyLevel}:${character.runHistory.length}`;
  let hash = 0;
  for (let i = 0; i < basis.length; i += 1) {
    hash = (hash * 31 + basis.charCodeAt(i)) >>> 0;
  }
  return hash;
}

export function buildPlayUrl(
  character: Pick<Character, "classId" | "level" | "runHistory">,
  keyLevel: number,
): string {
  const key = clampKeyLevel(keyLevel);
  const seed = deriveSeed(character, key);
  return `../client/?seed=${seed}&key=${key}`;
}
