/**
 * content/index.ts
 * Usage: `import { loadContent } from "@content/index"` to get validated, typed registries for
 * every class/ability/effect/combo/enemy/dungeon/clause. loadContent() throws loudly (never
 * returns partial data) on: an unknown id reference anywhere, a duplicate id within any
 * registry, a class without exactly 5 rotational slots, a talent patch that adds/renames an
 * ability (sets `id` on its patch), or a combo whose setupClass equals its payoffClass
 * (payoffClass "*"  -  "any other class", see schema/combo.ts  -  is exempt). Content is data: this
 * file never imports sim/ internals, only the local schema/*.ts copies of the sim-combat
 * contract. The registry-wide checks themselves live in content/validate.ts (re-exported here as
 * `validate`), split out to keep both files under the sim/CLAUDE.md 190 LOC cap.
 */
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import type { AbilityDef } from "./schema/ability";
import type { EffectDef } from "./schema/effect";
import type { ClassDef } from "./schema/class";
import type { ComboDef } from "./schema/combo";
import type { EnemyDef } from "./schema/enemy";
import type { DungeonDef } from "./schema/dungeon";
import type { ClauseDef } from "./schema/clause";
import type { FacetDef } from "./schema/facet";
import { validate } from "./validate";

export { validate } from "./validate";

function readJson<T>(rel: string): T {
  return JSON.parse(readFileSync(fileURLToPath(new URL(rel, import.meta.url)), "utf8")) as T;
}

export function byId<T extends { id: string }>(items: T[], kind: string): Map<string, T> {
  const map = new Map<string, T>();
  for (const item of items) {
    if (map.has(item.id)) throw new Error(`content: duplicate ${kind} id "${item.id}"`);
    map.set(item.id, item);
  }
  return map;
}

export interface ContentRegistry {
  abilities: Map<string, AbilityDef>;
  effects: Map<string, EffectDef>;
  classes: Map<string, ClassDef>;
  combos: Map<string, ComboDef>;
  enemies: Map<string, EnemyDef>;
  dungeons: Map<string, DungeonDef>;
  clauses: Map<string, ClauseDef>;
  /** BIBLE.md Sec 7 "Gear and progression": every Facet a class's five gear slots can roll. */
  facets: Map<string, FacetDef>;
}

export function loadContent(): ContentRegistry {
  const abilities = byId<AbilityDef>(
    [
      ...readJson<AbilityDef[]>("./data/abilities/warden.json"),
      ...readJson<AbilityDef[]>("./data/abilities/cinder.json"),
      ...readJson<AbilityDef[]>("./data/abilities/chanter.json"),
      ...readJson<AbilityDef[]>("./data/abilities/enemies.json"),
    ],
    "ability",
  );
  const effects = byId<EffectDef>(
    [...readJson<EffectDef[]>("./data/effects/player.json"), ...readJson<EffectDef[]>("./data/effects/enemy.json")],
    "effect",
  );
  const classes = byId<ClassDef>(
    [
      readJson<ClassDef>("./data/classes/warden.json"),
      readJson<ClassDef>("./data/classes/cinder.json"),
      readJson<ClassDef>("./data/classes/chanter.json"),
    ],
    "class",
  );
  const combos = byId<ComboDef>(readJson<ComboDef[]>("./data/combos.json"), "combo");
  const enemies = byId<EnemyDef>(
    [
      ...readJson<EnemyDef[]>("./data/enemies/choir.json"),
      ...readJson<EnemyDef[]>("./data/enemies/pair.json"),
      ...readJson<EnemyDef[]>("./data/enemies/seed.json"),
      ...readJson<EnemyDef[]>("./data/enemies/hollow-warden.json"),
    ],
    "enemy",
  );
  const dungeons = byId<DungeonDef>([readJson<DungeonDef>("./data/dungeons/hollow-vault.json")], "dungeon");
  const clauses = byId<ClauseDef>(readJson<ClauseDef[]>("./data/clauses.json"), "clause");
  const facets = byId<FacetDef>(
    [
      ...readJson<FacetDef[]>("./data/facets/warden.json"),
      ...readJson<FacetDef[]>("./data/facets/cinder.json"),
      ...readJson<FacetDef[]>("./data/facets/chanter.json"),
    ],
    "facet",
  );

  const registry: ContentRegistry = { abilities, effects, classes, combos, enemies, dungeons, clauses, facets };
  validate(registry);
  return registry;
}

/**
 * BIBLE.md Sec 9 "The Lock": a Payoff may close a windowRule-governed window only if its class
 * differs from the class that last Setup-tagged that window. Exported for sim reuse and tests.
 */
export function canPayoffCloseWindow(payoffClass: string, lastSetupClass: string | null): boolean {
  return lastSetupClass === null || payoffClass !== lastSetupClass;
}
