// Locally-defined snapshot shape consumed by the client for this POC.
// An integration lane wires the real sim later; this type is the contract
// this lane builds against until then (see keyrun-wt/client dispatch brief).
//
// research/11-tick-rate-measurement.md (D9) found that this file used to define its own
// `SIM_HZ = 20` literal, wired independently from sim/core/tick.ts's real `TICK_HZ` -- a latent
// footgun (the two constants agreed by coincidence, nothing enforced it). `SIM_HZ` is deleted;
// this is now the one client module that re-exports the sim's own tick-rate constant, so every
// other client file (render/hud.ts, render/pingLabel.ts) that needs to convert ticks to seconds
// imports `TICK_HZ` from here (or straight from sim/core/tick.ts) instead of a client-local copy.
// See tests/client/sim-host/snapshot.test.ts for the coupling assertion.
import { TICK_HZ } from "../../../sim/core/tick.js";
export { TICK_HZ };

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

export interface ActorCast {
  abilityId: string;
  startTick: number;
  endTick: number;
  /** D10: the sim's own cast definition says this cast can be interrupted (AbilityDef.interruptible).
   * Optional for older literals; both hosts set it (server/src/snapshot.ts reuses snapshot-codec.ts's
   * toCast). render/castBar.ts treats anything but `true` as not kickable. */
  kickable?: boolean;
}

/** One-shot interrupt feedback: a kick landed on `actorId` during sim tick `tick`. Present in exactly
 * one Snapshot (the one built right after that tick) and never repeated. */
export interface InterruptSnapshotEvent {
  type: "interrupt";
  tick: number;
  actorId: number;
  sourceId: number;
  school: string;
}

export type SnapshotEvent = InterruptSnapshotEvent;

/** Upper bound on Snapshot.events per snapshot (the channel is bounded; overflow is dropped). */
export const MAX_SNAPSHOT_EVENTS = 16;

export interface ActorSnapshot {
  id: number;
  kind: ActorKind;
  role: string;
  classId: string;
  pos: Vec3Mm;
  yaw: number; // 0..1023
  health: number;
  maxHealth: number;
  resource: number;
  maxResource: number;
  alive: boolean;
  targetId: number | null;
  cast: ActorCast | null;
  cooldowns: Record<string, number>;
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
  danger: boolean;
}

/** "timed"/"depleted" mirror sim/encounter/timer.ts's RunResult for a completed run (boss dead);
 * "abandoned" covers both an explicit abandon-key press and worker.ts's own auto-abandon after a
 * full party wipe (see worker.ts's file docstring -- sim has no respawn/rez mechanic). */
export type RunOutcome = "timed" | "depleted" | "abandoned";

export interface RunEndInfo {
  outcome: RunOutcome;
  tick: number;
}

export interface Snapshot {
  tick: number;
  actors: ActorSnapshot[];
  telegraphs: TelegraphSnapshot[];
  runTimerTicks: number;
  timerLimitTicks: number;
  /** design/BIBLE.md Sec 8's Slack bank: ticks remaining, and whether it has hit 0 (the key is
   * "depleted" -- the run still continues to completion, just downgraded). Optional so existing
   * Snapshot literals built before this lane (e.g. tests/client/interpolate.test.ts) keep compiling. */
  slackTicks?: number;
  maxSlackTicks?: number;
  depleted?: boolean;
  parTicks?: number;
  pullIndex?: number;
  pullCount?: number;
  runEnd?: RunEndInfo | null;
  /** One-shot, bounded (MAX_SNAPSHOT_EVENTS) feedback events for things that happened during the
   * tick this snapshot follows -- currently only landed interrupts. Never carried forward. */
  events?: SnapshotEvent[];
}

/** Real ms per simulated tick, derived from the sim's own TICK_HZ -- never a separate literal.
 * worker.ts's default real-time pacing; research/11 (D9)'s `?hz=` override (boot-params.ts's
 * parseHz) is the only thing allowed to diverge from this, and only when explicitly requested. */
export const SIM_TICK_MS = 1000 / TICK_HZ;
