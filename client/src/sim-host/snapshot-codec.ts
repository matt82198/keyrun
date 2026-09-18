/**
 * client/src/sim-host/snapshot-codec.ts
 *
 * Pure SimState/Run -> Snapshot encoding, split out of worker.ts to keep it under the repo's
 * 190-LOC-per-file gate (tests/repo/loc.test.ts).
 *
 * Usage: `const snapshot = buildSnapshot(run, setup, maxSlackTicks, runEndInfo);`
 */
import type { ActorState, Tick } from "../../../sim/core/state.js";
import type { Run } from "../../../sim/index.js";
import type { RunEndInfo } from "./snapshot.js";
import type { ActorSnapshot, Snapshot } from "./snapshot.js";
import type { RunSetup } from "./run-setup.js";
import { toTelegraphSnapshot } from "./telegraph-map.js";

function remainingCooldowns(actor: ActorState, tick: Tick): Record<string, number> {
  const out: Record<string, number> = {};
  for (const abilityId of Object.keys(actor.cooldowns)) {
    out[abilityId] = Math.max(0, (actor.cooldowns[abilityId] ?? 0) - tick);
  }
  return out;
}

function toActorSnapshot(actor: ActorState, tick: Tick): ActorSnapshot {
  return {
    id: actor.id,
    kind: actor.kind,
    role: actor.role,
    classId: actor.classId,
    pos: { ...actor.pos },
    yaw: actor.yaw,
    health: actor.health,
    maxHealth: actor.maxHealth,
    resource: actor.resource,
    maxResource: actor.maxResource,
    alive: actor.alive,
    targetId: actor.targetId,
    cast:
      actor.castId !== null && actor.castStartTick !== null && actor.castEndsTick !== null
        ? { abilityId: actor.castId, startTick: actor.castStartTick, endTick: actor.castEndsTick }
        : null,
    cooldowns: remainingCooldowns(actor, tick),
  };
}

export function buildSnapshot(run: Run, setup: RunSetup, maxSlackTicks: number, runEnd: RunEndInfo | null): Snapshot {
  const state = run.state;
  const actors: ActorSnapshot[] = Object.values(state.actors).map((actor) => toActorSnapshot(actor, state.tick));
  const telegraphs = run.encounter.telegraphs.map(toTelegraphSnapshot);

  return {
    tick: state.tick,
    actors,
    telegraphs,
    runTimerTicks: state.runTimerTicks,
    timerLimitTicks: setup.parTicks,
    slackTicks: run.encounter.slackTicks,
    maxSlackTicks,
    depleted: run.encounter.depleted,
    parTicks: run.encounter.parTicks,
    pullIndex: run.encounter.pullIndex,
    pullCount: setup.dungeon.pulls.length,
    runEnd,
  };
}
