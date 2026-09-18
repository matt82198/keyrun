/**
 * Usage: import type { Character, GearItem, Ability, RunRecord, StoreV1, GearSlot } from "./types.js";
 *        import { GEAR_SLOTS, KEYBIND_TABLE, SCHEMA_VERSION } from "./types.js";
 *
 * Shared data shapes for the character + queue screen store (schemaVersion 1).
 */

export const SCHEMA_VERSION = 1 as const;

export type GearSlot = "weapon" | "armor" | "trinket" | "ring1" | "ring2";

export const GEAR_SLOTS: readonly GearSlot[] = ["weapon", "armor", "trinket", "ring1", "ring2"];

// Five rotational abilities, one keybind each -- the whole keybind table for the POC.
export const KEYBIND_TABLE: readonly string[] = ["Q", "W", "E", "R", "F"];

export interface Ability {
  id: string;
  name: string;
  keybind: string;
  talentId: string;
}

export interface GearItem {
  id: string;
  name: string;
  ilvl: number;
  acquiredAt: number;
}

export interface RunRecord {
  id: string;
  dungeon: string;
  keyLevel: number;
  result: "success" | "failure";
  durationSec: number;
  completedAt: number;
}

export interface Character {
  classId: string;
  level: number;
  abilities: Ability[];
  gear: Record<GearSlot, GearItem>;
  keyLevel: number;
  queued: boolean;
  runHistory: RunRecord[];
  lastSeenAt: number;
}

export interface StoreV1 {
  schemaVersion: 1;
  character: Character;
}
