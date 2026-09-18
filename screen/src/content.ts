/**
 * Usage: import { CLASS_IDS, getClassDef, getAbilityRegistry, getFacetsForSlot, getClauses,
 *                  DUNGEON_ID } from "./content.js";
 *
 * Browser-safe mirror of content/index.ts's registries, scoped to what the screen needs: the
 * three playable classes' abilities/talents, their gear Facets, and the four Clauses.
 * content/index.ts's loadContent() reads files via node:fs and can never be bundled into
 * screen/src/main.ts (scripts/build.mjs bundles it for the browser, `external: ["three"]` only) --
 * this module imports the same JSON under content/data/** directly instead (esbuild's native
 * JSON loader; verified this type-checks under this repo's tsconfig without resolveJsonModule)
 * and builds the same id -> def maps, skipping content/index.ts's cross-registry validate() pass
 * (covered by content/'s own tests). screen only reads content/** data here, never mutates it,
 * per content/CLAUDE.md and this lane's OWN scope (screen/src/**, never content/**).
 */
import type { AbilityDef } from "../../content/schema/ability.js";
import type { ClassDef } from "../../content/schema/class.js";
import type { ClauseDef } from "../../content/schema/clause.js";
import type { FacetDef, GearSlotId } from "../../content/schema/facet.js";

import wardenClass from "../../content/data/classes/warden.json";
import cinderClass from "../../content/data/classes/cinder.json";
import chanterClass from "../../content/data/classes/chanter.json";
import wardenAbilities from "../../content/data/abilities/warden.json";
import cinderAbilities from "../../content/data/abilities/cinder.json";
import chanterAbilities from "../../content/data/abilities/chanter.json";
import wardenFacets from "../../content/data/facets/warden.json";
import cinderFacets from "../../content/data/facets/cinder.json";
import chanterFacets from "../../content/data/facets/chanter.json";
import clausesData from "../../content/data/clauses.json";

/** content/data/dungeons/hollow-vault.json's id -- the POC's one dungeon (design/BIBLE.md sec 9). */
export const DUNGEON_ID = "hollow-vault";

export const CLASS_IDS = ["warden", "cinder", "chanter"] as const;
export type ClassId = (typeof CLASS_IDS)[number];

const CLASSES: Record<ClassId, ClassDef> = {
  warden: wardenClass as ClassDef,
  cinder: cinderClass as ClassDef,
  chanter: chanterClass as ClassDef,
};

const ABILITIES = new Map<string, AbilityDef>();
for (const list of [wardenAbilities, cinderAbilities, chanterAbilities] as AbilityDef[][]) {
  for (const ability of list) ABILITIES.set(ability.id, ability);
}

const FACETS = new Map<string, FacetDef>();
for (const list of [wardenFacets, cinderFacets, chanterFacets] as FacetDef[][]) {
  for (const facet of list) FACETS.set(facet.id, facet);
}

const CLAUSES = clausesData as ClauseDef[];

export function isClassId(value: string): value is ClassId {
  return (CLASS_IDS as readonly string[]).includes(value);
}

export function getClassDef(classId: string): ClassDef {
  const def = CLASSES[classId as ClassId];
  if (!def) throw new Error(`screen/content: unknown class id "${classId}"`);
  return def;
}

export function getAbilityRegistry(): ReadonlyMap<string, AbilityDef> {
  return ABILITIES;
}

export function getFacetRegistry(): ReadonlyMap<string, FacetDef> {
  return FACETS;
}

/** Every Facet in `slot`'s pool that targets one of `classId`'s own abilities. */
export function getFacetsForSlot(classId: string, slot: GearSlotId): FacetDef[] {
  const classDef = getClassDef(classId);
  const classAbilityIds = new Set(Object.values(classDef.abilities));
  return [...FACETS.values()].filter((f) => f.slot === slot && classAbilityIds.has(f.abilityId));
}

/** The four Clauses (design/BIBLE.md sec 8): Echo, Tide, Hush, Pact. Never mutated by callers. */
export function getClauses(): readonly ClauseDef[] {
  return CLAUSES;
}
