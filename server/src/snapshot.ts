/**
 * server/src/snapshot.ts
 *
 * Wire snapshot shape broadcast once per tick. Mirrors client/src/sim-host/snapshot.ts +
 * snapshot-codec.ts field-for-field so client/src/net/mapping.ts needs no translation, plus one
 * server-only addition: `stateHash`, the wire-determinism proof (tests/server/determinism.test.ts
 * compares it against a local createRun/stepRun replay's hashState()).
 *
 * D10 net parity: the readability fields -- ActorCast.kickable, the one-shot `events` channel
 * (landed kicks), and each telegraph's `telegraphClass` -- are NOT re-derived here. They come from
 * the client host's own pure rules (snapshot-codec.ts's toCast/interruptEvents, telegraph-map.ts
 * via ./telegraph.ts), imported, so the local worker and the server cannot drift apart.
 *
 * Usage: `const snapshot = buildSnapshot(run, parTicks, maxSlackTicks, pullCount, humanIds);`
 */
import type { ActorState, Tick } from "../../sim/core/state";
import { hashState } from "../../sim/core/state";
import type { Run } from "../../sim/index";
import { interruptEvents, toCast } from "../../client/src/sim-host/snapshot-codec.js";
import type { ActorCast, SnapshotEvent } from "../../client/src/sim-host/snapshot.js";
import type { TelegraphClass } from "../../client/src/sim-host/telegraph-map.js";
import { toTelegraphSnapshot } from "./telegraph.js";

export type { SnapshotEvent };

export type ActorKind = "player" | "enemy";

export interface Vec3Mm {
  x: number;
  y: number;
  z: number;
}

export interface Vec2Mm {
  x: number;
  z: number;
}

/** The client host's own cast shape (incl. D10 `kickable`), built by snapshot-codec.ts's toCast. */
export type { ActorCast };

export interface ActorSnapshot {
  id: number;
  kind: ActorKind;
  role: string;
  classId: string;
  pos: Vec3Mm;
  yaw: number;
  health: number;
  maxHealth: number;
  resource: number;
  maxResource: number;
  alive: boolean;
  targetId: number | null;
  cast: ActorCast | null;
  cooldowns: Record<string, number>;
  /** Server-only addition (no client-side equivalent yet): whether a human or a bot currently
   * drives this actor -- lets a joining/backfilling client tell which slots are open. */
  controlledBy: "human" | "bot";
}

export type TelegraphShape = "circle" | "cone" | "line";

export interface TelegraphSnapshot {
  id: string;
  shape: TelegraphShape;
  center: Vec2Mm;
  radius?: number;
  angle1024?: number;
  yaw?: number;
  length?: number;
  width?: number;
  resolvesTick: number;
  /** Derived from telegraphClass (telegraph-map.ts's isDangerClass): a stack circle is not danger. */
  danger: boolean;
  /** D10 render class (telegraph-map.ts's telegraphClassFor): avoid / spread / stack / boss. */
  telegraphClass: TelegraphClass;
}

export interface Snapshot {
  tick: number;
  actors: ActorSnapshot[];
  telegraphs: TelegraphSnapshot[];
  runTimerTicks: number;
  timerLimitTicks: number;
  slackTicks: number;
  maxSlackTicks: number;
  depleted: boolean;
  parTicks: number;
  pullIndex: number;
  pullCount: number;
  keyLevel: number;
  /** One-shot, bounded feedback events for the tick just stepped (snapshot-codec.ts's
   * interruptEvents): currently only landed kicks. Never carried forward. */
  events: SnapshotEvent[];
  /** FNV-1a hash of the authoritative SimState this tick (sim/core/state.ts's hashState) -- the
   * wire-determinism proof; not part of the client's mirrored shape. */
  stateHash: string;
}

function remainingCooldowns(actor: ActorState, tick: Tick): Record<string, number> {
  const out: Record<string, number> = {};
  for (const abilityId of Object.keys(actor.cooldowns)) {
    out[abilityId] = Math.max(0, (actor.cooldowns[abilityId] ?? 0) - tick);
  }
  return out;
}

function toActorSnapshot(actor: ActorState, tick: Tick, abilities: Run["content"]["abilities"], controlledBy: "human" | "bot"): ActorSnapshot {
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
    controlledBy,
  };
}

export function buildSnapshot(run: Run, parTicks: number, maxSlackTicks: number, pullCount: number, humanControlled: ReadonlySet<number>): Snapshot {
  const state = run.state;
  const actors: ActorSnapshot[] = Object.values(state.actors).map((actor) =>
    toActorSnapshot(actor, state.tick, run.content.abilities, actor.kind === "player" && humanControlled.has(actor.id) ? "human" : "bot"),
  );
  const telegraphs = run.encounter.telegraphs.map(toTelegraphSnapshot);

  return {
    tick: state.tick,
    actors,
    telegraphs,
    runTimerTicks: state.runTimerTicks,
    timerLimitTicks: parTicks,
    slackTicks: run.encounter.slackTicks,
    maxSlackTicks,
    depleted: run.encounter.depleted,
    parTicks: run.encounter.parTicks,
    pullIndex: run.encounter.pullIndex,
    pullCount,
    keyLevel: run.encounter.keyLevel,
    events: interruptEvents(run),
    stateHash: hashState(state),
  };
}
