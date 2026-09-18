/**
 * tests/content/facets.test.ts
 * Usage: `npm test` (vitest). BIBLE.md Sec 7 "Gear and progression": every gear item carries one
 * Facet, and "Facets change function ... never a flat number". Confirms the facet pools
 * (content/data/facets/*.json) are well-formed: loaded without throwing by content/index.ts, at
 * least 4 facets per slot per class, every facet references a real ability id belonging to that
 * class, no facet patch renames/adds an ability id, and every facet's patch changes function
 * (not a bare numeric field).
 */
import { describe, expect, it } from "vitest";
import { loadContent, validate } from "../../content/index";
import { GEAR_SLOT_IDS, type FacetDef } from "../../content/schema/facet";

const CLASS_IDS = ["warden", "cinder", "chanter"] as const;

function facetsForClass(all: FacetDef[], classAbilityIds: Set<string>): FacetDef[] {
  return all.filter((f) => classAbilityIds.has(f.abilityId));
}

describe("facet pools", () => {
  it("load without throwing as part of loadContent()", () => {
    expect(() => loadContent()).not.toThrow();
  });

  it("has at least 4 facets per slot for each of the 3 classes", () => {
    const reg = loadContent();
    const allFacets = [...reg.facets.values()];
    for (const classId of CLASS_IDS) {
      const cls = reg.classes.get(classId)!;
      const slots = cls.abilities as unknown as Record<string, string>;
      const classAbilityIds = new Set(Object.values(slots));
      const own = facetsForClass(allFacets, classAbilityIds);
      for (const slot of GEAR_SLOT_IDS) {
        const count = own.filter((f) => f.slot === slot).length;
        expect(count, `${classId} slot ${slot} facet count`).toBeGreaterThanOrEqual(4);
      }
    }
  });

  it("every facet references a real ability id (checked by loadContent's validator)", () => {
    const reg = loadContent();
    for (const facet of reg.facets.values()) {
      expect(reg.abilities.has(facet.abilityId), `facet "${facet.id}" ability "${facet.abilityId}"`).toBe(true);
    }
  });

  it("no facet patch sets an ability id (would rename/add an ability)", () => {
    const reg = loadContent();
    for (const facet of reg.facets.values()) {
      expect("id" in facet.patch, `facet "${facet.id}"`).toBe(false);
    }
  });

  it("has no duplicate facet ids across the 3 class pools", () => {
    const reg = loadContent();
    const seen = new Set<string>();
    for (const facet of reg.facets.values()) {
      expect(seen.has(facet.id), `duplicate facet id "${facet.id}"`).toBe(false);
      seen.add(facet.id);
    }
  });

  it("every facet patch changes function, not a bare numeric tweak", () => {
    const reg = loadContent();
    const structuredOrFunctionKeys = new Set([
      "tagAdd", "tagRemove", "effectsAppend", "shape", "tags", "effects", "variants",
    ]);
    for (const facet of reg.facets.values()) {
      const keys = Object.keys(facet.patch as Record<string, unknown>);
      const hasFunctionChange = keys.some((k) => structuredOrFunctionKeys.has(k));
      expect(hasFunctionChange, `facet "${facet.id}" patch keys: ${keys.join(",")}`).toBe(true);
    }
  });

  it("throws when content/index.ts validation catches a facet with an unknown ability ref", () => {
    const reg = loadContent();
    const [firstFacet] = [...reg.facets.values()];
    expect(firstFacet).toBeDefined();
    reg.facets.get(firstFacet!.id)!.abilityId = "nonexistent.ability";
    expect(() => validate(reg)).toThrow(/unknown ability/);
  });
});
