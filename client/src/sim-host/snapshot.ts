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

/** One-shot cast feedback (D10 projections, render/cast-fx.ts): `actorId` completed `abilityId` during
 * `tick`. Geometry is the ability's own shape as sim/combat/targeting.ts resolves it: circle around the
 * caster, cone/line along the caster's facing (combat yaw: 0 = +x, 256 = +z; line half-width =
 * `radius`), target = a beam to `targetId`. `hitIds`: every actor it damaged or healed that tick. */
export interface CastSnapshotEvent {
  type: "cast";
  tick: number;
  actorId: number;
  abilityId: string;
  shape: "self" | "target" | "circle" | "cone" | "line";
  origin: Vec2Mm;
  yaw: number;
  radius?: number;
  angle1024?: number;
  length?: number;
  targetId: number | null;
  hitIds: number[];
}

/** One-shot "your key press cast nothing" feedback (sim-host/cast-feedback.ts; local host only). */
export interface CastFailedSnapshotEvent {
  type: "cast_failed";
  tick: number;
  actorId: number;
  abilityId: string;
  reason: string;
}

export type SnapshotEvent = InterruptSnapshotEvent | CastSnapshotEvent | CastFailedSnapshotEvent;

/** The open Window (design/BIBLE.md §4) an actor carries, projected from its "window:"/"lockwindow:"
 * effect by snapshot-codec.ts's toWindow. Open while `tick < endsTick`; opened at
 * `endsTick - durationTicks`. render/window-ring.ts draws it. */
export interface ActorWindow {
  windowId: string;
  setupClass: string;
  endsTick: number;
  durationTicks: number;
  /** The Lock (§9): an `alternate` Window. */
  lock: boolean;
}

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
  /** Ticks until this actor's global cooldown is ready (sim gcdReadyTick - tick, floored at 0).
   * Optional: the ?net=1 server snapshot does not project it; render/ability-bar-model.ts reads 0. */
  gcdRemainingTicks?: number;
  /** The latest-ending open Window on this actor, or null. Optional: the ?net=1 server snapshot
   * (server/src/snapshot.ts) does not project it yet, and older literals predate it. */
  window?: ActorWindow | null;
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
