/**
 * Usage:
 *   import { loadStore, saveStore, markLoadoutSeen, buildPlayUrl, clampKeyLevel,
 *            isKeyLocked, getNewGearSlots, recordRun, migrate, createFixtureCharacter,
 *            type Character, type StoreV1 } from "./store.js";
 *   const store = loadStore();
 *   // ...mutate store.character, then:
 *   saveStore(store);
 *
 * Versioned localStorage-backed store for the Keyrun character + queue screen.
 * Data shapes live in types.ts, pure business rules in rules.ts, the
 * placeholder character in fixture.ts, and upgrade logic in migrate.ts; this
 * module is the single public entry point -- persistence plus re-exports --
 * that the rest of screen/ and the tests import from.
 */
export * from "./types.js";
export * from "./rules.js";
export { createFixtureCharacter } from "./fixture.js";
export { migrate } from "./migrate.js";

import { createFixtureCharacter } from "./fixture.js";
import { migrate } from "./migrate.js";
import { SCHEMA_VERSION, type StoreV1 } from "./types.js";

export const STORAGE_KEY = "keyrun.screen.store.v1";

function hasLocalStorage(): boolean {
  try {
    return typeof localStorage !== "undefined";
  } catch {
    return false;
  }
}

export function loadStore(now: number = Date.now()): StoreV1 {
  if (!hasLocalStorage()) return { schemaVersion: SCHEMA_VERSION, character: createFixtureCharacter(now) };
  const raw = localStorage.getItem(STORAGE_KEY);
  if (!raw) {
    const fresh = { schemaVersion: SCHEMA_VERSION, character: createFixtureCharacter(now) } as const;
    saveStore(fresh);
    return fresh;
  }
  try {
    const migrated = migrate(JSON.parse(raw), now);
    saveStore(migrated);
    return migrated;
  } catch {
    const fresh = { schemaVersion: SCHEMA_VERSION, character: createFixtureCharacter(now) } as const;
    saveStore(fresh);
    return fresh;
  }
}

export function saveStore(store: StoreV1): void {
  if (!hasLocalStorage()) return;
  localStorage.setItem(STORAGE_KEY, JSON.stringify(store));
}
