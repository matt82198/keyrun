/**
 * Usage: import type { Character, GearItem, Facet, Ability, RunRecord, StoreV2, GearSlot, RunResult }
 *          from "./types.js";
 *        import { GEAR_SLOTS, KEYBIND_TABLE, FACET_POOLS, SCHEMA_VERSION, MAX_ITEM_TIER,
 *                  STARTING_CHARGES } from "./types.js";
 *
 * Shared data shapes for the character + queue screen store (schemaVersion 2: bounded gear
 * progression -- tier-capped items with a facet and crafting charges, per design/BIBLE.md section 7).
 */

export const SCHEMA_VERSION = 2 as const;

export type GearSlot = "weapon" | "armor" | "charm" | "ring" | "keystone";

export const GEAR_SLOTS: readonly GearSlot[] = ["weapon", "armor", "charm", "ring", "keystone"];

// Five rotational abilities, one keybind each -- the whole keybind table for the POC.
export const KEYBIND_TABLE: readonly string[] = ["Q", "W", "E", "R", "F"];

// Tier is power and it is capped at ten -- ten is the end (design/BIBLE.md section 7).
export const MAX_ITEM_TIER = 10;
// TUNE: crafting charges a freshly-rolled item starts with ("two charges per item").
export const STARTING_CHARGES = 2;

export interface Ability {
  id: string;
  name: string;
  keybind: string;
  talentId: string;
}

export interface Facet {
  id: string;
  text: string;
  abilityId: string;
}

export interface GearItem {
  id: string;
  slot: GearSlot;
  tier: number;
  facet: Facet;
  charges: number;
}

// Local mirror of sim/encounter/rewards.ts's FACET_POOLS -- kept in sync by hand (screen/ never
// imports sim/encounter's internal data, only its pure `rollRewardChoices` export) so the crafting
// reroll pool and the end-of-run reward pool read the same facet text.
export const FACET_POOLS: Record<GearSlot, Facet[]> = {
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

// Mirrors sim/encounter/timer.ts's RunResult -- an ended run is always exactly one of these.
export type RunResult = "timed" | "depleted" | "abandoned";

export interface RunRecord {
  id: string;
  dungeon: string;
  keyLevel: number;
  result: RunResult;
  durationSec: number;
  completedAt: number;
  rewardPicked: GearItem | null;
  ashGained: number;
}

export interface Character {
  classId: string;
  level: number;
  abilities: Ability[];
  gear: Record<GearSlot, GearItem>;
  // Items that tied the equipped item's tier: auto-equip never overwrites a chosen facet, so a tie is
  // parked here for the player to resolve (equip or discard) on the Loadout band.
  compareGear: Partial<Record<GearSlot, GearItem>>;
  // Slots auto-equipped since the loadout was last viewed; drives the Loadout band's "NEW" badge.
  recentlyEquipped: GearSlot[];
  // Account currency: every run banks Ash, win or lose (design/BIBLE.md section 7).
  ash: number;
  keyLevel: number;
  queued: boolean;
  runHistory: RunRecord[];
  // Pending end-of-run choice-of-three, set by completeRun and cleared by pickReward.
  rewardChoices: GearItem[] | null;
  lastSeenAt: number;
}

export interface StoreV2 {
  schemaVersion: 2;
  character: Character;
}
