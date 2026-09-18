/**
 * Usage: import { migrate } from "./migrate.js";
 *        const storeV1 = migrate(JSON.parse(rawFromLocalStorage));
 *
 * Upgrades any stored payload -- current (schemaVersion 1) or legacy/
 * unversioned ("v0") -- to StoreV1, filling gaps from a fresh fixture
 * character rather than crashing on missing fields.
 */
import { createFixtureCharacter } from "./fixture.js";
import { clampKeyLevel } from "./rules.js";
import { GEAR_SLOTS, SCHEMA_VERSION, type Character, type GearItem, type GearSlot, type StoreV1 } from "./types.js";

function isStoreV1(raw: unknown): raw is StoreV1 {
  return (
    typeof raw === "object" &&
    raw !== null &&
    (raw as { schemaVersion?: unknown }).schemaVersion === SCHEMA_VERSION &&
    typeof (raw as { character?: unknown }).character === "object" &&
    (raw as { character?: unknown }).character !== null
  );
}

function extractLegacyCharacter(raw: unknown): Partial<Character> | undefined {
  if (typeof raw !== "object" || raw === null || !("character" in raw)) return undefined;
  const character = (raw as { character?: unknown }).character;
  if (typeof character !== "object" || character === null) return undefined;
  return character as Partial<Character>;
}

function mergeGear(
  fixtureGear: Record<GearSlot, GearItem>,
  legacyGear: Partial<Record<GearSlot, GearItem>> | undefined,
): Record<GearSlot, GearItem> {
  const merged = { ...fixtureGear };
  if (legacyGear) {
    for (const slot of GEAR_SLOTS) {
      const item = legacyGear[slot];
      if (item) merged[slot] = item;
    }
  }
  return merged;
}

export function migrate(raw: unknown, now: number = Date.now()): StoreV1 {
  if (isStoreV1(raw)) return raw;
  const fixture = createFixtureCharacter(now);
  const legacy = extractLegacyCharacter(raw);
  if (!legacy) return { schemaVersion: SCHEMA_VERSION, character: fixture };
  const character: Character = {
    classId: legacy.classId ?? fixture.classId,
    level: legacy.level ?? fixture.level,
    abilities: legacy.abilities ?? fixture.abilities,
    gear: mergeGear(fixture.gear, legacy.gear),
    keyLevel: clampKeyLevel(legacy.keyLevel ?? fixture.keyLevel),
    queued: false,
    runHistory: legacy.runHistory ?? fixture.runHistory,
    lastSeenAt: legacy.lastSeenAt ?? fixture.lastSeenAt,
  };
  return { schemaVersion: SCHEMA_VERSION, character };
}
