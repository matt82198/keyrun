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
 * RUN OPTIONS (run-options-wiring): an optional 5th `options` argument (sim/run-options.ts's
 * `RunOptions`) carries the run's Clause pick (`clauseId`, looked up in `content.clauses` and
 * folded into `encounter.rules` via sim/encounter/clauses.ts's `applyClause` -- see that file's
 * WIRING NOTE, which this fulfils) and any party members' already-loadout-resolved abilities
 * (`loadouts`, merged onto `content.abilities` -- see run-options.ts's file docstring for why sim
 * receives applyLoadout's OUTPUT, never raw talentChoices/gear: sim never imports content).
 * Omitting `options` (or any of its fields) reproduces today's exact behaviour byte-for-byte --
 * no existing golden replay changes.
 *
 * Usage:
 *   const run = createRun(seed, party, dungeonDef, content);
 *   const run2 = createRun(seed, party, dungeonDef, content, {clauseId: "echo", loadouts: {1: patchedAbilities}});
 *   const { run: run3, events } = stepRun(run, commandsForThisTick);
 */
import { createState, type SimState } from "./core/state";
import { step as coreStep } from "./core/tick";
import type { Command } from "./core/commands";
import type { SimEvent } from "./core/events";
import { createActor } from "./combat/actors";
import { makeRules } from "./combat/rules";
import {
  abilityTagsOf,
  applyBreakBarPatches,
  applyDeathsAndWipe,
  checkRunEnd,
  fixupTargeting,
  initialRunEndState,
  mergeActors,
  rulesConfigOf,
} from "./run-wiring";
import { stepEncounter, stepEchoSpawns, spawnPull, initialSlackTicks, type BreakBarWiring, type DungeonDef, type EncounterState } from "./encounter/index";
import { resolveEncounterRules, mergeLoadoutAbilities, type RunOptions } from "./run-options";
import { setActiveEncounterRules } from "./combat/clause-hooks";
import type { PartyMemberSpec, Run, RunContent, StepRunOpts } from "./run-types";

export type { PartyMemberSpec, Run, RunContent, StepRunOpts } from "./run-types";
export type { RunOptions } from "./run-options";
export { UnknownClauseError, UnknownLoadoutActorError, UnknownLoadoutAbilityError } from "./run-options";

/**
 * Builds a fresh run: a `createState(seed)` with every party member spawned as a player actor,
 * plus the dungeon's first pull (index 0) spawned as enemy actors, and an initial EncounterState
 * (empty telegraph/break-bar registries, pullIndex 0, a full Slack bank for `keyLevel`).
 */
export function createRun(
  seed: number,
  party: PartyMemberSpec[],
  dungeon: DungeonDef,
  content: RunContent,
  options?: RunOptions,
): Run {
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

  // run-options-wiring: options?.loadouts merges onto content.abilities (unchanged reference when
  // omitted); options?.clauseId folds into the encounter's starting rules via applyClause. Both
  // throw a clear, named error (never a silent default) on bad input -- see run-options.ts.
  const abilities = mergeLoadoutAbilities(content.abilities, party, options?.loadouts);
  const effectiveContent: RunContent = abilities === content.abilities ? content : { ...content, abilities };
  const rules = resolveEncounterRules(content, keyLevel, options?.clauseId);

  const encounter: EncounterState = {
    telegraphs: [],
    breakBars: {},
    pullIndex: 0,
    slackTicks: initialSlackTicks(keyLevel),
    parTicks: content.parTicks ?? 24000, // 20 minutes at 20Hz, a generous POC default
    keyLevel,
    depleted: false,
    rules,
    firedOnDeath: {},
  };

  // P2 policy-inside-stepRun: targeting resolves immediately at creation too, not just on the
  // first stepRun tick -- a host (or a bot's first-tick decision) never sees an untargeted party.
  fixupTargeting(state);

  return { state, encounter, dungeon, content: effectiveContent, runEnd: initialRunEndState() };
}

/**
 * Advances `run` by exactly one tick. `commands` must be addressed to `run.state.tick` (the same
 * contract sim/core/tick.step enforces -- anything else is silently dropped). Returns the advanced
 * run and every event emitted this tick (encounter's telegraph/pull events first, then combat's,
 * then this tick's targeting-fixup/Slack/run-end policy -- see run-wiring.ts).
 *
 * P2 policy-inside-stepRun: this is the one call site every host (server/src/room.ts, client/src/
 * sim-host/sim-runtime.ts) drives -- targeting fixup, Slack spend on death/wipe, and run-end
 * detection all happen here, unconditionally, every tick. No host calls run-wiring.ts's helpers
 * directly any more, so none can forget one, call them out of order, or drift its own copy.
 */
export function stepRun(run: Run, commands: Command[], opts?: StepRunOpts): { run: Run; events: SimEvent[] } {
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

  setActiveEncounterRules(run.encounter.rules); // clause-mechanics: Hush/Pact hooks read this during coreStep
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
  const echo = stepEchoSpawns(nextEncounter, nextState, events, nextState.tick, emit); // clause-mechanics: Echo
  nextEncounter = echo.encounter;
  if (echo.spawned.length > 0) nextState = mergeActors(nextState, echo.spawned);

  fixupTargeting(nextState);
  nextEncounter = applyDeathsAndWipe(nextState, nextEncounter, events, nextState.tick, emit);

  const runEnd = checkRunEnd(run.runEnd, nextState, nextEncounter, run.dungeon, nextState.tick, opts?.abandonRequested ?? false, emit);
  if (runEnd.result !== null && run.runEnd.result === null) {
    // A "depleted" result is always true by definition (run-wiring.ts's checkRunEnd) even when
    // applyDeathsAndWipe's own Slack spend didn't happen to reach 0 -- keep the Snapshot-facing
    // flag consistent with the result being reported.
    if (runEnd.result === "depleted") nextEncounter = { ...nextEncounter, depleted: true };
    emit({ tick: nextState.tick, type: "run_end", result: runEnd.result });
  }

  return {
    run: { state: nextState, encounter: nextEncounter, dungeon: run.dungeon, content: run.content, runEnd },
    events,
  };
}
