/**
 * sim/encounter/pulls.ts — spawns a dungeon pull's enemies at their defined points and advances
 * `EncounterState.pullIndex` once every enemy spawned by the active pull is dead.
 *
 * `DungeonDef` is a local, minimal shape (id + ordered pulls, each an ordered list of enemy spawns);
 * content/ is expected to supply a richer definition later that satisfies this same shape (sim never
 * imports content, per sim/CLAUDE.md). Pure: `spawnPull` returns new ActorState[] to be merged into
 * `state.actors` by the caller; `checkPullAdvance` returns a patched EncounterState. Both emit
 * pull_start / pull_clear via the caller-supplied emit function; neither mutates its inputs.
 *
 * Usage: `const spawned = spawnPull(dungeon, encounter.pullIndex, tick, emit);`
 *        `encounter = checkPullAdvance(state, dungeon, encounter, tick, emit);`
 */
import type { ActorId, ActorState, SimEvent, SimState, Tick, Vec3 } from "./types";
import type { EncounterState } from "./telegraphs";

export interface PullSpawn {
  actorId: ActorId;
  classId: string;
  pos: Vec3;
  yaw: number;
  health: number;
  /** Copied onto ActorState only when present, so content without it hashes exactly as before. */
  speedMmPerTick?: number;
  chaseMarginMm?: number;
}

export interface PullDef {
  id: string;
  spawns: PullSpawn[];
}

export interface DungeonDef {
  id: string;
  pulls: PullDef[];
}

function spawnActor(spawn: PullSpawn): ActorState {
  return {
    id: spawn.actorId,
    kind: "enemy",
    classId: spawn.classId,
    role: "none",
    pos: spawn.pos,
    yaw: spawn.yaw,
    health: spawn.health,
    maxHealth: spawn.health,
    resource: 0,
    maxResource: 0,
    alive: true,
    targetId: null,
    castId: null,
    castEndsTick: null,
    castStartTick: null,
    cooldowns: {},
    gcdReadyTick: 0,
    effects: [],
    threat: {},
    ...(spawn.speedMmPerTick !== undefined ? { speedMmPerTick: spawn.speedMmPerTick } : {}),
    ...(spawn.chaseMarginMm !== undefined ? { chaseMarginMm: spawn.chaseMarginMm } : {}),
  };
}

export function spawnPull(
  dungeon: DungeonDef,
  pullIndex: number,
  tick: Tick,
  emit: (event: SimEvent) => void
): ActorState[] {
  const pull = dungeon.pulls[pullIndex];
  if (!pull) return [];
  emit({
    tick,
    type: "pull_start",
    pullIndex,
    pullId: pull.id,
    actorIds: pull.spawns.map((s) => s.actorId),
  });
  return pull.spawns.map(spawnActor);
}

export function checkPullAdvance(
  state: SimState,
  dungeon: DungeonDef,
  encounter: EncounterState,
  tick: Tick,
  emit: (event: SimEvent) => void
): EncounterState {
  const pull = dungeon.pulls[encounter.pullIndex];
  if (!pull || pull.spawns.length === 0) return encounter;
  const allDead = pull.spawns.every((s) => {
    const actor = state.actors[s.actorId];
    return actor === undefined || !actor.alive;
  });
  if (!allDead) return encounter;
  emit({ tick, type: "pull_clear", pullIndex: encounter.pullIndex, pullId: pull.id });
  return { ...encounter, pullIndex: encounter.pullIndex + 1 };
}
