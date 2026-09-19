/**
 * sim/run-wiring.ts — small helpers split out of sim/index.ts to keep it under sim/CLAUDE.md's
 * <=190-LOC-per-file cap (break-bar-wiring / death-trigger-wiring follow-up to PR #33): building a
 * `RulesConfig` and merging spawned actors (both pre-existing sim/index.ts private helpers, moved
 * here verbatim), turning `RunContent.abilities` into the `abilityTags` map
 * sim/encounter/breakbar.ts's `deriveBreakBarInputs` wants, and applying a tick's
 * `BreakBarEffectPatch[]` (sim/encounter/index.ts's `StepEncounterResult.effectPatches`) to
 * `SimState` via sim/combat/effects.ts's real `applyEffectInstance` -- exactly what
 * tests/sim/integration/break-bar.test.ts's own local `advanceTick` did by hand before `stepRun`
 * did this itself. `RunContent` is imported type-only (erased at compile time, per esbuild/tsc's
 * handling of `import type`), so this file and sim/index.ts's mutual reference is not a runtime
 * circular dependency.
 *
 * Usage: `const cfg = rulesConfigOf(content); const tags = abilityTagsOf(content.abilities);`
 *        `state = mergeActors(state, spawned); state = applyBreakBarPatches(state, patches, content.effects);`
 */
import type { ActorState, SimState } from "./core/state";
import type { AbilityDef } from "./combat/abilities";
import type { EffectDef } from "./combat/effects";
import { applyEffectInstance } from "./combat/effects";
import type { RulesConfig } from "./combat/rules";
import type { BreakBarEffectPatch } from "./encounter/breakbar";
import type { RunContent } from "./index";

export function rulesConfigOf(content: RunContent): RulesConfig {
  return {
    abilities: content.abilities,
    effects: content.effects,
    lockoutTicks: content.lockoutTicks,
    baseSpeedMmPerTick: content.baseSpeedMmPerTick,
    actorRadiusMm: content.actorRadiusMm,
    bounds: content.bounds,
    ...(content.combos !== undefined ? { combos: content.combos } : {}),
    ...(content.fieldCombos !== undefined ? { fieldCombos: content.fieldCombos } : {}),
  };
}

export function mergeActors(state: SimState, spawned: ActorState[]): SimState {
  if (spawned.length === 0) return state;
  const actors = { ...state.actors };
  for (const actor of spawned) actors[actor.id] = actor;
  return { ...state, actors };
}

/** [abilityId] -> its content tags (e.g. "interrupt"/"control"), for breakbar.ts's
 * `deriveBreakBarInputs`. */
export function abilityTagsOf(abilities: Record<string, AbilityDef>): Record<string, string[] | undefined> {
  const tags: Record<string, string[] | undefined> = {};
  for (const [id, def] of Object.entries(abilities)) tags[id] = def.tags;
  return tags;
}

/**
 * Applies a tick's break-bar onEmpty effect patches (stagger stuns etc.) to `state.actors` after
 * combat has resolved that same tick -- see sim/encounter/index.ts's file docstring for why these
 * ride out as data instead of being applied inside stepEncounter itself. There is no canonical
 * "source" for a bar's onEmpty effect (BreakBarEffectPatch carries no sourceId), so each patch is
 * applied with the staggered actor itself as the effect's sourceId -- a deliberate, documented
 * simplification; nothing today reads a stun's sourceId.
 */
export function applyBreakBarPatches(
  state: SimState,
  patches: BreakBarEffectPatch[],
  effects: Record<string, EffectDef>
): SimState {
  if (patches.length === 0) return state;
  let actors = state.actors;
  for (const patch of patches) {
    const actor = actors[patch.actorId];
    const effDef = effects[patch.effectId];
    if (!actor || !effDef) continue;
    const patched: ActorState = { ...actor, effects: [...actor.effects] };
    applyEffectInstance(patched, effDef, patch.actorId, patch.durationTicks, patch.stacks, state.tick);
    if (actors === state.actors) actors = { ...state.actors };
    actors[patch.actorId] = patched;
  }
  return actors === state.actors ? state : { ...state, actors };
}
