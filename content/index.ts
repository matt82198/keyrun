/**
 * content/index.ts
 * Usage: `import { loadContent } from "@content/index"` to get validated, typed registries for
 * every class/ability/effect/combo/enemy/dungeon/clause. loadContent() throws loudly (never
 * returns partial data) on: an unknown id reference anywhere, a duplicate id within any
 * registry, a class without exactly 5 rotational slots, a talent patch that adds/renames an
 * ability (sets `id` on its patch), or a combo whose setupClass equals its payoffClass
 * (payoffClass "*"  -  "any other class", see schema/combo.ts  -  is exempt). Content is data: this
 * file never imports sim/ internals, only the local schema/*.ts copies of the sim-combat
 * contract.
 */
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import type { AbilityDef, EffectOp } from "./schema/ability";
import type { EffectDef } from "./schema/effect";
import type { ClassDef } from "./schema/class";
import type { ComboDef } from "./schema/combo";
import type { EnemyDef } from "./schema/enemy";
import type { DungeonDef } from "./schema/dungeon";
import type { ClauseDef } from "./schema/clause";
import type { FacetDef } from "./schema/facet";

function readJson<T>(rel: string): T {
  return JSON.parse(readFileSync(fileURLToPath(new URL(rel, import.meta.url)), "utf8")) as T;
}

const ROTATIONAL_SLOTS = ["rot1", "rot2", "rot3", "rot4", "rot5"] as const;
const ALL_SLOTS = [...ROTATIONAL_SLOTS, "interrupt", "defensive", "utility", "movement"] as const;

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

export function validate(reg: ContentRegistry): void {
  checkEffectRefs(reg);
  validateClasses(reg);
  validateCombos(reg);
  validateEnemies(reg);
  validateDungeons(reg);
  validateFacets(reg);
}

function checkEffectOps(ops: EffectOp[], reg: ContentRegistry, where: string): void {
  for (const op of ops) {
    if ((op.op === "applyEffect" || op.op === "removeEffect") && op.effectId !== "*" && !reg.effects.has(op.effectId)) {
      throw new Error(`content: ${where} references unknown effect "${op.effectId}"`);
    }
  }
}

function checkEffectRefs(reg: ContentRegistry): void {
  for (const ability of reg.abilities.values()) {
    checkEffectOps(ability.effects, reg, `ability "${ability.id}"`);
    for (const variant of Object.values(ability.variants ?? {})) checkEffectOps(variant, reg, `ability "${ability.id}" variant`);
  }
  for (const combo of reg.combos.values()) checkEffectOps(combo.effects, reg, `combo "${combo.id}"`);
  for (const enemy of reg.enemies.values()) {
    for (const bar of enemy.breakBars ?? []) checkEffectOps(bar.onEmpty, reg, `enemy "${enemy.id}" breakBar "${bar.id}"`);
  }
}

function validateClasses(reg: ContentRegistry): void {
  const slotNames: readonly string[] = ROTATIONAL_SLOTS;
  for (const cls of reg.classes.values()) {
    const rotKeys = Object.keys(cls.abilities).filter((k) => slotNames.includes(k));
    if (rotKeys.length !== 5) {
      throw new Error(`content: class "${cls.id}" has ${rotKeys.length} rotational abilities, expected 5`);
    }
    for (const slot of ALL_SLOTS) {
      const abilityId = (cls.abilities as unknown as Record<string, string>)[slot];
      if (!abilityId || !reg.abilities.has(abilityId)) {
        throw new Error(`content: class "${cls.id}" slot "${slot}" references unknown ability "${abilityId}"`);
      }
    }
    for (const fork of cls.talents) {
      if (!reg.abilities.has(fork.abilityId)) {
        throw new Error(`content: class "${cls.id}" talent fork references unknown ability "${fork.abilityId}"`);
      }
      for (const opt of fork.options) {
        if ("id" in opt.patch) {
          throw new Error(`content: class "${cls.id}" talent "${opt.id}" patch adds/renames an ability id`);
        }
      }
    }
  }
}

function validateFacets(reg: ContentRegistry): void {
  for (const facet of reg.facets.values()) {
    if (!reg.abilities.has(facet.abilityId)) {
      throw new Error(`content: facet "${facet.id}" references unknown ability "${facet.abilityId}"`);
    }
    if ("id" in facet.patch) {
      throw new Error(`content: facet "${facet.id}" patch adds/renames an ability id`);
    }
  }
}

function validateCombos(reg: ContentRegistry): void {
  for (const combo of reg.combos.values()) {
    if (!reg.classes.has(combo.setupClass)) {
      throw new Error(`content: combo "${combo.id}" references unknown setupClass "${combo.setupClass}"`);
    }
    if (combo.payoffClass !== "*" && !reg.classes.has(combo.payoffClass)) {
      throw new Error(`content: combo "${combo.id}" references unknown payoffClass "${combo.payoffClass}"`);
    }
    if (combo.payoffClass === combo.setupClass) {
      throw new Error(`content: combo "${combo.id}" has matching setupClass/payoffClass "${combo.setupClass}"`);
    }
  }
}

function validateEnemies(reg: ContentRegistry): void {
  for (const enemy of reg.enemies.values()) {
    for (const abilityId of enemy.abilities) {
      if (!reg.abilities.has(abilityId)) throw new Error(`content: enemy "${enemy.id}" references unknown ability "${abilityId}"`);
    }
    for (const step of enemy.script) {
      if (!reg.abilities.has(step.cast)) throw new Error(`content: enemy "${enemy.id}" script references unknown ability "${step.cast}"`);
    }
  }
}

function validateDungeons(reg: ContentRegistry): void {
  for (const dungeon of reg.dungeons.values()) {
    for (const pull of dungeon.pulls) {
      for (const enemy of pull.enemies) {
        if (!reg.enemies.has(enemy.enemyId)) {
          throw new Error(`content: dungeon "${dungeon.id}" pull "${pull.id}" references unknown enemy "${enemy.enemyId}"`);
        }
      }
    }
    if (!reg.enemies.has(dungeon.boss.enemyId)) {
      throw new Error(`content: dungeon "${dungeon.id}" boss references unknown enemy "${dungeon.boss.enemyId}"`);
    }
  }
}

/**
 * BIBLE.md Sec 9 "The Lock": a Payoff may close a windowRule-governed window only if its class
 * differs from the class that last Setup-tagged that window. Exported for sim reuse and tests.
 */
export function canPayoffCloseWindow(payoffClass: string, lastSetupClass: string | null): boolean {
  return lastSetupClass === null || payoffClass !== lastSetupClass;
}
