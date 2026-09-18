/**
 * Usage: npm test (vitest picks this up via tests/**\/*.test.ts).
 *
 * Covers the store.ts contract: legacy -> schemaVersion 2 migration, loadout "new" delta flags,
 * key-stepper clamp/lock, run-history capping, and the Play URL shape.
 */
import { describe, expect, it } from "vitest";
import {
  buildPlayUrl,
  clampKeyLevel,
  createFixtureCharacter,
  getNewGearSlots,
  isKeyLocked,
  markLoadoutSeen,
  migrate,
  recordRun,
  type RunRecord,
} from "../../screen/src/store.js";

describe("migrate", () => {
  it("upgrades a schemaVersion 1 (ilvl-based gear) payload to schemaVersion 2", () => {
    const v1 = {
      schemaVersion: 1,
      character: {
        classId: "ranger",
        level: 3,
        keyLevel: 8,
        gear: { weapon: { id: "w0", name: "Rusty Sword", ilvl: 5, acquiredAt: 1000 } },
        runHistory: [
          { id: "r1", dungeon: "Hollow Vault", keyLevel: 8, result: "success", durationSec: 200, completedAt: 900 },
        ],
      },
    };
    const result = migrate(v1, 5000);

    expect(result.schemaVersion).toBe(2);
    expect(result.character.classId).toBe("ranger");
    expect(result.character.level).toBe(3);
    expect(result.character.keyLevel).toBe(8);
    expect(result.character.queued).toBe(false);
    expect(result.character.ash).toBe(0);
    // legacy gear shape (ilvl) cannot carry forward -- resets to the tiered fixture loadout.
    expect(result.character.gear.weapon.tier).toBe(1);
    expect(result.character.gear.keystone).toBeDefined();
    // legacy "success"/"failure" results map onto the new timed/depleted/abandoned vocabulary.
    expect(result.character.runHistory[0]!.result).toBe("timed");
    expect(result.character.runHistory[0]!.rewardPicked).toBeNull();
  });

  it("is a no-op for an already-current v2 payload", () => {
    const current = { schemaVersion: 2 as const, character: createFixtureCharacter(1000) };
    expect(migrate(current, 2000)).toBe(current);
  });

  it("falls back to a fresh fixture for garbage input", () => {
    const result = migrate({ nonsense: true }, 5000);
    expect(result.schemaVersion).toBe(2);
    expect(result.character.classId).toBe("warden");
    expect(result.character.gear.weapon.tier).toBe(1);
  });
});

describe("getNewGearSlots / markLoadoutSeen", () => {
  it("flags only slots auto-equipped since the loadout was last viewed", () => {
    const character = createFixtureCharacter(10000);
    character.recentlyEquipped = ["ring"];
    expect(getNewGearSlots(character)).toEqual(["ring"]);
  });

  it("clears the flags once the loadout has been seen", () => {
    const character = createFixtureCharacter(10000);
    character.recentlyEquipped = ["ring", "charm"];
    const seen = markLoadoutSeen(character);
    expect(getNewGearSlots(seen)).toEqual([]);
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
        result: "timed",
        durationSec: 100,
        completedAt: i,
        rewardPicked: null,
        ashGained: 50,
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
