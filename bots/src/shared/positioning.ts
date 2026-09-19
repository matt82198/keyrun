/**
 * bots/src/shared/positioning.ts
 *
 * Integer-millimetre geometry for telegraph avoidance: point-in-shape tests
 * for circle/cone/line telegraphs, plus a small-grid nearest-safe-point
 * search. All math here is integer mm to stay deterministic across
 * platforms (no float drift in replay comparisons).
 *
 * Usage: `import { toMM, inTelegraph, nearestSafePoint } from "./positioning.js"`
 */
import type { Telegraph } from "../types.js";

export type MM = number;
export interface PointMM {
  x: MM;
  z: MM;
}

/** Convert a sim-space (metres, float) coordinate to integer millimetres. */
export function toMM(metres: number): MM {
  return Math.round(metres * 1000);
}

/** Convert an angle1024 unit (0..1024 = one full turn) to radians. */
export function angle1024ToRad(units: number): number {
  return (units * 2 * Math.PI) / 1024;
}

export function pointInCircle(p: PointMM, center: PointMM, radiusMM: number): boolean {
  const dx = p.x - center.x;
  const dz = p.z - center.z;
  return dx * dx + dz * dz <= radiusMM * radiusMM;
}

/** Cone apex at `center`, opening along `yawRad`, half-width `angle1024` units, out to `lengthMM`. */
export function pointInCone(
  p: PointMM,
  center: PointMM,
  yawRad: number,
  angle1024: number,
  lengthMM: number
): boolean {
  const dx = p.x - center.x;
  const dz = p.z - center.z;
  const dist = Math.sqrt(dx * dx + dz * dz);
  if (dist > lengthMM) return false;
  if (dist === 0) return true;
  const pointAngle = Math.atan2(dz, dx);
  let delta = pointAngle - yawRad;
  while (delta > Math.PI) delta -= 2 * Math.PI;
  while (delta < -Math.PI) delta += 2 * Math.PI;
  const halfWidthRad = angle1024ToRad(angle1024) / 2;
  return Math.abs(delta) <= halfWidthRad;
}

/** Rectangle-as-line telegraph from `center`, extending `lengthMM` along `yawRad`, `widthMM` wide. */
export function pointInLine(
  p: PointMM,
  center: PointMM,
  yawRad: number,
  lengthMM: number,
  widthMM: number
): boolean {
  const dx = p.x - center.x;
  const dz = p.z - center.z;
  const forward = dx * Math.cos(yawRad) + dz * Math.sin(yawRad);
  const side = -dx * Math.sin(yawRad) + dz * Math.cos(yawRad);
  return forward >= 0 && forward <= lengthMM && Math.abs(side) <= widthMM / 2;
}

/** True if `p` (mm) falls inside the telegraph's danger shape. */
export function inTelegraph(p: PointMM, tg: Telegraph): boolean {
  const center = { x: toMM(tg.center.x), z: toMM(tg.center.z) };
  if (tg.shape === "circle") {
    return pointInCircle(p, center, toMM(tg.radius ?? 0));
  }
  if (tg.shape === "cone") {
    return pointInCone(p, center, tg.yaw ?? 0, tg.angle1024 ?? 0, toMM(tg.length ?? 0));
  }
  return pointInLine(p, center, tg.yaw ?? 0, toMM(tg.length ?? 0), toMM(tg.width ?? 0));
}

export function isInAnyDanger(p: PointMM, telegraphs: Telegraph[]): boolean {
  return telegraphs.some((tg) => tg.danger && inTelegraph(p, tg));
}

/**
 * Search an outward-expanding square grid (step `gridStepMM`) around `from`
 * for the nearest point that lands in none of the dangerous telegraphs and
 * satisfies `extraCheck` (default: always true -- e.g. shared/floor.ts's
 * healer range-keeping passes a "stays within heal range" predicate here so
 * the same grid search serves both callers without duplicating it).
 * Returns `from` unchanged if it already qualifies; null if no qualifying
 * point is found within `maxRingsSearched` rings.
 */
export function nearestSafePoint(
  from: PointMM,
  telegraphs: Telegraph[],
  gridStepMM = 500,
  maxRingsSearched = 20,
  extraCheck: (p: PointMM) => boolean = () => true
): PointMM | null {
  if (!isInAnyDanger(from, telegraphs) && extraCheck(from)) return from;
  for (let ring = 1; ring <= maxRingsSearched; ring++) {
    const candidates: PointMM[] = [];
    for (let i = -ring; i <= ring; i++) {
      for (let j = -ring; j <= ring; j++) {
        if (Math.max(Math.abs(i), Math.abs(j)) !== ring) continue;
        candidates.push({ x: from.x + i * gridStepMM, z: from.z + j * gridStepMM });
      }
    }
    candidates.sort((a, b) => {
      const da = (a.x - from.x) ** 2 + (a.z - from.z) ** 2;
      const db = (b.x - from.x) ** 2 + (b.z - from.z) ** 2;
      return da - db;
    });
    for (const c of candidates) {
      if (!isInAnyDanger(c, telegraphs) && extraCheck(c)) return c;
    }
  }
  return null;
}
