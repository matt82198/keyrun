/**
 * content/validate.ts
 * The registry-wide integrity checks loadContent() runs before handing back a ContentRegistry:
 * unknown id references, a class without exactly 5 rotational slots, a talent/facet patch that
 * adds/renames an ability id, or a combo whose setupClass equals its payoffClass. Split out of
 * content/index.ts so both files stay under the sim/CLAUDE.md 190 LOC cap; content/index.ts
 * re-exports `validate` so importers keep using a single `@content/index` entry point.
 */
import type { EffectOp } from "./schema/ability";
import type { ContentRegistry } from "./index";

const ROTATIONAL_SLOTS = ["rot1", "rot2", "rot3", "rot4", "rot5"] as const;
const ALL_SLOTS = [...ROTATIONAL_SLOTS, "interrupt", "defensive", "utility", "movement"] as const;

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
