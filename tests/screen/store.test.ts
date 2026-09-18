/**
 * Usage: npm test (vitest picks this up via tests/**\/*.test.ts).
 *
 * Covers the store.ts contract: v0->v1 migration, loadout delta flags,
 * key-stepper clamp/lock, run-history capping, and the Play URL shape.
 */
import { describe, expect, it } from "vitest";
import {
  buildPlayUrl,
  clampKeyLevel,
  createFixtureCharacter,
  getNewGearSlots,
  isKeyLocked,
  migrate,
  recordRun,
  type RunRecord,
} from "../../screen/src/store.js";

describe("migrate", () => {
  it("upgrades an unversioned (v0) payload to schemaVersion 1, filling gaps from the fixture", () => {
    const v0 = {
      character: {
        classId: "ranger",
        level: 3,
        gear: { weapon: { id: "w0", name: "Rusty Sword", ilvl: 5, acquiredAt: 1000 } },
      },
    };
    const result = migrate(v0, 5000);

    expect(result.schemaVersion).toBe(1);
    expect(result.character.classId).toBe("ranger");
    expect(result.character.level).toBe(3);
    expect(result.character.queued).toBe(false);
    expect(result.character.gear.weapon.name).toBe("Rusty Sword");
    expect(result.character.gear.armor).toBeDefined();
    expect(result.character.gear.ring1).toBeDefined();
  });

  it("is a no-op for an already-current v1 payload", () => {
    const current = { schemaVersion: 1 as const, character: createFixtureCharacter(1000) };
    expect(migrate(current, 2000)).toBe(current);
  });

  it("falls back to a fresh fixture for garbage input", () => {
    const result = migrate({ nonsense: true }, 5000);
    expect(result.schemaVersion).toBe(1);
    expect(result.character.classId).toBe("warden");
  });
});

describe("getNewGearSlots", () => {
  it("flags only gear acquired after lastSeenAt", () => {
    const character = createFixtureCharacter(10000);
    character.lastSeenAt = 5000;
    character.gear.ring1.acquiredAt = 6000;
    character.gear.weapon.acquiredAt = 4000;

    const newSlots = getNewGearSlots(character);
    expect(newSlots).toContain("ring1");
    expect(newSlots).not.toContain("weapon");
  });

  it("flags nothing once lastSeenAt catches up to every item", () => {
    const character = createFixtureCharacter(10000);
    character.lastSeenAt = 10000;
    expect(getNewGearSlots(character)).toEqual([]);
  });
});

describe("clampKeyLevel / isKeyLocked", () => {
  it("clamps below the floor and above the ceiling", () => {
    expect(clampKeyLevel(0)).toBe(2);
    expect(clampKeyLevel(-5)).toBe(2);
    expect(clampKeyLevel(25)).toBe(20);
    expect(clampKeyLevel(7)).toBe(7);
  });

  it("locks the stepper only while queued", () => {
    expect(isKeyLocked({ queued: true })).toBe(true);
    expect(isKeyLocked({ queued: false })).toBe(false);
  });
});

describe("recordRun", () => {
  it("caps run history at five, newest first", () => {
    let history: RunRecord[] = [];
    for (let i = 0; i < 7; i += 1) {
      history = recordRun(history, {
        id: `r${i}`,
        dungeon: "Hollow Vault",
        keyLevel: 5,
        result: "success",
        durationSec: 100,
        completedAt: i,
      });
    }
    expect(history.length).toBe(5);
    expect(history[0]!.id).toBe("r6");
    expect(history[4]!.id).toBe("r2");
  });
});

describe("buildPlayUrl", () => {
  it("contains a numeric seed and the clamped key level", () => {
    const character = createFixtureCharacter(1000);
    const url = buildPlayUrl(character, 12);
    expect(url).toMatch(/^\.\.\/client\/\?seed=\d+&key=12$/);
  });

  it("clamps an out-of-range key level before building the url", () => {
    const character = createFixtureCharacter(1000);
    const url = buildPlayUrl(character, 99);
    expect(url).toContain("key=20");
  });
});
