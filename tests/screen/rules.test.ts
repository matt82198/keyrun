/**
 * Usage: npm test (vitest picks this up via tests/**\/*.test.ts).
 *
 * Covers rules.ts's core gear contract: tier clamp and the auto-equip/compare rule. Crafting
 * lives in craft.test.ts, the Ash/reward economy in progression.test.ts, talents in
 * talents.test.ts, and the Clause pick in clauses.test.ts -- split to mirror screen/src/'s split.
 */
import { describe, expect, it, vi } from "vitest";
import {
  applyGearPick,
  autoEquip,
  clampItemTier,
  createFixtureCharacter,
  discardCompare,
  equipCompare,
  type Item,
} from "../../screen/src/store.js";

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
