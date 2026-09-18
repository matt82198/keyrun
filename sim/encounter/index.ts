/**
 * sim/encounter/index.ts — composes the encounter lane for one tick.
 *
 * `stepEncounter` never applies damage or mutates ActorState health/effects itself — that is
 * sim/combat's job once state.actors is patched by whatever consumes the returned `commands`. Each
 * tick it: (1) asks scripts.ts which enemies cast and registers any telegraph events into the active
 * registry, (2) resolves telegraphs whose `resolvesTick` has arrived and emits "telegraph_hit" hit
 * lists (still no damage — just who was standing in it), (3) ticks break-bar cooldown resets, (4)
 * advances pulls when cleared.
 *
 * DEVIATION: the brief's stated signature is `stepEncounter(state, encounter, dungeonDef, emit) ->
 * {state, encounter}`. The enemy-authored cast Commands this tick produces have nowhere to live in
 * `state` or `encounter` (SimState carries no pending-command queue per its contract), so the return
 * also carries `commands: Command[]` for the caller to feed into sim/combat's step. `state` itself is
 * returned unchanged (encounter never mutates ActorState) — it is passed through only so the return
 * shape matches the brief and callers can chain `{state, encounter} = stepEncounter(...)`.
 *
 * BREAK BAR WIRING (BIBLE.md Sec 9 "The Sundered Twins", backlog item break-bar-wiring): an
 * optional trailing `breakBarWiring` argument (content->sim data + this tick's submitted commands,
 * see breakbar.ts's `BreakBarSource`/`deriveBreakBarInputs`) is the "spawn/enemy setup" hook that
 * turns a spawned shared-bar pair into a real `BreakBarState` the first tick both of its `actorIds`
 * are alive, then drains it off this tick's tagged casts. It is optional (defaults to `undefined`)
 * purely so sim/index.ts's existing 5-arg call (that file is out of this lane's scope) keeps
 * compiling unchanged; threading `commands`/`abilityTags` through that call site so break bars
 * drain inside a real `stepRun`-driven game is the one-line follow-up left for sim/index.ts's
 * owning lane. `StepEncounterResult.effectPatches` is the onEmpty counterpart to `commands`: this
 * function never mutates ActorState, so a bar's stagger effects (e.g. `stunned` on both twins)
 * ride out as data for the caller to apply via sim/combat/effects.ts's real `applyEffectInstance`.
 *
 * Usage: `const { state, encounter, commands } = stepEncounter(state, encounter, dungeon, scripts, emit);`
 */
import type { ActorId, Command, SimEvent, SimState } from "./types";
import { stepEnemyScripts, type EnemyScript } from "./scripts";
import { resolveTelegraphs, type EncounterState, type Telegraph } from "./telegraphs";
import {
  tickBreakBarReset,
  createBreakBar,
  drainBreakBar,
  deriveBreakBarInputs,
  breakBarEffectPatches,
  type BreakBarSource,
  type BreakBarEffectPatch,
} from "./breakbar";
import { checkPullAdvance, type DungeonDef } from "./pulls";

/** Content->sim data this tick needs to drain any active break bars, plus the commands submitted
 * this tick (players + bots -- sim/index.ts's stepRun already has these; see docstring above). */
export interface BreakBarWiring {
  sources: Record<string, BreakBarSource>;
  commands: Command[];
  abilityTags: Record<string, string[] | undefined>;
}

let nextTelegraphId = 1;

function registerTelegraph(event: SimEvent): Telegraph {
  const telegraph: Telegraph = {
    id: nextTelegraphId++,
    sourceId: event.actorId as ActorId,
    abilityId: event.cast as string,
    shape: event.shape as Telegraph["shape"],
    origin: event.origin as Telegraph["origin"],
    yaw: event.yaw as number,
    resolvesTick: event.resolvesTick as number,
  };
  if (event.radius !== undefined) telegraph.radius = event.radius as number;
  if (event.angle1024 !== undefined) telegraph.angle1024 = event.angle1024 as number;
  if (event.length !== undefined) telegraph.length = event.length as number;
  if (event.width !== undefined) telegraph.width = event.width as number;
  return telegraph;
}

export interface StepEncounterResult {
  state: SimState;
  encounter: EncounterState;
  commands: Command[];
  /** onEmpty effect patches from any break bar that staggered this tick; see file docstring. */
  effectPatches: BreakBarEffectPatch[];
}

export function stepEncounter(
  state: SimState,
  encounter: EncounterState,
  dungeon: DungeonDef,
  scripts: Record<ActorId, EnemyScript>,
  emit: (event: SimEvent) => void,
  breakBarWiring?: BreakBarWiring
): StepEncounterResult {
  const tick = state.tick;

  const { commands, telegraphEvents } = stepEnemyScripts(state, scripts);
  telegraphEvents.forEach(emit);
  const registered = telegraphEvents.map(registerTelegraph);

  let telegraphs = [...encounter.telegraphs, ...registered];
  const hits = resolveTelegraphs(state, { ...encounter, telegraphs });
  for (const hit of hits) {
    emit({ tick, type: "telegraph_hit", telegraphId: hit.telegraphId, actorIds: hit.hitActorIds });
  }
  const resolvedIds = new Set(hits.map((h) => h.telegraphId));
  telegraphs = telegraphs.filter((t) => !resolvedIds.has(t.id));

  const breakBars: EncounterState["breakBars"] = {};
  for (const key of Object.keys(encounter.breakBars)) {
    const bar = encounter.breakBars[key];
    if (bar) breakBars[key] = tickBreakBarReset(bar, tick);
  }

  const effectPatches: BreakBarEffectPatch[] = [];
  const sources = breakBarWiring?.sources ?? {};
  for (const [id, source] of Object.entries(sources)) {
    // Spawn/enemy setup: lazily create this bar the first tick every one of its actors is alive.
    if (!breakBars[id] && source.actorIds.every((actorId) => state.actors[actorId]?.alive)) {
      breakBars[id] = createBreakBar(source.max, source.actorIds);
    }
    const bar = breakBars[id];
    if (!bar) continue;
    const wasStaggered = bar.current <= 0;
    const inputs = deriveBreakBarInputs(breakBarWiring!.commands, state.actors, breakBarWiring!.abilityTags, source);
    const drained = drainBreakBar(bar, id, inputs, tick, emit);
    breakBars[id] = drained;
    effectPatches.push(...breakBarEffectPatches(source, !wasStaggered && drained.current <= 0));
  }

  let nextEncounter: EncounterState = { ...encounter, telegraphs, breakBars };
  nextEncounter = checkPullAdvance(state, dungeon, nextEncounter, tick, emit);

  return { state, encounter: nextEncounter, commands, effectPatches };
}

export * from "./types";
export * from "./scripts";
export * from "./telegraphs";
export * from "./breakbar";
export * from "./pulls";
export * from "./timer";
export * from "./tempo";
// Clauses (design/BIBLE.md §8, run modifiers, sim side only): appended, not wired into
// stepEncounter/stepRun here -- see sim/encounter/clauses.ts's WIRING NOTE for the one-line hook
// the integration lane (sim/index.ts, PR #15) needs to thread an EncounterRules into EncounterState.
export * from "./clauses";
