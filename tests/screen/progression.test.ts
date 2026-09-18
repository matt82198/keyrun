/**
 * Usage: npm test (vitest picks this up via tests/**\/*.test.ts).
 *
 * Covers progression.ts's run economy: Ash accrual by result, the end-of-run reward roll + pick,
 * and the "since last run" delta line.
 */
import { describe, expect, it } from "vitest";
import {
  ASH_ABANDONED,
  ASH_DEPLETED,
  ASH_TIMED,
  ashForResult,
  completeRun,
  computeRunDelta,
  createFixtureCharacter,
  pickReward,
  type Character,
} from "../../screen/src/store.js";
import type { Item } from "../../content/schema/facet.js";

function makeItem(overrides: Partial<Item> = {}): Item {
  return {
    id: "test-item",
    slot: "weapon",
    tier: 5,
    facet: { id: "warden.weapon.wideningBreak", text: "Guard Break trades range for a much wider cone.", abilityId: "warden.guardBreak" },
    charges: 2,
    ...overrides,
  };
}

describe("ashForResult", () => {
  it("orders Ash by result: timed > depleted > abandoned", () => {
    expect(ashForResult("timed")).toBe(ASH_TIMED);
    expect(ashForResult("depleted")).toBe(ASH_DEPLETED);
    expect(ashForResult("abandoned")).toBe(ASH_ABANDONED);
    expect(ASH_TIMED).toBeGreaterThan(ASH_DEPLETED);
    expect(ASH_DEPLETED).toBeGreaterThan(ASH_ABANDONED);
  });
});

describe("completeRun / pickReward", () => {
  it("banks Ash win or lose and rolls a choice of exactly three", () => {
    const character = createFixtureCharacter(1000);
    const { character: next, rewardChoices } = completeRun(
      character,
      { result: "depleted", keyLevel: 10, durationSec: 240 },
      5000,
    );
    expect(next.ash).toBe(character.ash + ASH_DEPLETED);
    expect(rewardChoices).toHaveLength(3);
    expect(next.runHistory[0]!.result).toBe("depleted");
    expect(next.runHistory[0]!.ashGained).toBe(ASH_DEPLETED);
    expect(next.runHistory[0]!.rewardPicked).toBeNull();
    expect(next.rewardChoices).toHaveLength(3);
  });

  it("pickReward equips (or parks) the chosen item and attaches it to the latest history entry", () => {
    const character = createFixtureCharacter(1000);
    const { character: afterRun } = completeRun(character, { result: "timed", keyLevel: 5, durationSec: 150 }, 6000);
    const choice = afterRun.rewardChoices![0]!;
    const afterPick = pickReward(afterRun, choice.id);
    expect(afterPick.rewardChoices).toBeNull();
    expect(afterPick.runHistory[0]!.rewardPicked?.id).toBe(choice.id);
    const nowOwned =
      afterPick.gear[choice.slot].id === choice.id || afterPick.compareGear[choice.slot]?.id === choice.id;
    expect(nowOwned).toBe(true);
  });

  it("pickReward is a no-op for an id that is not a pending choice", () => {
    const character = createFixtureCharacter(1000);
    const { character: afterRun } = completeRun(character, { result: "timed", keyLevel: 5, durationSec: 150 }, 6000);
    const next = pickReward(afterRun, "not-a-real-id");
    expect(next).toBe(afterRun);
  });
});

describe("computeRunDelta (since-last-run line)", () => {
  it("returns null with no run history", () => {
    const character = createFixtureCharacter(1000);
    expect(computeRunDelta(character)).toBeNull();
  });

  it("flags a new best time, a new tier, and reports Ash gained", () => {
    const character: Character = {
      ...createFixtureCharacter(1000),
      runHistory: [
        {
          id: "r2",
          dungeon: "Hollow Vault",
          keyLevel: 10,
          result: "timed",
          durationSec: 150,
          completedAt: 2000,
          rewardPicked: makeItem({ tier: 6 }),
          ashGained: ASH_TIMED,
        },
        {
          id: "r1",
          dungeon: "Hollow Vault",
          keyLevel: 10,
          result: "depleted",
          durationSec: 200,
          completedAt: 1000,
          rewardPicked: null,
          ashGained: ASH_DEPLETED,
        },
      ],
    };
    const delta = computeRunDelta(character);
    expect(delta).not.toBeNull();
    expect(delta!.newBestTime).toBe(true);
    expect(delta!.newTier).toBe(true);
    expect(delta!.ashGained).toBe(ASH_TIMED);
    expect(delta!.text).toContain("new best time");
    expect(delta!.text).toContain("new tier 6");
    expect(delta!.text).toContain(`+${ASH_TIMED} Ash`);
  });

  it("does not claim a new best time when the latest run was slower", () => {
    const character: Character = {
      ...createFixtureCharacter(1000),
      runHistory: [
        {
          id: "r2",
          dungeon: "Hollow Vault",
          keyLevel: 10,
          result: "depleted",
          durationSec: 300,
          completedAt: 2000,
          rewardPicked: null,
          ashGained: ASH_DEPLETED,
        },
        {
          id: "r1",
          dungeon: "Hollow Vault",
          keyLevel: 10,
          result: "timed",
          durationSec: 150,
          completedAt: 1000,
          rewardPicked: null,
          ashGained: ASH_TIMED,
        },
      ],
    };
    const delta = computeRunDelta(character);
    expect(delta!.newBestTime).toBe(false);
    expect(delta!.newTier).toBe(false);
    expect(delta!.text).not.toContain("new best time");
  });
});
