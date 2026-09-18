/**
 * sim/index.ts — the composition root. Wires sim/core's tick loop, sim/combat's StepRules, and
 * sim/encounter's per-tick enemy-script/telegraph step into one `createRun` / `stepRun` pair, so a
 * caller (the client's sim-host worker, tests, a future server) never has to hand-wire the three
 * lanes itself.
 *
 * Tick order inside stepRun, both parts addressed to the SAME `run.state.tick`:
 *   1. `stepEncounter` reads the (unmutated) current state to decide which enemies cast this tick
 *      and to register/resolve telegraphs; it returns enemy-authored `cast` Commands for this tick
 *      plus a patched EncounterState (never touches ActorState itself -- see sim/encounter/index.ts).
 *   2. Those enemy commands are merged with the caller-supplied `commands` (players + bots) and
 *      handed to `sim/core/tick.step` together with a `sim/combat/rules` StepRules built from
 *      `run.content`. This is where damage/heals/effects/cast completion actually happen -- an
 *      enemy telegraph's *warning* comes from step 1, but the ability itself resolves through the
 *      ordinary cast pipeline in step 2 against the caster's actual position/facing at completion,
 *      exactly like a player cast (see sim/combat/casting.ts). For a stationary scripted enemy
 *      whose ability castTicks equals the telegraph's leadTicks, the two coincide.
 *   3. If the encounter step advanced `pullIndex` (the previous pull's spawns are all dead), the
 *      next pull's enemies are spawned into the post-tick state.
 *
 * `createRun`'s documented return shape is `{state, encounter}`; the object returned also carries
 * `dungeon` and `content` so `stepRun` does not need them re-passed on every call -- a strict
 * superset of the documented shape, not a deviation from it.
 *
 * Usage:
 *   const run = createRun(seed, party, dungeonDef, content);
 *   const { run: run2, events } = stepRun(run, commandsForThisTick);
 */
import { createState, type ActorId, type ActorState, type SimState, type Tick, type Vec3 } from "./core/state";
import { step as coreStep } from "./core/tick";
import type { Command } from "./core/commands";
import type { SimEvent } from "./core/events";
import { createActor } from "./combat/actors";
import { makeRules, type RulesConfig } from "./combat/rules";
import type { AbilityDef } from "./combat/abilities";
import type { EffectDef } from "./combat/effects";
import type { Bounds } from "./combat/movement";
import {
  stepEncounter,
  spawnPull,
  initialSlackTicks,
  type DungeonDef,
  type EncounterState,
  type EnemyScript,
} from "./encounter/index";

/** One party member to spawn as a player actor at run creation. */
export interface PartyMemberSpec {
  id: ActorId;
  classId: string;
  role: "tank" | "healer" | "damage" | "none";
  pos: Vec3;
  yaw?: number;
  maxHealth: number;
  maxResource: number;
}

/**
 * The data a run needs beyond the party and dungeon: ability/effect definitions for
 * `sim/combat/rules.ts` (see RulesConfig), the enemy AI scripts for `sim/encounter`, and the
 * encounter pacing knobs (`keyLevel`/`parTicks`) that seed the initial EncounterState. `content`
 * never comes from a `content/` package import -- per sim/CLAUDE.md, sim never imports content;
 * callers (client, tests) pass their own definitions in.
 */
export interface RunContent {
  abilities: Record<string, AbilityDef>;
  effects: Record<string, EffectDef>;
  lockoutTicks: Tick;
  baseSpeedMmPerTick: number;
  actorRadiusMm: number;
  bounds: Bounds;
  enemyScripts: Record<ActorId, EnemyScript>;
  keyLevel?: number;
  parTicks?: number;
}

export interface Run {
  state: SimState;
  encounter: EncounterState;
  dungeon: DungeonDef;
  content: RunContent;
}

function rulesConfigOf(content: RunContent): RulesConfig {
  return {
    abilities: content.abilities,
    effects: content.effects,
    lockoutTicks: content.lockoutTicks,
    baseSpeedMmPerTick: content.baseSpeedMmPerTick,
    actorRadiusMm: content.actorRadiusMm,
    bounds: content.bounds,
  };
}

function mergeActors(state: SimState, spawned: ActorState[]): SimState {
  if (spawned.length === 0) return state;
  const actors = { ...state.actors };
  for (const actor of spawned) actors[actor.id] = actor;
  return { ...state, actors };
}

/**
 * Builds a fresh run: a `createState(seed)` with every party member spawned as a player actor,
 * plus the dungeon's first pull (index 0) spawned as enemy actors, and an initial EncounterState
 * (empty telegraph/break-bar registries, pullIndex 0, a full Slack bank for `keyLevel`).
 */
export function createRun(seed: number, party: PartyMemberSpec[], dungeon: DungeonDef, content: RunContent): Run {
  let state = createState(seed);

  const players = party.map((member) =>
    createActor({
      id: member.id,
      kind: "player",
      classId: member.classId,
      role: member.role,
      pos: member.pos,
      ...(member.yaw !== undefined ? { yaw: member.yaw } : {}),
      maxHealth: member.maxHealth,
      maxResource: member.maxResource,
    }),
  );
  state = mergeActors(state, players);

  const keyLevel = content.keyLevel ?? 1;
  // spawnPull's pull_start event has nowhere to go from createRun's fixed {state, encounter}
  // return shape (no emit callback in this function's signature); it is intentionally dropped.
  // stepRun's own pull transitions (see below) DO forward their pull_start/pull_clear events.
  const spawned = spawnPull(dungeon, 0, state.tick, () => {});
  state = mergeActors(state, spawned);

  const encounter: EncounterState = {
    telegraphs: [],
    breakBars: {},
    pullIndex: 0,
    slackTicks: initialSlackTicks(keyLevel),
    parTicks: content.parTicks ?? 24000, // 20 minutes at 20Hz, a generous POC default
    keyLevel,
    depleted: false,
  };

  return { state, encounter, dungeon, content };
}

/**
 * Advances `run` by exactly one tick. `commands` must be addressed to `run.state.tick` (the same
 * contract sim/core/tick.step enforces -- anything else is silently dropped). Returns the advanced
 * run and every event emitted this tick (encounter's telegraph/pull events first, then combat's).
 */
export function stepRun(run: Run, commands: Command[]): { run: Run; events: SimEvent[] } {
  const events: SimEvent[] = [];
  const emit = (e: SimEvent): void => {
    events.push(e);
  };

  const encResult = stepEncounter(run.state, run.encounter, run.dungeon, run.content.enemyScripts, emit);

  const rules = makeRules(rulesConfigOf(run.content));
  const merged = [...commands, ...encResult.commands];
  const stepped = coreStep(run.state, merged, rules);
  events.push(...stepped.events);

  let nextState = stepped.state;
  let nextEncounter = encResult.encounter;
  if (nextEncounter.pullIndex !== run.encounter.pullIndex) {
    const spawned = spawnPull(run.dungeon, nextEncounter.pullIndex, nextState.tick, emit);
    nextState = mergeActors(nextState, spawned);
  }

  return {
    run: { state: nextState, encounter: nextEncounter, dungeon: run.dungeon, content: run.content },
    events,
  };
}
