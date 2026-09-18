/**
 * Usage: npm test (vitest picks this up via tests/**\/*.test.ts).
 *
 * Covers sim/encounter/rewards.ts: determinism, never two identical choices, and the key/result tier
 * band rules -- including the "hard BiS" gate (tiers 9-10 only on a timed clear at key >= 15).
 */
import { describe, expect, it } from "vitest";
import { GEAR_SLOTS, HARD_BIS_MIN_KEY, rollRewardChoices, type GearSlot } from "@sim/encounter/rewards";

describe("rollRewardChoices: determinism", () => {
  it("is a pure function of its inputs -- same args always produce the same three items", () => {
    const a = rollRewardChoices(42, 10, "timed", GEAR_SLOTS);
    const b = rollRewardChoices(42, 10, "timed", GEAR_SLOTS);
    expect(a).toEqual(b);
  });

  it("changes with the seed", () => {
    const a = rollRewardChoices(1, 10, "timed", GEAR_SLOTS);
    const b = rollRewardChoices(2, 10, "timed", GEAR_SLOTS);
    expect(a).not.toEqual(b);
  });
});

describe("rollRewardChoices: never two identical", () => {
  it("never returns two choices with the same slot+facet+tier, across many seeds", () => {
    for (let seed = 0; seed < 200; seed += 1) {
      const [a, b, c] = rollRewardChoices(seed, 15, "timed", GEAR_SLOTS);
      const key = (item: { slot: string; facet: { id: string }; tier: number }) =>
        `${item.slot}:${item.facet.id}:${item.tier}`;
      const keys = new Set([key(a), key(b), key(c)]);
      expect(keys.size).toBe(3);
    }
  });
});

describe("rollRewardChoices: key/result tier band rules", () => {
  it("never rolls tier above 8 on a low key even across many seeds", () => {
    for (let seed = 0; seed < 100; seed += 1) {
      const choices = rollRewardChoices(seed, 3, "timed", GEAR_SLOTS);
      for (const item of choices) expect(item.tier).toBeLessThanOrEqual(8);
    }
  });

  it("never rolls the hard-BiS tiers (9-10) on a depleted or abandoned result, even at a high key", () => {
    for (let seed = 0; seed < 100; seed += 1) {
      for (const result of ["depleted", "abandoned"] as const) {
        const choices = rollRewardChoices(seed, 20, result, GEAR_SLOTS);
        for (const item of choices) expect(item.tier).toBeLessThanOrEqual(8);
      }
    }
  });

  it(`never rolls the hard-BiS tiers below key ${HARD_BIS_MIN_KEY}, even on a timed clear`, () => {
    for (let seed = 0; seed < 100; seed += 1) {
      const choices = rollRewardChoices(seed, HARD_BIS_MIN_KEY - 1, "timed", GEAR_SLOTS);
      for (const item of choices) expect(item.tier).toBeLessThanOrEqual(8);
    }
  });

  it(`can roll tiers 9-10 on a timed clear at key >= ${HARD_BIS_MIN_KEY} (hard BiS)`, () => {
    let sawHardBis = false;
    for (let seed = 0; seed < 300; seed += 1) {
      const choices = rollRewardChoices(seed, 20, "timed", GEAR_SLOTS);
      if (choices.some((item) => item.tier >= 9)) {
        sawHardBis = true;
        break;
      }
    }
    expect(sawHardBis).toBe(true);
  });

  it("every rolled tier stays within [1,10] and every slot is one of the five gear slots", () => {
    const validSlots = new Set<GearSlot>(GEAR_SLOTS);
    for (let seed = 0; seed < 50; seed += 1) {
      const choices = rollRewardChoices(seed, 18, "timed", GEAR_SLOTS);
      for (const item of choices) {
        expect(item.tier).toBeGreaterThanOrEqual(1);
        expect(item.tier).toBeLessThanOrEqual(10);
        expect(validSlots.has(item.slot)).toBe(true);
        expect(item.charges).toBe(2);
      }
    }
  });
});
