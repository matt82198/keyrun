/**
 * sim/encounter/echo-spawns.ts — Echo (design/BIBLE.md sec 8, clauses.ts's WIRING NOTE): the
 * "integration lane" spawn machinery clauses.ts's own `pendingEchoSpawns` docstring asks for
 * ("Pure -- does not spawn actors (needs pulls.ts's spawn machinery, owned by the integration
 * lane)"). Split out of sim/encounter/index.ts to keep that file under sim/CLAUDE.md's
 * <=190-LOC-per-file cap.
 *
 * `stepEchoSpawns` queues a delayed weak-add spawn for every "interrupt" SimEvent this tick's
 * combat step emitted (`pendingEchoSpawns`, pure request derivation), then realizes any request
 * whose delay has elapsed as a real enemy actor at its source's last known position (or the
 * origin, if that actor no longer exists).
 *
 * Called from sim/index.ts's stepRun AFTER `coreStep` resolves this tick's interrupts --
 * sim/encounter/index.ts's `stepEncounter` itself runs BEFORE combat and cannot see them yet.
 * Pure: returns a patched EncounterState plus any newly spawned ActorState[] for the caller to
 * `mergeActors` into `state.actors`, the same shape/convention `spawnPull` (pulls.ts) uses.
 *
 * Usage: `const echo = stepEchoSpawns(encounter, state, events, tick, emit);`
 *        `if (echo.spawned.length) state = mergeActors(state, echo.spawned);`
 */
import type { ActorId, ActorState, SimEvent, SimState, Tick } from "./types";
import type { EncounterState } from "./telegraphs";
import { pendingEchoSpawns } from "./clauses";
import { createActor } from "../combat/actors";

/** Next actor id `stepEchoSpawns` assigns to a realized Echo add -- far above any dungeon/party id
 * (this repo's fixtures and content data use small ids) so a spawned add never collides. */
export const ECHO_ACTOR_ID_BASE = 1_000_000;

export interface EchoSpawnResult {
  encounter: EncounterState;
  spawned: ActorState[];
}

export function stepEchoSpawns(
  encounter: EncounterState,
  state: SimState,
  events: SimEvent[],
  tick: Tick,
  emit: (event: SimEvent) => void
): EchoSpawnResult {
  const queued = [...(encounter.pendingEchoSpawns ?? []), ...pendingEchoSpawns(events, encounter.rules)];
  const due = queued.filter((r) => r.spawnAtTick <= tick);
  const remaining = queued.filter((r) => r.spawnAtTick > tick);
  if (due.length === 0) return { encounter: { ...encounter, pendingEchoSpawns: remaining }, spawned: [] };

  let nextId = encounter.nextEchoActorId ?? ECHO_ACTOR_ID_BASE;
  const spawned: ActorState[] = [];
  for (const req of due) {
    const id = nextId as ActorId;
    nextId += 1;
    const origin = state.actors[req.sourceActorId]?.pos ?? { x: 0, y: 0, z: 0 };
    const actor = createActor({ id, kind: "enemy", classId: "echo-add", role: "none", pos: origin, maxHealth: req.maxHealth, maxResource: 0 });
    spawned.push(actor);
    emit({ tick, type: "echo_spawn", actorId: id, sourceActorId: req.sourceActorId, maxHealth: req.maxHealth });
  }
  return { encounter: { ...encounter, pendingEchoSpawns: remaining, nextEchoActorId: nextId }, spawned };
}
