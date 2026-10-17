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
import type { ActorCast, ActorSnapshot, Snapshot, SnapshotEvent } from "./snapshot.js";
import { MAX_SNAPSHOT_EVENTS } from "./snapshot.js";
import type { RunSetup } from "./run-setup.js";
import { toTelegraphSnapshot } from "./telegraph-map.js";

function remainingCooldowns(actor: ActorState, tick: Tick): Record<string, number> {
  const out: Record<string, number> = {};
  for (const abilityId of Object.keys(actor.cooldowns)) {
    out[abilityId] = Math.max(0, (actor.cooldowns[abilityId] ?? 0) - tick);
  }
  return out;
}

const LOCKOUT_PREFIX = "lockout:";

/**
 * D10 interrupt feedback, derived from SimState alone so every host that already calls buildSnapshot
 * gets it with no extra plumbing: sim/combat/casting.ts's interruptCast is the only writer of a
 * `lockout:<school>` effect, and it stamps `endsTick = <interrupt tick> + lockoutTicks`. A lockout
 * whose start is exactly the tick just stepped (state.tick - 1) is therefore a kick that landed this
 * tick -- one-shot by construction (the next snapshot's previous tick no longer matches). Parity with
 * the sim's own "interrupt" event is asserted in tests/client/sim-host/snapshot-kick.test.ts.
 */
function interruptEvents(run: Run): SnapshotEvent[] {
  const steppedTick = run.state.tick - 1;
  const out: SnapshotEvent[] = [];
  for (const actor of Object.values(run.state.actors)) {
    for (const e of actor.effects) {
      if (!e.effectId.startsWith(LOCKOUT_PREFIX) || e.endsTick - run.content.lockoutTicks !== steppedTick) continue;
      if (out.length >= MAX_SNAPSHOT_EVENTS) return out;
      out.push({ type: "interrupt", tick: steppedTick, actorId: actor.id, sourceId: e.sourceId, school: e.effectId.slice(LOCKOUT_PREFIX.length) });
    }
  }
  return out;
}

function toCast(actor: ActorState, abilities: Run["content"]["abilities"]): ActorCast | null {
  if (actor.castId === null || actor.castStartTick === null || actor.castEndsTick === null) return null;
  const kickable = abilities[actor.castId]?.interruptible === true;
  return { abilityId: actor.castId, startTick: actor.castStartTick, endTick: actor.castEndsTick, kickable };
}

function toActorSnapshot(actor: ActorState, tick: Tick, abilities: Run["content"]["abilities"]): ActorSnapshot {
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
    cast: toCast(actor, abilities),
    cooldowns: remainingCooldowns(actor, tick),
  };
}

export function buildSnapshot(run: Run, setup: RunSetup, maxSlackTicks: number, runEnd: RunEndInfo | null): Snapshot {
  const state = run.state;
  const actors: ActorSnapshot[] = Object.values(state.actors).map((actor) => toActorSnapshot(actor, state.tick, run.content.abilities));
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
    events: interruptEvents(run),
  };
}
