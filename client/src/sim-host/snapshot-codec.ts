/**
 * client/src/sim-host/snapshot-codec.ts
 *
 * Pure SimState/Run -> Snapshot encoding, split out of worker.ts to keep it under the repo's
 * 190-LOC-per-file gate (tests/repo/loc.test.ts).
 *
 * Usage: `const snapshot = buildSnapshot(run, setup, maxSlackTicks, runEndInfo, stepped.events);`
 * (`simEvents`, optional: the step's own SimEvents, turned into one-shot cast events by cast-events.ts.)
 */
import type { ActorState, Tick } from "../../../sim/core/state.js";
import type { SimEvent } from "../../../sim/core/events.js";
import { castEvents } from "./cast-events.js";
import type { Run } from "../../../sim/index.js";
import type { RunEndInfo } from "./snapshot.js";
import type { ActorCast, ActorSnapshot, ActorWindow, Snapshot, SnapshotEvent } from "./snapshot.js";
import { MAX_SNAPSHOT_EVENTS } from "./snapshot.js";
import type { RunSetup } from "./run-setup.js";
import { toTelegraphSnapshot } from "./telegraph-map.js";
import { isWindowEffectId, parseWindowEffectId } from "../../../sim/combat/window-state.js";

type Abilities = Run["content"]["abilities"];
const windowDurations = new WeakMap<Abilities, Map<string, number>>();

/** windowId -> durationTicks from every "window" op anywhere in the content's ability defs (incl.
 * nested variants), cached per content object. The Window effect itself only stores endsTick. */
function durationsFor(abilities: Abilities): Map<string, number> {
  let out = windowDurations.get(abilities);
  if (out) return out;
  const found = new Map<string, number>();
  const visit = (v: unknown): void => {
    if (Array.isArray(v)) return v.forEach(visit);
    if (!v || typeof v !== "object") return;
    const o = v as Record<string, unknown>;
    if (o.op === "window" && typeof o.windowId === "string" && typeof o.durationTicks === "number") {
      found.set(o.windowId, Math.max(found.get(o.windowId) ?? 0, o.durationTicks));
    }
    Object.values(o).forEach(visit);
  };
  visit(Object.values(abilities));
  windowDurations.set(abilities, (out = found));
  return out;
}

/** The actor's latest-ending open Window (sim/combat/window-state.ts's findOpenWindow rule:
 * endsTick > tick), or null. Read-only projection of SimState; no sim rule changes. */
export function toWindow(actor: ActorState, tick: Tick, abilities: Abilities): ActorWindow | null {
  let best: ActorState["effects"][number] | null = null;
  for (const e of actor.effects) {
    if (isWindowEffectId(e.effectId) && e.endsTick > tick && (!best || e.endsTick > best.endsTick)) best = e;
  }
  const parsed = best ? parseWindowEffectId(best.effectId) : null;
  if (!best || !parsed) return null;
  const durationTicks = Math.max(durationsFor(abilities).get(parsed.windowId) ?? 0, best.endsTick - tick, 1);
  return { windowId: parsed.windowId, setupClass: parsed.setupClass, endsTick: best.endsTick, durationTicks, lock: parsed.alternate };
}

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
 * the sim's own "interrupt" event is asserted in tests/client/sim-host/snapshot-kick.test.ts. Exported
 * for server/src/snapshot.ts (the ?net=1 path), so both hosts emit identical events from one rule.
 */
export function interruptEvents(run: Run): SnapshotEvent[] {
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

/** Shared with server/src/snapshot.ts (one authority for the wire cast shape, incl. kickable). */
export function toCast(actor: ActorState, abilities: Run["content"]["abilities"]): ActorCast | null {
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
    gcdRemainingTicks: Math.max(0, actor.gcdReadyTick - tick),
    window: toWindow(actor, tick, abilities),
  };
}

export function buildSnapshot(
  run: Run,
  setup: RunSetup,
  maxSlackTicks: number,
  runEnd: RunEndInfo | null,
  simEvents?: readonly SimEvent[],
): Snapshot {
  const kicks = interruptEvents(run);
  const events: SnapshotEvent[] = [...kicks, ...castEvents(run, simEvents, MAX_SNAPSHOT_EVENTS - kicks.length)];
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
    events,
  };
}
