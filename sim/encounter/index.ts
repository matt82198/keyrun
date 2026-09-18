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
 * Usage: `const { state, encounter, commands } = stepEncounter(state, encounter, dungeon, scripts, emit);`
 */
import type { ActorId, Command, SimEvent, SimState } from "./types";
import { stepEnemyScripts, type EnemyScript } from "./scripts";
import { resolveTelegraphs, type EncounterState, type Telegraph } from "./telegraphs";
import { tickBreakBarReset } from "./breakbar";
import { checkPullAdvance, type DungeonDef } from "./pulls";

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
}

export function stepEncounter(
  state: SimState,
  encounter: EncounterState,
  dungeon: DungeonDef,
  scripts: Record<ActorId, EnemyScript>,
  emit: (event: SimEvent) => void
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

  let nextEncounter: EncounterState = { ...encounter, telegraphs, breakBars };
  nextEncounter = checkPullAdvance(state, dungeon, nextEncounter, tick, emit);

  return { state, encounter: nextEncounter, commands };
}

export * from "./types";
export * from "./scripts";
export * from "./telegraphs";
export * from "./breakbar";
export * from "./pulls";
export * from "./timer";
export * from "./tempo";
