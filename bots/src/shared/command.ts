/**
 * bots/src/shared/command.ts
 *
 * Integer-valid "walk from A to B" command construction, shared by every role/shared module that
 * turns a move intent into a sim Command. `ActorState.pos` (and every Telegraph/ping point bots
 * read) is already sim-space integer millimetres (sim/CLAUDE.md: "Positions in millimetres
 * (int)") -- never float metres -- so building `dir`/`yaw` always goes through sim/core/fixed.ts's
 * integer helpers (normaliseTo, yawBetween, angleDiff), never Math.hypot/Math.atan2. A command
 * built any other way fails sim/core/commands.ts's isIntInRange(-1000,1000) `dir` /
 * isIntInRange(0,1023) `yaw` guards, and sim/core/tick.ts's step() silently drops it -- the P0
 * "bots never move" defect this file exists to prevent regressing.
 *
 * Usage: `moveToward(tick, actorId, from, to)` -- null when `from`/`to` coincide (no direction).
 */
import { normaliseTo, yawBetween, angleDiff, type Vec3 } from "../../../sim/core/fixed.js";
import type { ActorId, Command, Tick } from "../types.js";

export interface PointLike {
  x: number;
  z: number;
}

/** `dir` components are a fixed-point unit vector scaled by this (sim/core/commands.ts's
 * [-1000,1000] validation range). */
const DIR_SCALE = 1000;
const ORIGIN: Vec3 = { x: 0, y: 0, z: 0 };

/** Integer yaw (0..1023) facing from `from` toward `to`, via sim/core/fixed.ts's yawBetween. */
export function yawToward(from: PointLike, to: PointLike): number {
  return yawBetween(ORIGIN, { x: to.x - from.x, y: 0, z: to.z - from.z });
}

/** True if `desired` is a meaningfully different facing than `current` (sim/core/fixed.ts's
 * angleDiff, the integer replacement for the old `Math.abs(yaw - self.yaw) > 0.01` float check). */
export function yawChanged(current: number, desired: number): boolean {
  return angleDiff(current, desired) !== 0;
}

/**
 * A "move" command walking `actorId` from `from` toward `to`: `dir` is an integer unit vector
 * (sim/core/fixed.ts's normaliseTo, scaled to DIR_SCALE) and `yaw` faces the same direction. Null
 * when `from` and `to` coincide -- there is no direction to walk in or face.
 */
export function moveToward(tick: Tick, actorId: ActorId, from: PointLike, to: PointLike): Command | null {
  const dx = to.x - from.x;
  const dz = to.z - from.z;
  if (dx === 0 && dz === 0) return null;
  const dir = normaliseTo({ x: dx, y: 0, z: dz }, DIR_SCALE);
  return { tick, actorId, kind: "move", dir: { x: dir.x, z: dir.z }, yaw: yawToward(from, to) };
}
