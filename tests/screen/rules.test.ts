/**
 * Usage: npm test (vitest picks this up via tests/**\/*.test.ts).
 *
 * Covers rules.ts's progression-loop contract: gear tier clamp, the auto-equip/compare rule,
 * crafting charge exhaustion, Ash accrual by result, the end-of-run reward roll + pick, run-history
 * reward attachment, and the "since last run" delta line.
 */
import { describe, expect, it, vi } from "vitest";
import {
  ASH_ABANDONED,
  ASH_DEPLETED,
  ASH_TIMED,
  applyGearPick,
  ashForResult,
  autoEquip,
  clampItemTier,
  completeRun,
  computeRunDelta,
  createFixtureCharacter,
  discardCompare,
  equipCompare,
  pickReward,
  rerollFacet,
  type Character,
  type GearItem,
} from "../../screen/src/store.js";

function makeItem(overrides: Partial<GearItem> = {}): GearItem {
  return {
    id: "test-item",
    slot: "weapon",
    tier: 5,
    facet: { id: "weapon-haft", text: "Strike's Window lasts 1s longer", abilityId: "ability-1" },
    charges: 2,
    ...overrides,
  };
}

describe("clampItemTier", () => {
  it("passes through in-range tiers untouched", () => {
    expect(clampItemTier(1)).toBe(1);
    expect(clampItemTier(10)).toBe(10);
    expect(clampItemTier(7)).toBe(7);
  });

  it("clamps and logs when a write goes above the cap", () => {
    const warn = vi.spyOn(console, "warn").mockImplementation(() => {});
    expect(clampItemTier(11)).toBe(10);
    expect(clampItemTier(99)).toBe(10);
    expect(warn).toHaveBeenCalledTimes(2);
    warn.mockRestore();
  });

  it("clamps below one", () => {
    const warn = vi.spyOn(console, "warn").mockImplementation(() => {});
    expect(clampItemTier(0)).toBe(1);
    expect(clampItemTier(-3)).toBe(1);
    warn.mockRestore();
  });
});

describe("autoEquip", () => {
  it("equips an incoming item of strictly higher tier", () => {
    const character = createFixtureCharacter(1000);
    const incoming = makeItem({ id: "new-weapon", tier: 3 }); // fixture weapon is tier 1
    const result = autoEquip(character.gear, incoming);
    expect(result.equipped).toBe(true);
    expect(result.gear.weapon.id).toBe("new-weapon");
    expect(result.compare).toBeNull();
  });

  it("keeps the current item and flags an equal-tier incoming item as compare", () => {
    const character = createFixtureCharacter(1000);
    const incoming = makeItem({ id: "tied-weapon", tier: character.gear.weapon.tier });
    const result = autoEquip(character.gear, incoming);
    expect(result.equipped).toBe(false);
    expect(result.gear.weapon.id).toBe(character.gear.weapon.id); // untouched -- never overwrites a chosen facet
    expect(result.compare?.id).toBe("tied-weapon");
  });

  it("discards a lower-tier incoming item entirely", () => {
    const character = createFixtureCharacter(1000);
    // bump equipped tier so an incoming tier-1 item is strictly lower
    character.gear.weapon = makeItem({ id: "current", tier: 4 });
    const incoming = makeItem({ id: "worse", tier: 1 });
    const result = autoEquip(character.gear, incoming);
    expect(result.equipped).toBe(false);
    expect(result.compare).toBeNull();
    expect(result.gear.weapon.id).toBe("current");
  });

  it("clamps an incoming item's tier above the cap before comparing", () => {
    const warn = vi.spyOn(console, "warn").mockImplementation(() => {});
    const character = createFixtureCharacter(1000);
    const incoming = makeItem({ id: "overtiered", tier: 15 });
    const result = autoEquip(character.gear, incoming);
    expect(result.gear.weapon.tier).toBe(10);
    warn.mockRestore();
  });
});

describe("applyGearPick / equipCompare / discardCompare", () => {
  it("equips via applyGearPick when strictly higher tier", () => {
    const character = createFixtureCharacter(1000);
    const incoming = makeItem({ id: "picked", tier: 9 });
    const next = applyGearPick(character, incoming);
    expect(next.gear.weapon.id).toBe("picked");
    expect(next.recentlyEquipped).toContain("weapon");
  });

  it("parks a tie in compareGear instead of auto-replacing the chosen facet", () => {
    const character = createFixtureCharacter(1000);
    const incoming = makeItem({ id: "tied", tier: character.gear.weapon.tier });
    const next = applyGearPick(character, incoming);
    expect(next.gear.weapon.id).toBe(character.gear.weapon.id);
    expect(next.compareGear.weapon?.id).toBe("tied");
  });

  it("equipCompare promotes the parked item and clears the compare slot", () => {
    let character = createFixtureCharacter(1000);
    character = applyGearPick(character, makeItem({ id: "tied", tier: character.gear.weapon.tier }));
    character = equipCompare(character, "weapon");
    expect(character.gear.weapon.id).toBe("tied");
    expect(character.compareGear.weapon).toBeUndefined();
  });

  it("discardCompare clears the parked item and keeps the equipped one", () => {
    let character = createFixtureCharacter(1000);
    const originalId = character.gear.weapon.id;
    character = applyGearPick(character, makeItem({ id: "tied", tier: character.gear.weapon.tier }));
    character = discardCompare(character, "weapon");
    expect(character.gear.weapon.id).toBe(originalId);
    expect(character.compareGear.weapon).toBeUndefined();
  });
});

describe("rerollFacet (crafting)", () => {
  it("consumes one of two charges and produces a facet from the slot's pool", () => {
    let character = createFixtureCharacter(1000);
    const before = character.gear.weapon;
    expect(before.charges).toBe(2);
    character = rerollFacet(character, before.id);
    expect(character.gear.weapon.charges).toBe(1);
    expect(character.gear.weapon.slot).toBe("weapon");
  });

  it("is deterministic: same character + item state rerolls to the same facet", () => {
    const character = createFixtureCharacter(1000);
    const a = rerollFacet(character, character.gear.weapon.id);
    const b = rerollFacet(character, character.gear.weapon.id);
    expect(a.gear.weapon.facet.id).toBe(b.gear.weapon.facet.id);
  });

  it("refuses once charges reach zero", () => {
    let character = createFixtureCharacter(1000);
    const id = character.gear.weapon.id;
    character = rerollFacet(character, id);
    character = rerollFacet(character, id);
    expect(character.gear.weapon.charges).toBe(0);
    const facetBeforeRefusal = character.gear.weapon.facet;
    character = rerollFacet(character, id);
    expect(character.gear.weapon.charges).toBe(0);
    expect(character.gear.weapon.facet).toBe(facetBeforeRefusal);
  });

  it("is a no-op for an unknown item id", () => {
    const character = createFixtureCharacter(1000);
    const next = rerollFacet(character, "does-not-exist");
    expect(next).toBe(character);
  });
});

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
