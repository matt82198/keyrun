/**
 * Usage: import type { Character, RunRecord, StoreV3, RunResult, Item, GearSlotId, FacetDef }
 *          from "./types.js";
 *        import { GEAR_SLOT_IDS, KEYBIND_TABLE, SCHEMA_VERSION, MAX_ITEM_TIER,
 *                  STARTING_CHARGES } from "./types.js";
 *
 * Shared data shapes for the character + queue screen store (schemaVersion 3: gear now carries
 * content's own `Item`/`GearSlotId` shape from content/schema/facet.ts instead of a screen-local
 * mirror -- content/schema/facet.ts flagged this exact drift as "future work for whoever wires
 * gear into the character screen"; this is that wiring. Talent picks (`talentChoices`) and the
 * per-run Clause pick (`clauseId`) are new in v3, per design/BIBLE.md sections 6 and 8.
 */
import type { GearSlotId, Item } from "../../content/schema/facet.js";

export { GEAR_SLOT_IDS } from "../../content/schema/facet.js";
export type { FacetDef, GearSlotId, Item } from "../../content/schema/facet.js";

export const SCHEMA_VERSION = 3 as const;

// Five rotational abilities, one keybind each (design/BIBLE.md sec 2: "1-5, rotational
// abilities"). Q/E/R/F/G (interrupt/defensive/utility/movement/ping) are fixed, not user-facing
// choices, so the screen does not need to display them as a talent/keybind list.
export const KEYBIND_TABLE: readonly string[] = ["1", "2", "3", "4", "5"];

// Tier is power and it is capped at ten -- ten is the end (design/BIBLE.md section 7).
export const MAX_ITEM_TIER = 10;
// TUNE: crafting charges a freshly-rolled item starts with ("two charges per item").
export const STARTING_CHARGES = 2;

// Mirrors sim/encounter/timer.ts's RunResult -- an ended run is always exactly one of these.
export type RunResult = "timed" | "depleted" | "abandoned";

export interface RunRecord {
  id: string;
  dungeon: string;
  keyLevel: number;
  result: RunResult;
  durationSec: number;
  completedAt: number;
  rewardPicked: Item | null;
  ashGained: number;
}

export interface Character {
  classId: string;
  level: number;
  // Free respec (design/BIBLE.md sec 6): forked-ability id -> chosen TalentOption id. An ability
  // with no entry stays at its base definition. Same shape as content/apply.ts's TalentChoices.
  talentChoices: Record<string, string>;
  gear: Record<GearSlotId, Item>;
  // Items that tied the equipped item's tier: auto-equip never overwrites a chosen facet, so a tie is
  // parked here for the player to resolve (equip or discard) on the Loadout band.
  compareGear: Partial<Record<GearSlotId, Item>>;
  // Slots auto-equipped since the loadout was last viewed; drives the Loadout band's "NEW" badge.
  recentlyEquipped: GearSlotId[];
  // Account currency: every run banks Ash, win or lose (design/BIBLE.md section 7).
  ash: number;
  keyLevel: number;
  // The current key's Clause pick (design/BIBLE.md sec 8): exactly one is always active, never
  // "none" -- always one of the three offers `pickClauseOffers(keyLevel)` currently returns.
  clauseId: string;
  queued: boolean;
  runHistory: RunRecord[];
  // Pending end-of-run choice-of-three, set by completeRun and cleared by pickReward.
  rewardChoices: Item[] | null;
  lastSeenAt: number;
}

export interface StoreV3 {
  schemaVersion: 3;
  character: Character;
}
