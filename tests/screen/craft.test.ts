/**
 * Usage: npm test (vitest picks this up via tests/**\/*.test.ts).
 *
 * Covers craft.ts: the charge-gated random Facet reroll (now sourced from content's real
 * per-class Facet pool instead of the old screen-local mirror) and the free, unlimited Facet
 * picker (the Loadout band's build-lab swap -- crafting charges/Ash gate acquiring a fresh random
 * roll, not swapping to a Facet the pool already publishes).
 */
import { describe, expect, it } from "vitest";
import { createFixtureCharacter, getFacetsForSlot, pickFacet, rerollFacet } from "../../screen/src/store.js";

describe("rerollFacet (crafting)", () => {
  it("consumes one of two charges and produces a facet from the class's real slot pool", () => {
    let character = createFixtureCharacter(1000);
    const before = character.gear.weapon;
    expect(before.charges).toBe(2);
    character = rerollFacet(character, before.id);
    expect(character.gear.weapon.charges).toBe(1);
    expect(character.gear.weapon.slot).toBe("weapon");
    const pool = getFacetsForSlot("warden", "weapon").map((f) => f.id);
    expect(pool).toContain(character.gear.weapon.facet!.id);
  });

  it("is deterministic: same character + item state rerolls to the same facet", () => {
    const character = createFixtureCharacter(1000);
    const a = rerollFacet(character, character.gear.weapon.id);
    const b = rerollFacet(character, character.gear.weapon.id);
    expect(a.gear.weapon.facet!.id).toBe(b.gear.weapon.facet!.id);
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

describe("pickFacet (free, unlimited Loadout picker)", () => {
  it("sets the equipped item's facet without touching charges", () => {
    const character = createFixtureCharacter(1000);
    const pool = getFacetsForSlot("warden", "weapon");
    const target = pool.find((f) => f.id !== character.gear.weapon.facet?.id)!;
    const chargesBefore = character.gear.weapon.charges;

    const next = pickFacet(character, character.gear.weapon.id, target.id);
    expect(next.gear.weapon.facet?.id).toBe(target.id);
    expect(next.gear.weapon.facet?.text).toBe(target.text);
    expect(next.gear.weapon.charges).toBe(chargesBefore);
  });

  it("persists across further reads of the same character object", () => {
    const character = createFixtureCharacter(1000);
    const pool = getFacetsForSlot("warden", "armor");
    const target = pool[pool.length - 1]!;
    const next = pickFacet(character, character.gear.armor.id, target.id);
    expect(next.gear.armor.facet?.id).toBe(target.id);
    // re-reading the same field gives the same, already-persisted value (no hidden re-roll).
    expect(next.gear.armor.facet?.id).toBe(target.id);
  });

  it("is a no-op for a facet id outside that slot's class pool", () => {
    const character = createFixtureCharacter(1000);
    const next = pickFacet(character, character.gear.weapon.id, "not-a-real-facet");
    expect(next).toBe(character);
  });

  it("is a no-op for an unknown item id", () => {
    const character = createFixtureCharacter(1000);
    const next = pickFacet(character, "does-not-exist", "warden.weapon.keenEdge");
    expect(next).toBe(character);
  });
});
