/**
 * content/index.ts
 * Usage: `import { loadContent } from "@content/index"` to get validated, typed registries for
 * every class/ability/effect/combo/enemy/dungeon/clause/facet. loadContent() throws loudly (never
 * returns partial data) on any invariant content/validate.ts checks -- see that file for the full
 * list (unknown id references, duplicate ids, the 5-rotational-slot / 2-setup-2-payoff shape, an
 * id-changing talent or facet patch, a combo whose setupClass equals its payoffClass, and any
 * ability that fails the REAL sim validator, sim/combat/abilities.ts's `validateAbilityDef`, not a
 * local copy). Content is data: this file never imports sim/ *internals* (no casting/damage/
 * effects runtime code), only the type-and-validator surface sim/combat/*.ts exports for exactly
 * this purpose.
 *
 * `toComboTables`/`toSimDungeon` (content/sim-adapt.ts, re-exported below) are the two converters
 * that turn loaded content into the plain data sim/index.ts's `createRun`/`RunContent` and
 * sim/combat/rules.ts's `RulesConfig` actually consume -- see
 * tests/sim/integration/content.test.ts for the full wiring.
 */
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import type { AbilityDef } from "./schema/ability";
import type { EffectDef } from "./schema/effect";
import type { ClassDef } from "./schema/class";
import type { ComboEntry } from "./schema/combo";
import type { EnemyDef } from "./schema/enemy";
import type { DungeonDef } from "./schema/dungeon";
import type { ClauseDef } from "./schema/clause";
import type { FacetDef } from "./schema/facet";
import type { ContentRegistry } from "./schema/registry";
import { validateRegistry } from "./validate";

export type { ContentRegistry } from "./schema/registry";
export { canPayoffCloseWindow } from "./schema/enemy";
export { toComboTables, toSimDungeon, type ExpandedDungeon } from "./sim-adapt";

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
  const combos = byId<ComboEntry>(readJson<ComboEntry[]>("./data/combos.json"), "combo");
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

export function validate(reg: ContentRegistry): void {
  validateRegistry(reg);
}
