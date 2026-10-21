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
 * are alive, then drains it off this tick's tagged casts. It stays optional (defaults to
 * `undefined`) for any caller with no break bars to drain; `sim/index.ts`'s `stepRun` now builds it
 * from `RunContent.breakBarSources` on every real run (see that file). `StepEncounterResult.
 * effectPatches` is the onEmpty counterpart to `commands`: this function never mutates ActorState,
 * so a bar's stagger effects (e.g. `stunned` on both twins) ride out as data for the caller to
 * apply via sim/combat/effects.ts's real `applyEffectInstance`.
 *
 * DEATH-TRIGGER WIRING (backlog item death-trigger-wiring, scripts.ts's FIRE-ONCE CONTRACT):
 * `encounter.firedOnDeath` is threaded into `stepEnemyScripts` as its third argument and the
 * result folded back into the returned `EncounterState`, so an `"onDeath"` step (e.g.
 * `seed.deathBloom`) fires exactly once across a run instead of re-firing every tick after death.
 * `sim/combat/rules.ts` carries the matching half of this fix (it no longer drops a dead actor's
 * "cast" command -- see that file's DEATH-CAST EXCEPTION note).
 *
 * TARGETING WIRING (PR #35's WIRING NOTE, sim/encounter/script-target.ts): an optional trailing
 * `abilities` argument (default `{}`) is `stepEnemyScripts`'s own 4th parameter, passed straight
 * through -- with it, an enemy script's cast resolves a real targetId (threat/nearest for a
 * hostile ability, lowest-health ally for a heal) and range-gates it against the ability's own
 * `range`, skipping the cast this tick rather than firing at an out-of-range target. `sim/index.ts`
 * passes `RunContent.abilities` here on every real run; omitted, `stepEnemyScripts` falls back to
 * its always-safe legacy behaviour (see that file's docstring).
 *
 * Usage: `const { state, encounter, commands } = stepEncounter(state, encounter, dungeon, scripts, emit);`
 */
import type { ActorId, Command, SimEvent, SimState } from "./types";
import { stepEnemyScripts, type EnemyScript } from "./scripts";
import { resolveTelegraphs, type EncounterState, type Telegraph } from "./telegraphs";
import { driftTelegraphs } from "./clauses";
import type { AbilityDef } from "../combat/abilities";
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

/** Builds the Telegraph for one "telegraph" event. `id` comes from the run's own
 * `EncounterState.nextTelegraphId` (see stepEncounter) -- never a module-level counter, which
 * would interleave ids across runs sharing a process and break per-seed determinism. */
function registerTelegraph(event: SimEvent, id: number): Telegraph {
  const telegraph: Telegraph = {
    id,
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
  breakBarWiring?: BreakBarWiring,
  abilities: Record<string, AbilityDef> = {}
): StepEncounterResult {
  const tick = state.tick;

  const { commands, telegraphEvents, firedOnDeath, deferredCasts } = stepEnemyScripts(
    state, scripts, encounter.firedOnDeath ?? {}, abilities, encounter.deferredCasts ?? []);
  telegraphEvents.forEach(emit);
  const firstTelegraphId = encounter.nextTelegraphId ?? 1;
  const registered = telegraphEvents.map((e, i) => registerTelegraph(e, firstTelegraphId + i));
  const nextTelegraphId = firstTelegraphId + registered.length;

  // Tide (design/BIBLE.md sec 8): drifts every tracked telegraph's origin by
  // `encounter.rules.driftMmPerTick` this tick (a no-op unless Tide is the active Clause) BEFORE
  // resolving hits, so a telegraph's final, fully-drifted position is what actually gets hit-
  // tested on its resolution tick.
  let telegraphs = driftTelegraphs([...encounter.telegraphs, ...registered], encounter.rules);
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

  let nextEncounter: EncounterState = { ...encounter, telegraphs, breakBars, firedOnDeath, deferredCasts, nextTelegraphId };
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
// Clauses (design/BIBLE.md §8, run modifiers): wired into the tick path here (Tide's drift above)
// and in echo-spawns.ts (Echo), sim/combat/casting.ts (Hush) and damage.ts (Pact) via
// sim/combat/clause-hooks.ts's module-scoped active-rules channel, set once per tick by
// sim/index.ts's stepRun.
export * from "./clauses";
export * from "./echo-spawns";
