/**
 * content/validate.ts
 * Every content-loading invariant check content/index.ts's `validate()` runs, pulled into its own
 * file purely to keep content/index.ts under sim/CLAUDE.md's <=190 LOC-per-file cap. Not meant to
 * be imported directly by anything other than content/index.ts -- `import { validate } from
 * "@content/index"` is still the public entry point.
 */
import type { EffectOp } from "./schema/ability";
import type { ContentRegistry } from "./schema/registry";
import { validateAbilityDef } from "../sim/combat/abilities";

const ROTATIONAL_SLOTS = ["rot1", "rot2", "rot3", "rot4", "rot5"] as const;
const ALL_SLOTS = [...ROTATIONAL_SLOTS, "interrupt", "defensive", "utility", "movement"] as const;

/** Enemy-side pseudo-class (sim/combat/combos.ts's own docstring convention) for a boss/enemy
 * Window's row in the ComboTable -- never a real ClassDef, exempt from the "setupClass must be a
 * known class" check below. */
const BOSS_PSEUDO_CLASS = "boss";

export function validateRegistry(reg: ContentRegistry): void {
  validateAbilitiesAgainstSim(reg);
  checkEffectRefs(reg);
  validateClasses(reg);
  validateCombos(reg);
  validateEnemies(reg);
  validateDungeons(reg);
  validateFacets(reg);
}

/** Runs every ability through the REAL sim validator (sim/combat/abilities.ts's
 * `validateAbilityDef`), not a local copy -- catches anything this content-authoring layer could
 * produce that the sim ability pipeline would actually reject at runtime (missing cost, empty
 * effects, a line shape needing `radius`, etc). Content's `AbilityDef`'s extra `name`/`variants`
 * fields are structurally irrelevant to the check (validateAbilityDef only reads the real
 * AbilityDef fields), so passing one straight through is safe. */
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
