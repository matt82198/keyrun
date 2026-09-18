/**
 * content/schema/registry.ts
 * `ContentRegistry` is the typed, by-id-map shape `content/index.ts`'s `loadContent()` returns.
 * Pulled out to its own file (rather than declared inline in content/index.ts) purely so
 * content/index.ts and content/validate.ts can both import it without one importing the other for
 * a type only -- keeps both files under sim/CLAUDE.md's <=190 LOC-per-file cap.
 */
import type { AbilityDef } from "./ability";
import type { EffectDef } from "./effect";
import type { ClassDef } from "./class";
import type { ComboEntry } from "./combo";
import type { EnemyDef } from "./enemy";
import type { DungeonDef } from "./dungeon";
import type { ClauseDef } from "./clause";
import type { FacetDef } from "./facet";

export interface ContentRegistry {
  abilities: Map<string, AbilityDef>;
  effects: Map<string, EffectDef>;
  classes: Map<string, ClassDef>;
  combos: Map<string, ComboEntry>;
  enemies: Map<string, EnemyDef>;
  dungeons: Map<string, DungeonDef>;
  clauses: Map<string, ClauseDef>;
  /** BIBLE.md Sec 7 "Gear and progression": every Facet a class's five gear slots can roll. */
  facets: Map<string, FacetDef>;
}
