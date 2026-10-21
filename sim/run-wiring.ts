/**
 * sim/run-wiring.ts — small helpers split out of sim/index.ts to keep it under sim/CLAUDE.md's
 * <=190-LOC-per-file cap: building a `RulesConfig`, merging spawned actors, the `abilityTags` map
 * sim/encounter/breakbar.ts's `deriveBreakBarInputs` wants, applying a tick's break-bar effect
 * patches, and (P2, policy-inside-stepRun) the run's own host policy: enemy/damage-role
 * auto-targeting, death and wipe time on the run clock (D46), and run-end detection.
 *
 * POLICY-INSIDE-STEPRUN (P2, follow-up to server-policy-in-sim P1): `fixupTargeting`/
 * `applyDeathsAndWipe`/`checkRunEnd` used to be exported host-callable helpers every host
 * (server/src/room.ts, client/src/sim-host/run-policy.ts + hud/run-end.ts) had to remember to call,
 * in the right order, every tick -- the exact drift P1 already found once. They are sim-internal
 * now: `createRun`/`stepRun` (sim/index.ts) are the ONLY callers, so no host can forget or drift a
 * copy of its own. A host that wants to end the run early (an explicit human "abandon") passes
 * `{abandonRequested: true}` to `stepRun` -- the one signal sim cannot derive on its own -- and
 * reads the result off `run.runEnd` or the `run_end` SimEvent `stepRun` emits.
 *
 * `fixupTargeting` no longer takes a `tankActorId` -- it derives the tank from `state.actors`
 * (the one player actor with `role: "tank"`). `checkRunEnd` treats "dungeon complete" as
 * `encounter.pullIndex` having advanced past the dungeon's last pull -- content-agnostically true
 * the moment the final pull (the boss, by every current DungeonDef builder's convention) clears,
 * with no separate "bossActorId" concept needed in sim at all.
 *
 * An unrecoverable full wipe (sim has no rez mechanic) auto-ends the run after `WIPE_GRACE_TICKS`
 * with result "depleted", not "abandoned": a run that cannot finish is a key not timed (D46: the
 * one way a key depletes other than its clock passing par) -- "abandoned" is reserved for an explicit, still-recoverable
 * human quit (this corrects both hosts' prior duplicated behaviour, which mislabeled an
 * auto-timed-out wipe as "abandoned").
 *
 * Usage: internal to sim/index.ts's stepRun; a host never calls these directly any more.
 */
import type { ActorState, SimState, ActorId, Tick } from "./core/state";
import type { AbilityDef } from "./combat/abilities";
import type { EffectDef } from "./combat/effects";
import { applyEffectInstance } from "./combat/effects";
import { lockHolders } from "./combat/holder";
import type { RulesConfig } from "./combat/rules";
import type { BreakBarEffectPatch } from "./encounter/breakbar";
import type { SimEvent } from "./core/events";
import type { DungeonDef, EncounterState } from "./encounter/index";
import { chargeDeath, chargeWipe, finishRun, type RunResult } from "./encounter/timer";
import type { RunContent } from "./run-types";

// 5s at 20Hz -- sim's own auto-end policy for a full wipe with no rez mechanic (design/BIBLE.md
// Sec 8's Slack model assumes deaths get recovered; this five-actor POC has no mechanic for that).
export const WIPE_GRACE_TICKS = 100;

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

/** Applies a tick's break-bar onEmpty effect patches (stagger stuns etc.) to `state.actors` --
 * there is no canonical "source" for a bar's onEmpty effect, so each patch is applied with the
 * staggered actor itself as the effect's sourceId (nothing today reads a stun's sourceId). */
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

/** Enemy auto-target + damage-role auto-target fallback (sim has no enemy AI target acquisition of
 * its own -- see sim/combat/targeting.ts's manual `cycleTarget` tab-cycle). Every untargeted living
 * enemy points at the party's tank (the one living-or-not player actor with `role: "tank"`, if any);
 * every living damage-role player without a living-enemy target points at the current pull's first
 * living enemy. Mutates `state.actors` in place; safe to call every tick (already-resolved targets
 * are left alone) and at run creation. HOLDER-LOCK (STATE.md D30): lockHolders first (combat/holder.ts). */
export function fixupTargeting(state: SimState): void {
  lockHolders(state);
  const tank = Object.values(state.actors).find((a) => a.kind === "player" && a.role === "tank");
  const enemies = Object.values(state.actors).filter((a) => a.kind === "enemy" && a.alive);
  if (tank) {
    for (const enemy of enemies) {
      if (enemy.targetId === null) enemy.targetId = tank.id;
    }
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

/** Adds this tick's player deaths and wipe to the run clock (timer.ts, STATE.md D46): every distinct
 * player who died this tick costs its own death, and a full wipe adds the wipe cost on top. Emits
 * nothing (nothing drains); `_tick`/`_emit` keep the call shape. Mirrors RunWiring.cs. */
export function applyDeathsAndWipe(
  state: SimState,
  encounter: EncounterState,
  events: SimEvent[],
  _tick: Tick,
  _emit: (e: SimEvent) => void
): EncounterState {
  const dead = new Set(events.filter((e) => e.type === "death" && state.actors[e.actorId as ActorId]?.kind === "player").map((e) => e.actorId));
  let next = encounter;
  for (let i = 0; i < dead.size; i++) next = chargeDeath(next);
  const players = Object.values(state.actors).filter((a) => a.kind === "player");
  if (dead.size > 0 && players.length > 0 && players.every((a) => !a.alive)) next = chargeWipe(next);
  return next;
}

export interface RunEndState {
  result: RunResult | null;
  resultTick: Tick | null;
  wipeStartTick: Tick | null;
}

export function initialRunEndState(): RunEndState {
  return { result: null, resultTick: null, wipeStartTick: null };
}

/** Decides WHEN the run is over -- see file docstring. Idempotent once `result` is set (further
 * calls are no-ops), so `stepRun` can call this unconditionally every tick. */
export function checkRunEnd(
  prev: RunEndState,
  state: SimState,
  encounter: EncounterState,
  dungeon: DungeonDef,
  tick: Tick,
  abandonRequested: boolean,
  emit: (e: SimEvent) => void
): RunEndState {
  if (prev.result !== null) return prev;

  const dungeonComplete = encounter.pullIndex >= dungeon.pulls.length;
  if (dungeonComplete) {
    return { result: finishRun(encounter, tick, true, emit), resultTick: tick, wipeStartTick: prev.wipeStartTick };
  }
  if (abandonRequested) {
    return { result: finishRun(encounter, tick, false, emit), resultTick: tick, wipeStartTick: prev.wipeStartTick };
  }

  const players = Object.values(state.actors).filter((a) => a.kind === "player");
  const allPlayersDead = players.length > 0 && players.every((a) => !a.alive);
  if (allPlayersDead) {
    const wipeStart = prev.wipeStartTick ?? tick;
    if (tick - wipeStart >= WIPE_GRACE_TICKS) {
      // completed=true, depleted forced true: an unrecoverable full wipe is always a failure to
      // finish within the Slack-tracked budget, reported "depleted" unconditionally -- not
      // contingent on this run's deaths having routed through applyDeathsAndWipe's own per-death
      // cost (e.g. actors killed directly rather than via combat's "death" event) already having
      // zeroed the bank by coincidence. stepRun patches `nextEncounter.depleted` to match (see its
      // own docstring) so the Snapshot's Slack display stays consistent with this result.
      return { result: finishRun({ ...encounter, depleted: true }, tick, true, emit), resultTick: tick, wipeStartTick: wipeStart };
    }
    return { result: null, resultTick: null, wipeStartTick: wipeStart };
  }
  return { result: null, resultTick: null, wipeStartTick: null };
}
