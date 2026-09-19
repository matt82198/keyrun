/**
 * sim/index.ts — the composition root. Wires sim/core's tick loop, sim/combat's StepRules, and
 * sim/encounter's per-tick enemy-script/telegraph step into one `createRun` / `stepRun` pair, so a
 * caller (the client's sim-host worker, tests, a future server) never has to hand-wire the three
 * lanes itself.
 *
 * Tick order inside stepRun, all addressed to the SAME `run.state.tick`:
 *   1. `stepEncounter` decides which enemies cast this tick (resolving targetId/range via
 *      sim/encounter/script-target.ts, given `content.abilities`), registers/resolves telegraphs,
 *      and drains any break bars (`content.breakBarSources`) off this tick's `commands`; returns
 *      enemy `cast` Commands plus a patched EncounterState (never touches ActorState itself).
 *   2. Enemy commands are merged with the caller-supplied `commands` (players + bots) and handed
 *      to `sim/core/tick.step` with a `sim/combat/rules` StepRules built from `run.content` --
 *      where damage/heals/effects/cast completion actually happen, against each caster's live
 *      position/facing at completion (sim/combat/casting.ts), same as a player cast.
 *   3. Any break bar that just staggered has its onEmpty patches (run-wiring.ts's
 *      `applyBreakBarPatches`) applied to the post-combat state.
 *   4. If `pullIndex` advanced (the previous pull's spawns are all dead), the next pull's enemies
 *      are spawned into the post-tick state.
 *
 * `createRun`'s documented return shape is `{state, encounter}`; the object returned also carries
 * `dungeon` and `content` so `stepRun` does not need them re-passed on every call -- a strict
 * superset of the documented shape, not a deviation from it.
 *
 * Usage:
 *   const run = createRun(seed, party, dungeonDef, content);
 *   const { run: run2, events } = stepRun(run, commandsForThisTick);
 */
import { createState, type ActorId, type SimState, type Tick, type Vec3 } from "./core/state";
import { step as coreStep } from "./core/tick";
import type { Command } from "./core/commands";
import type { SimEvent } from "./core/events";
import { createActor } from "./combat/actors";
import { makeRules } from "./combat/rules";
import type { ComboTable } from "./combat/combos";
import type { AbilityDef } from "./combat/abilities";
import type { EffectDef } from "./combat/effects";
import type { Bounds } from "./combat/movement";
import { abilityTagsOf, applyBreakBarPatches, mergeActors, rulesConfigOf } from "./run-wiring";
import {
  stepEncounter,
  spawnPull,
  initialSlackTicks,
  type BreakBarSource,
  type BreakBarWiring,
  type DungeonDef,
  type EncounterState,
  type EnemyScript,
} from "./encounter/index";
import { defaultEncounterRules, type EncounterRules } from "./encounter/clauses";

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
  encounterRules?: EncounterRules;
  /** [setupClass][payoffClass] -> ComboDef for target/self ("burst") Windows and Combos
   * (design/BIBLE.md §4, §5); passed straight through to `sim/combat/rules.ts`'s `RulesConfig`.
   * Omit for content that does not use Windows and Combos yet -- window/payoff ops then simply
   * find no combo and no-op (see RulesConfig's own docstring). */
  combos?: ComboTable;
  /** Same shape as `combos`, for the smaller "field" combos floor-patch Windows fire. */
  fieldCombos?: ComboTable;
  /** [barId] -> break-bar source data (break-bar-wiring, BIBLE.md Sec 9 "The Sundered Twins"),
   * passed to `stepEncounter` as `BreakBarWiring.sources` on every tick. Omit for content with no
   * break bars -- `stepRun` then drains none. */
  breakBarSources?: Record<string, BreakBarSource>;
}

export interface Run {
  state: SimState;
  encounter: EncounterState;
  dungeon: DungeonDef;
  content: RunContent;
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
    rules: content.encounterRules ?? defaultEncounterRules(keyLevel),
    firedOnDeath: {},
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

  const wiring: BreakBarWiring = {
    sources: run.content.breakBarSources ?? {},
    commands,
    abilityTags: abilityTagsOf(run.content.abilities),
  };
  const encResult = stepEncounter(
    run.state,
    run.encounter,
    run.dungeon,
    run.content.enemyScripts,
    emit,
    wiring,
    run.content.abilities
  );

  const rules = makeRules(rulesConfigOf(run.content));
  const merged = [...commands, ...encResult.commands];
  const stepped = coreStep(run.state, merged, rules);
  events.push(...stepped.events);

  let nextState = applyBreakBarPatches(stepped.state, encResult.effectPatches, run.content.effects);
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
