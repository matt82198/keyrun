/**
 * Integer movement: velocity in mm/tick from move commands (with a movement-speed modifier in
 * basis points), capsule-vs-cylinder actor-actor collision push-out (treated as circles in the
 * XZ plane, since actors are vertical capsules of equal radius), and arena-bounds AABB clamping.
 * `isqrt` is sim/core/fixed.ts's pure integer Newton's-method square root (re-exported here so
 * existing callers/tests keep importing it from this module); it is always the true integer
 * square root, so results stay platform-deterministic regardless of Math.sqrt's rounding.
 * Usage: applyMovement(actor, cmd.dir, baseSpeedMm, mods.movementBp, cmd.yaw); clampToBounds(actor, bounds);
 */
import type { ActorState } from "./types";
import { BP_DENOM } from "./effects";
import { isqrt as coreIsqrt } from "../core/fixed";

export interface Bounds {
  minX: number;
  maxX: number;
  minZ: number;
  maxZ: number;
}

/** n<=0 short-circuits to 0 (core's isqrt throws on negative input; callers here only ever pass
 * squared distances, which are never negative, so this guard exists purely to preserve the old
 * n===0 fast path without changing observable behaviour). */
export function isqrt(n: number): number {
  if (n <= 0) return 0;
  return coreIsqrt(n);
}

export function applyMovement(
  actor: ActorState,
  dir: { x: number; z: number },
  baseSpeedMmPerTick: number,
  movementModBp: number,
  yaw: number,
): void {
  actor.yaw = yaw;
  const len2 = dir.x * dir.x + dir.z * dir.z;
  if (len2 === 0) return;
  const len = isqrt(len2);
  if (len === 0) return;
  const speed = Math.max(0, Math.trunc((baseSpeedMmPerTick * (BP_DENOM + movementModBp)) / BP_DENOM));
  const dx = Math.trunc((dir.x * speed) / len);
  const dz = Math.trunc((dir.z * speed) / len);
  actor.pos = { x: actor.pos.x + dx, y: actor.pos.y, z: actor.pos.z + dz };
}

/** Pushes two overlapping equal-radius actor cylinders apart symmetrically along their XZ delta. */
export function resolveCollision(a: ActorState, b: ActorState, radiusMm: number): void {
  const dx = b.pos.x - a.pos.x;
  const dz = b.pos.z - a.pos.z;
  const dist2 = dx * dx + dz * dz;
  const minDist = radiusMm * 2;
  if (dist2 >= minDist * minDist) return;
  const dist = isqrt(dist2);
  if (dist === 0) {
    const half = Math.trunc(minDist / 2);
    a.pos = { ...a.pos, x: a.pos.x - half };
    b.pos = { ...b.pos, x: b.pos.x + half };
    return;
  }
  const overlap = minDist - dist;
  const pushX = Math.trunc((dx * overlap) / (2 * dist));
  const pushZ = Math.trunc((dz * overlap) / (2 * dist));
  a.pos = { ...a.pos, x: a.pos.x - pushX, z: a.pos.z - pushZ };
  b.pos = { ...b.pos, x: b.pos.x + pushX, z: b.pos.z + pushZ };
}

export function clampToBounds(actor: ActorState, bounds: Bounds): void {
  actor.pos = {
    ...actor.pos,
    x: Math.min(bounds.maxX, Math.max(bounds.minX, actor.pos.x)),
    z: Math.min(bounds.maxZ, Math.max(bounds.minZ, actor.pos.z)),
  };
}
