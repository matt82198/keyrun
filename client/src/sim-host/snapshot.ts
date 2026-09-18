// Locally-defined snapshot shape consumed by the client for this POC.
// An integration lane wires the real sim later; this type is the contract
// this lane builds against until then (see keyrun-wt/client dispatch brief).

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
}

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
}

export const SIM_HZ = 20;
export const SIM_TICK_MS = 1000 / SIM_HZ;
