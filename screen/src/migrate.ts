/**
 * Usage: import { migrate } from "./migrate.js";
 *        const storeV2 = migrate(JSON.parse(rawFromLocalStorage));
 *
 * Upgrades any stored payload -- current (schemaVersion 2) or legacy (schemaVersion 1's ilvl-based
 * gear, or unversioned "v0") -- to StoreV2. Pre-v2 gear used a wholly different item shape (ilvl, no
 * tier/facet/charges) so it cannot be carried forward meaningfully; a legacy character's classId,
 * level, keyLevel and run history are preserved, gear resets to the fixture's tier-1 loadout, and Ash
 * starts at zero (a real player never existed before schemaVersion 2, this repo has none).
 */
import { createFixtureCharacter } from "./fixture.js";
import { clampKeyLevel } from "./rules.js";
import { SCHEMA_VERSION, type Character, type RunRecord, type RunResult, type StoreV2 } from "./types.js";

function isStoreV2(raw: unknown): raw is StoreV2 {
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

export function migrate(raw: unknown, now: number = Date.now()): StoreV2 {
  if (isStoreV2(raw)) return raw;

  const fixture = createFixtureCharacter(now);
  const legacy = extractLegacyCharacter(raw);
  if (!legacy) return { schemaVersion: SCHEMA_VERSION, character: fixture };

  const character: Character = {
    classId: typeof legacy.classId === "string" ? legacy.classId : fixture.classId,
    level: typeof legacy.level === "number" ? legacy.level : fixture.level,
    abilities: fixture.abilities,
    gear: fixture.gear,
    compareGear: {},
    recentlyEquipped: [],
    ash: 0,
    keyLevel: clampKeyLevel(typeof legacy.keyLevel === "number" ? legacy.keyLevel : fixture.keyLevel),
    queued: false,
    runHistory: extractLegacyRunHistory(legacy.runHistory) ?? fixture.runHistory,
    rewardChoices: null,
    lastSeenAt: typeof legacy.lastSeenAt === "number" ? legacy.lastSeenAt : fixture.lastSeenAt,
  };
  return { schemaVersion: SCHEMA_VERSION, character };
}
