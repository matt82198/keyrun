// Usage: integer-only vector math over Vec3 (integer millimetres) and yaw (0..1023 units per turn).
// All exported functions return integers; scale/normaliseTo round to nearest, ties away from zero.
// yawBetween (via sim/core/atan.ts) and isqrt are pure integer: no Math.atan2, Math.sin, Math.cos, or
// Math.sqrt anywhere on the sim path, so results are bit-identical across engines -- a float that rounds
// to a different yaw bucket at a boundary would otherwise desync a replay between engines.

import { yawFromComponents } from "./atan";

export type Vec3 = { x: number; y: number; z: number };
export type Bp = number; // basis points, 10000 = 1.0

const BP_SCALE = 10000;
export const YAW_MAX = 1024;

/** Integer division of n/d, rounded to nearest, ties away from zero. */
function roundDiv(n: number, d: number): number {
  if (d === 0) throw new Error("roundDiv: division by zero");
  const sign = n < 0 !== d < 0 ? -1 : 1;
  const an = Math.abs(n);
  const ad = Math.abs(d);
  return sign * Math.floor((an + Math.floor(ad / 2)) / ad);
}

/**
 * Exact integer square root: floor(sqrt(n)), exact when n is a perfect square. Pure integer Newton's
 * method (no Math.sqrt): doubles a seed until its square exceeds n, then refines to convergence before
 * a final +-1 correction pass.
 */
export function isqrt(n: number): number {
  if (!Number.isInteger(n) || n < 0) throw new Error("isqrt: expected a non-negative integer");
  if (n === 0) return 0;
  let seed = 1;
  while (seed * seed <= n) seed *= 2;
  let x = seed;
  for (;;) {
    const next = Math.floor((x + Math.floor(n / x)) / 2);
    if (next >= x) break;
    x = next;
  }
  while (x * x > n) x -= 1;
  while ((x + 1) * (x + 1) <= n) x += 1;
  return x;
}

export function add(a: Vec3, b: Vec3): Vec3 {
  return { x: a.x + b.x, y: a.y + b.y, z: a.z + b.z };
}

export function sub(a: Vec3, b: Vec3): Vec3 {
  return { x: a.x - b.x, y: a.y - b.y, z: a.z - b.z };
}

/** Scale a vector by basis points (10000 = 1.0), rounding each component to the nearest integer. */
export function scale(v: Vec3, bp: Bp): Vec3 {
  return {
    x: roundDiv(v.x * bp, BP_SCALE),
    y: roundDiv(v.y * bp, BP_SCALE),
    z: roundDiv(v.z * bp, BP_SCALE),
  };
}

export function dot(a: Vec3, b: Vec3): number {
  return a.x * b.x + a.y * b.y + a.z * b.z;
}

/** Integer magnitude of v via isqrt of the (integer) squared length. */
export function length(v: Vec3): number {
  return isqrt(dot(v, v));
}

/** Rescale v to have integer length `len`, preserving direction. Zero vector maps to itself. */
export function normaliseTo(v: Vec3, len: number): Vec3 {
  const l = length(v);
  if (l === 0) return { x: 0, y: 0, z: 0 };
  return {
    x: roundDiv(v.x * len, l),
    y: roundDiv(v.y * len, l),
    z: roundDiv(v.z * len, l),
  };
}

/**
 * Yaw (0..1023) of the direction from `from` to `to` in the x/z plane. 0 = +x, 256 = +z. Integer-only:
 * delegates to sim/core/atan.ts's octant-reduced fixed-point atan lookup. Coincident points (zero-length
 * direction) return 0 (documented, arbitrary but deterministic).
 */
export function yawBetween(from: Vec3, to: Vec3): number {
  return yawFromComponents(to.x - from.x, to.z - from.z);
}

/** Shortest signed distance from yaw `a` to yaw `b`, in (-YAW_MAX/2, YAW_MAX/2]. */
export function angleDiff(a: number, b: number): number {
  let d = ((b - a) % YAW_MAX) + YAW_MAX;
  d %= YAW_MAX;
  if (d > YAW_MAX / 2) d -= YAW_MAX;
  return d;
}
