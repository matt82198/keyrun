/**
 * Usage: npm test (vitest picks this up via tests/**\/*.test.ts).
 *
 * Covers the store.ts contract: legacy -> schemaVersion 3 migration (v1's ilvl gear, v2's
 * screen-local GearItem/GearSlot gear, and v3 passthrough), loadout "new" delta flags, key-stepper
 * clamp/lock, and run-history capping. The Clause pick and buildPlayUrl live in clauses.test.ts.
 */
import { describe, expect, it } from "vitest";
import {
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
  it("upgrades a schemaVersion 1 (ilvl-based gear) payload to schemaVersion 3", () => {
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

    expect(result.schemaVersion).toBe(3);
    expect(result.character.classId).toBe("ranger");
    expect(result.character.level).toBe(3);
    expect(result.character.keyLevel).toBe(8);
    expect(result.character.queued).toBe(false);
    expect(result.character.ash).toBe(0);
    expect(result.character.talentChoices).toEqual({});
    expect(typeof result.character.clauseId).toBe("string");
    // legacy ilvl gear shape cannot carry forward -- resets to the tiered fixture loadout.
    expect(result.character.gear.weapon.tier).toBe(1);
    expect(result.character.gear.keystone).toBeDefined();
    // legacy "success"/"failure" results map onto the new timed/depleted/abandoned vocabulary.
    expect(result.character.runHistory[0]!.result).toBe("timed");
    expect(result.character.runHistory[0]!.rewardPicked).toBeNull();
  });

  it("upgrades an old-shape schemaVersion 2 localStorage blob (screen-local GearItem gear, no talentChoices/clauseId) to 3", () => {
    const v2 = {
      schemaVersion: 2,
      character: {
        classId: "warden",
        level: 10,
        gear: {
          weapon: {
            id: "weapon-fixture",
            slot: "weapon",
            tier: 4,
            facet: { id: "warden.weapon.keenEdge", text: "Strike becomes a narrow frontal cone, catching an extra enemy.", abilityId: "warden.strike" },
            charges: 1,
          },
          armor: { id: "armor-fixture", slot: "armor", tier: 1, facet: { id: "a", text: "t", abilityId: "warden.brace" }, charges: 2 },
          charm: { id: "charm-fixture", slot: "charm", tier: 1, facet: { id: "b", text: "t", abilityId: "warden.taunt" }, charges: 2 },
          ring: { id: "ring-fixture", slot: "ring", tier: 1, facet: { id: "c", text: "t", abilityId: "warden.charge" }, charges: 2 },
          keystone: { id: "keystone-fixture", slot: "keystone", tier: 1, facet: { id: "d", text: "t", abilityId: "warden.vigil" }, charges: 2 },
        },
        compareGear: {},
        recentlyEquipped: [],
        ash: 120,
        keyLevel: 9,
        queued: false,
        runHistory: [],
        rewardChoices: null,
        lastSeenAt: 4000,
      },
    };
    const result = migrate(v2, 5000);

    expect(result.schemaVersion).toBe(3);
    // v2 gear carries straight through -- it is structurally the same Item shape as v3's.
    expect(result.character.gear.weapon.id).toBe("weapon-fixture");
    expect(result.character.gear.weapon.tier).toBe(4);
    expect(result.character.gear.weapon.facet?.id).toBe("warden.weapon.keenEdge");
    expect(result.character.ash).toBe(120);
    // new v3 fields are backfilled with valid defaults.
    expect(result.character.talentChoices).toEqual({});
    expect(typeof result.character.clauseId).toBe("string");
  });

  it("is a no-op for an already-current v3 payload", () => {
    const current = { schemaVersion: 3 as const, character: createFixtureCharacter(1000) };
    expect(migrate(current, 2000)).toBe(current);
  });

  it("falls back to a fresh fixture for garbage input", () => {
    const result = migrate({ nonsense: true }, 5000);
    expect(result.schemaVersion).toBe(3);
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
