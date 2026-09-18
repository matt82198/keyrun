/**
 * content/index.ts
 * Usage: `import { loadContent } from "@content/index"` to get validated, typed registries for
 * every class/ability/effect/combo/enemy/dungeon/clause. loadContent() throws loudly (never
 * returns partial data) on: an unknown id reference anywhere, a duplicate id within any registry,
 * a class without exactly 5 rotational slots, a talent patch that adds/renames an ability (sets
 * `id` on its patch), a combo whose setupClass equals its payoffClass (payoffClass "*" -- "any
 * other class", see schema/combo.ts -- is exempt), or an ability that fails the REAL sim
 * validator (`sim/combat/abilities.ts`'s `validateAbilityDef`, not a local copy -- see
 * `validateAbilitiesAgainstSim`). Content is data: this file never imports sim/ *internals* (no
 * casting/damage/effects runtime code), only the type-and-validator surface sim/combat/*.ts
 * exports for exactly this purpose.
 *
 * `toComboTables`/`toSimDungeon` below are the two converters that turn loaded content into the
 * plain data sim/index.ts's `createRun`/`RunContent` actually consumes -- see
 * tests/sim/integration/content.test.ts for the full wiring.
 */
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import type { EffectOp } from "./schema/ability";
import type { ContentAbilityDef } from "./schema/ability";
import type { ContentEffectDef } from "./schema/effect";
import type { ClassDef } from "./schema/class";
import type { ComboEntry, ComboDef, ComboTable } from "./schema/combo";
import type { EnemyDef } from "./schema/enemy";
import type { DungeonDef } from "./schema/dungeon";
import type { ClauseDef } from "./schema/clause";
import { validateAbilityDef } from "../sim/combat/abilities";
import type { DungeonDef as SimDungeonDef, PullDef as SimPullDef, PullSpawn } from "../sim/encounter/pulls";
import type { EnemyScript } from "../sim/encounter/scripts";

export { canPayoffCloseWindow } from "./schema/enemy";

function readJson<T>(rel: string): T {
  return JSON.parse(readFileSync(fileURLToPath(new URL(rel, import.meta.url)), "utf8")) as T;
}

const ROTATIONAL_SLOTS = ["rot1", "rot2", "rot3", "rot4", "rot5"] as const;
const ALL_SLOTS = [...ROTATIONAL_SLOTS, "interrupt", "defensive", "utility", "movement"] as const;

/** Enemy-side pseudo-class (sim/combat/combos.ts's own docstring convention) for a boss/enemy
 * Window's row in the ComboTable -- never a real ClassDef, exempt from the "setupClass must be a
 * known class" check below. */
const BOSS_PSEUDO_CLASS = "boss";

export function byId<T extends { id: string }>(items: T[], kind: string): Map<string, T> {
  const map = new Map<string, T>();
  for (const item of items) {
    if (map.has(item.id)) throw new Error(`content: duplicate ${kind} id "${item.id}"`);
    map.set(item.id, item);
  }
  return map;
}

export interface ContentRegistry {
  abilities: Map<string, ContentAbilityDef>;
  effects: Map<string, ContentEffectDef>;
  classes: Map<string, ClassDef>;
  combos: Map<string, ComboEntry>;
  enemies: Map<string, EnemyDef>;
  dungeons: Map<string, DungeonDef>;
  clauses: Map<string, ClauseDef>;
}

export function loadContent(): ContentRegistry {
  const abilities = byId<ContentAbilityDef>(
    [
      ...readJson<ContentAbilityDef[]>("./data/abilities/warden.json"),
      ...readJson<ContentAbilityDef[]>("./data/abilities/cinder.json"),
      ...readJson<ContentAbilityDef[]>("./data/abilities/chanter.json"),
      ...readJson<ContentAbilityDef[]>("./data/abilities/enemies.json"),
    ],
    "ability",
  );
  const effects = byId<ContentEffectDef>(
    [...readJson<ContentEffectDef[]>("./data/effects/player.json"), ...readJson<ContentEffectDef[]>("./data/effects/enemy.json")],
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

  const registry: ContentRegistry = { abilities, effects, classes, combos, enemies, dungeons, clauses };
  validate(registry);
  return registry;
}

export function validate(reg: ContentRegistry): void {
  validateAbilitiesAgainstSim(reg);
  checkEffectRefs(reg);
  validateClasses(reg);
  validateCombos(reg);
  validateEnemies(reg);
  validateDungeons(reg);
}

/** Runs every ability through the REAL sim validator (sim/combat/abilities.ts's
 * `validateAbilityDef`), not a local copy -- catches anything this content-authoring layer could
 * produce that the sim ability pipeline would actually reject at runtime (missing cost, empty
 * effects, a line shape needing `radius`, etc). `ContentAbilityDef`'s extra `name`/`variants`
 * fields are structurally irrelevant to the check (validateAbilityDef only reads the real
 * AbilityDef fields), so passing a ContentAbilityDef straight through is safe. */
function validateAbilitiesAgainstSim(reg: ContentRegistry): void {
  for (const ability of reg.abilities.values()) {
    const errs = validateAbilityDef(ability);
    if (errs.length > 0) {
      throw new Error(`content: ability "${ability.id}" fails sim validation: ${errs.join("; ")}`);
    }
  }
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

function validateCombos(reg: ContentRegistry): void {
  for (const combo of reg.combos.values()) {
    const setupIsKnown = combo.setupClass === BOSS_PSEUDO_CLASS || reg.classes.has(combo.setupClass);
    if (!setupIsKnown) {
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
 * Expands a `Map<string, ComboEntry>` (data/combos.json, as loaded) into the two real
 * `ComboTable`s sim/combat/rules.ts's `RulesConfig` wants: `combos` (target/self Window
 * closures, keyed by exact setupClass/payoffClass) and `fieldCombos` (floor-patch Window
 * closures -- every `payoffClass: "*"` entry is expanded into one cell per other known class,
 * since sim/combat/windows.ts's `tryClosePayoffOnFloor` takes a fully concrete table and has no
 * wildcard concept of its own).
 */
export function toComboTables(entries: Map<string, ComboEntry>, classIds: string[]): { combos: ComboTable; fieldCombos: ComboTable } {
  const combos: ComboTable = {};
  const fieldCombos: ComboTable = {};
  for (const entry of entries.values()) {
    const def: ComboDef = { id: entry.id, effects: entry.effects };
    const isField = entry.payoffClass === "*";
    const table = isField ? fieldCombos : combos;
    const payoffClasses = isField ? classIds.filter((c) => c !== entry.setupClass) : [entry.payoffClass];
    for (const payoffClass of payoffClasses) {
      const row = table[entry.setupClass] ?? {};
      row[payoffClass] = def;
      table[entry.setupClass] = row;
    }
  }
  return { combos, fieldCombos };
}

export interface ExpandedDungeon {
  dungeon: SimDungeonDef;
  enemyScripts: Record<number, EnemyScript>;
}

/**
 * Expands a content `DungeonDef` (an authoring recipe of `{enemyId, count, spawn}` rows, see
 * schema/dungeon.ts) plus the loaded `EnemyDef` registry into the real sim/encounter `DungeonDef`
 * (concrete actor ids/classId/pos/yaw/health per spawn) and a matching `enemyScripts` map, both
 * consumable directly by `sim/index.ts`'s `createRun`/`RunContent`. Actor ids are assigned
 * sequentially starting at `firstActorId` in pull/enemy-group/count order, so the mapping is
 * deterministic for a given content snapshot (not stable across content edits -- callers that need
 * a specific actor id for a specific spawn should read it back off the returned pull's `spawns`).
 */
export function toSimDungeon(dungeon: DungeonDef, enemies: Map<string, EnemyDef>, firstActorId = 1000): ExpandedDungeon {
  let nextId = firstActorId;
  const enemyScripts: Record<number, EnemyScript> = {};
  const pulls: SimPullDef[] = dungeon.pulls.map((pull) => {
    const spawns: PullSpawn[] = [];
    for (const group of pull.enemies) {
      const def = enemies.get(group.enemyId);
      if (!def) throw new Error(`content: toSimDungeon: pull "${pull.id}" references unknown enemy "${group.enemyId}"`);
      for (let i = 0; i < group.count; i += 1) {
        const actorId = nextId;
        nextId += 1;
        spawns.push({
          actorId,
          classId: def.id,
          pos: { x: group.spawn.x, y: 0, z: group.spawn.z },
          yaw: 0,
          health: def.maxHealth,
        });
        enemyScripts[actorId] = def.script;
      }
    }
    return { id: pull.id, spawns };
  });
  return { dungeon: { id: dungeon.id, pulls }, enemyScripts };
}
