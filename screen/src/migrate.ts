/**
 * Usage: import { migrate } from "./migrate.js";
 *        const storeV3 = migrate(JSON.parse(rawFromLocalStorage));
 *
 * Upgrades any stored payload -- current (schemaVersion 3), schemaVersion 2 (screen-local
 * GearItem/GearSlot gear -- structurally identical to v3's content Item/GearSlotId, so it
 * migrates in place), schemaVersion 1 (ilvl-based gear), or unversioned "v0" -- to StoreV3. A v1
 * character's gear used a wholly different shape (ilvl, no tier/facet/charges) so it cannot be
 * carried forward meaningfully and resets to the fixture's tier-1 loadout; a v2 character's gear,
 * compareGear and Ash carry straight through. Every payload gains v3's new fields: talentChoices
 * (free respec -- design/BIBLE.md sec 6) defaults to empty (every forked ability at its base
 * definition), and clauseId (design/BIBLE.md sec 8) defaults to the first of the key level's
 * current three Clause offers.
 */
import { createFixtureCharacter } from "./fixture.js";
import { pickClauseOffers } from "./clauses.js";
import { clampKeyLevel } from "./rules.js";
import {
  GEAR_SLOT_IDS,
  SCHEMA_VERSION,
  type Character,
  type GearSlotId,
  type Item,
  type RunRecord,
  type RunResult,
  type StoreV3,
} from "./types.js";

function isStoreV3(raw: unknown): raw is StoreV3 {
  return (
    typeof raw === "object" &&
    raw !== null &&
    (raw as { schemaVersion?: unknown }).schemaVersion === SCHEMA_VERSION &&
    typeof (raw as { character?: unknown }).character === "object" &&
    (raw as { character?: unknown }).character !== null
  );
}

function extractLegacyCharacter(raw: unknown): Record<string, unknown> | undefined {
  if (typeof raw !== "object" || raw === null || !("character" in raw)) return undefined;
  const character = (raw as { character?: unknown }).character;
  if (typeof character !== "object" || character === null) return undefined;
  return character as Record<string, unknown>;
}

function mapLegacyResult(result: unknown): RunResult {
  if (result === "success" || result === "timed") return "timed";
  if (result === "abandoned") return "abandoned";
  return "depleted";
}

function extractLegacyRunHistory(raw: unknown): RunRecord[] | undefined {
  if (!Array.isArray(raw)) return undefined;
  return raw.map((entry: unknown, index: number): RunRecord => {
    const e = (typeof entry === "object" && entry !== null ? entry : {}) as Record<string, unknown>;
    return {
      id: typeof e.id === "string" ? e.id : `legacy-run-${index}`,
      dungeon: typeof e.dungeon === "string" ? e.dungeon : "Hollow Vault",
      keyLevel: typeof e.keyLevel === "number" ? clampKeyLevel(e.keyLevel) : 2,
      result: mapLegacyResult(e.result),
      durationSec: typeof e.durationSec === "number" ? e.durationSec : 0,
      completedAt: typeof e.completedAt === "number" ? e.completedAt : 0,
      rewardPicked: null,
      ashGained: 0,
    };
  });
}

/** True when `gearRaw`'s items already look like the tiered Item shape (v2 or v3's gear) rather
 * than v1's ilvl gear -- the boundary that decides whether gear/compareGear carry forward. */
function looksLikeItemShapedGear(gearRaw: unknown): gearRaw is Record<GearSlotId, Item> {
  if (typeof gearRaw !== "object" || gearRaw === null) return false;
  const first = Object.values(gearRaw as Record<string, unknown>)[0];
  if (typeof first !== "object" || first === null) return false;
  const item = first as Record<string, unknown>;
  return typeof item.tier === "number" && "facet" in item && typeof item.charges === "number";
}

function migrateGear(legacy: Record<string, unknown>, fixture: Character): Record<GearSlotId, Item> {
  if (!looksLikeItemShapedGear(legacy.gear)) return fixture.gear;
  const source = legacy.gear;
  const gear = {} as Record<GearSlotId, Item>;
  for (const slot of GEAR_SLOT_IDS) gear[slot] = source[slot] ?? fixture.gear[slot];
  return gear;
}

function migrateCompareGear(legacy: Record<string, unknown>): Partial<Record<GearSlotId, Item>> {
  if (!looksLikeItemShapedGear(legacy.gear)) return {}; // v1: gear itself resets, so compare can't line up either
  if (typeof legacy.compareGear !== "object" || legacy.compareGear === null) return {};
  return legacy.compareGear as Partial<Record<GearSlotId, Item>>;
}

export function migrate(raw: unknown, now: number = Date.now()): StoreV3 {
  if (isStoreV3(raw)) return raw;

  const fixture = createFixtureCharacter(now);
  const legacy = extractLegacyCharacter(raw);
  if (!legacy) return { schemaVersion: SCHEMA_VERSION, character: fixture };

  const keyLevel = clampKeyLevel(typeof legacy.keyLevel === "number" ? legacy.keyLevel : fixture.keyLevel);
  const talentChoices =
    typeof legacy.talentChoices === "object" && legacy.talentChoices !== null
      ? (legacy.talentChoices as Record<string, string>)
      : {};
  const clauseId = typeof legacy.clauseId === "string" ? legacy.clauseId : pickClauseOffers(keyLevel)[0].id;
  const recentlyEquipped = Array.isArray(legacy.recentlyEquipped) ? (legacy.recentlyEquipped as GearSlotId[]) : [];

  const character: Character = {
    classId: typeof legacy.classId === "string" ? legacy.classId : fixture.classId,
    level: typeof legacy.level === "number" ? legacy.level : fixture.level,
    talentChoices,
    gear: migrateGear(legacy, fixture),
    compareGear: migrateCompareGear(legacy),
    recentlyEquipped,
    ash: typeof legacy.ash === "number" ? legacy.ash : 0,
    keyLevel,
    clauseId,
    queued: false,
    runHistory: extractLegacyRunHistory(legacy.runHistory) ?? fixture.runHistory,
    rewardChoices: null,
    lastSeenAt: typeof legacy.lastSeenAt === "number" ? legacy.lastSeenAt : fixture.lastSeenAt,
  };
  return { schemaVersion: SCHEMA_VERSION, character };
}
