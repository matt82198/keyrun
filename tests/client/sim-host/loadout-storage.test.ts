/**
 * tests/client/sim-host/loadout-storage.test.ts
 *
 * run-options-wiring: loadout-storage.ts's `readPersistedLoadout` reads the SAME localStorage key
 * screen/src/store.ts writes (`"keyrun.screen.store.v2"`, mirrored here as `SCREEN_STORAGE_KEY` --
 * this test pins that literal so a drift in either file is caught), driven with a fake storage
 * (no real browser/DOM localStorage in this test environment).
 */
import { describe, expect, it } from "vitest";
import { readPersistedLoadout, SCREEN_STORAGE_KEY } from "../../../client/src/sim-host/loadout-storage.js";

class FakeStorage {
  private readonly data = new Map<string, string>();
  setItem(key: string, value: string): void {
    this.data.set(key, value);
  }
  getItem(key: string): string | null {
    return this.data.get(key) ?? null;
  }
}

const REAL_ITEM = { id: "item-1", slot: "weapon" as const, tier: 3, facet: { id: "facet-1", text: "does a thing", abilityId: "warden.strike" }, charges: 2 };

function storeWith(character: unknown): FakeStorage {
  const storage = new FakeStorage();
  storage.setItem(SCREEN_STORAGE_KEY, JSON.stringify({ schemaVersion: 3, character }));
  return storage;
}

describe("loadout-storage: readPersistedLoadout", () => {
  it("returns null when storage is absent/null/undefined", () => {
    expect(readPersistedLoadout(undefined)).toBeNull();
    expect(readPersistedLoadout(null)).toBeNull();
  });

  it("returns null when the key is missing (fresh visitor)", () => {
    expect(readPersistedLoadout(new FakeStorage())).toBeNull();
  });

  it("returns null for malformed JSON, never throws", () => {
    const storage = new FakeStorage();
    storage.setItem(SCREEN_STORAGE_KEY, "not json {{{");
    expect(readPersistedLoadout(storage)).toBeNull();
  });

  it("returns null when the record has no character", () => {
    const storage = new FakeStorage();
    storage.setItem(SCREEN_STORAGE_KEY, JSON.stringify({ schemaVersion: 3 }));
    expect(readPersistedLoadout(storage)).toBeNull();
  });

  it("reads real talentChoices and only the gear slots with a real Item shape", () => {
    const storage = storeWith({
      talentChoices: { "warden.strike": "riposte" },
      gear: { weapon: REAL_ITEM, armor: { garbage: true }, charm: null },
    });
    const result = readPersistedLoadout(storage);
    expect(result).not.toBeNull();
    expect(result!.talentChoices).toEqual({ "warden.strike": "riposte" });
    expect(result!.gear).toEqual([REAL_ITEM]); // armor/charm/ring/keystone dropped (not a real Item)
  });

  it("defaults talentChoices to {} and gear to [] when absent", () => {
    const storage = storeWith({});
    const result = readPersistedLoadout(storage);
    expect(result).toEqual({ talentChoices: {}, gear: [] });
  });

  it("never throws even if storage.getItem itself throws (private-mode edge case)", () => {
    const throwing = { getItem: () => { throw new Error("blocked"); } };
    expect(readPersistedLoadout(throwing)).toBeNull();
  });
});
