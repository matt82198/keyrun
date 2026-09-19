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
 *        `fixupTargeting(run, tankActorId); run = applyDeathsAndWipe(run, events, tick);`
 *
 * ABSORBED HOST POLICY (server-policy-in-sim, P1): `fixupTargeting`/`applyDeathsAndWipe` used to be
 * hand-duplicated, byte-for-byte, in both client/src/sim-host/run-policy.ts and the old
 * server/src/targeting-policy.ts -- sim itself has no enemy target-acquisition (only
 * sim/combat/targeting.ts's manual `cycleTarget` tab-cycle) and nothing calls sim/encounter/
 * timer.ts's spendSlackOnDeath/spendSlackOnWipe automatically. That was genuinely missing from sim,
 * so it now lives here as the one canonical copy (tests/sim/integration/run-end.test.ts exercises
 * it through the public createRun/stepRun path) and both hosts call it from here. DEVIATION:
 * client/src/sim-host/run-policy.ts still carries its own copy (client/** is out of this lane's
 * ownership) -- a follow-up lane should delete it and import from here instead, same as this PR
 * does for server/src/room.ts.
 */
import type { ActorState, SimState } from "./core/state";
import type { ActorId, Tick } from "./core/state";
import type { AbilityDef } from "./combat/abilities";
import type { EffectDef } from "./combat/effects";
import { applyEffectInstance } from "./combat/effects";
import type { RulesConfig } from "./combat/rules";
import type { BreakBarEffectPatch } from "./encounter/breakbar";
import type { SimEvent } from "./core/events";
import { spendSlackOnDeath, spendSlackOnWipe } from "./encounter/timer";
import type { RunContent, Run } from "./index";

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

/**
 * Enemy auto-target + damage-role auto-target fallback (see file docstring's ABSORBED HOST POLICY
 * note). Every untargeted living enemy points at `tankActorId` (or the tank's own id if the tank
 * has since died -- self-referential on purpose, matching the pre-absorption server behaviour);
 * every living damage-role player without a living-enemy target points at the current pull's first
 * living enemy. Mutates `run.state.actors` in place -- same as the pre-absorption copies, and safe
 * to call every tick (already-resolved targets are left alone).
 */
export function fixupTargeting(run: Run, tankActorId: ActorId): void {
  const state = run.state;
  const tank = state.actors[tankActorId];
  const fallbackTargetId = tank && tank.alive ? tank.id : tankActorId;
  const enemies = Object.values(state.actors).filter((a) => a.kind === "enemy" && a.alive);
  for (const enemy of enemies) {
    if (enemy.targetId === null) enemy.targetId = fallbackTargetId;
  }
  const firstEnemy = enemies[0];
  if (!firstEnemy) return;
  for (const actor of Object.values(state.actors)) {
    if (actor.kind !== "player" || actor.role !== "damage" || !actor.alive) continue;
    const current = actor.targetId !== null ? state.actors[actor.targetId] : undefined;
    const currentIsLivingEnemy = current !== undefined && current.kind === "enemy" && current.alive;
    if (!currentIsLivingEnemy) actor.targetId = firstEnemy.id;
  }
}

/**
 * Spends the run's Slack bank (sim/encounter/timer.ts) on this tick's player deaths/wipe -- see
 * file docstring's ABSORBED HOST POLICY note. No-ops (returns `run` unchanged) when this tick's
 * `events` carry no player "death".
 */
export function applyDeathsAndWipe(run: Run, events: SimEvent[], tick: Tick): Run {
  const anyPlayerDeath = events.some((e) => e.type === "death" && run.state.actors[e.actorId as ActorId]?.kind === "player");
  if (!anyPlayerDeath) return run;
  const noop = (): void => {};
  let next = { ...run, encounter: spendSlackOnDeath(run.encounter, tick, noop) };
  const players = Object.values(next.state.actors).filter((a) => a.kind === "player");
  if (players.length > 0 && players.every((a) => !a.alive)) {
    next = { ...next, encounter: spendSlackOnWipe(next.encounter, tick, noop) };
  }
  return next;
}
