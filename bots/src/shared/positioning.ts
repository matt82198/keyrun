/**
 * bots/src/shared/positioning.ts
 *
 * Integer-millimetre geometry for telegraph avoidance: point-in-shape tests
 * for circle/cone/line telegraphs, plus a small-grid nearest-safe-point
 * search. Every coordinate here -- actor positions and Telegraph
 * center/radius/length/width alike -- is already sim-space integer
 * millimetres (sim/CLAUDE.md: "Positions in millimetres (int)"), passed
 * straight from the sim with no unit conversion; there used to be a
 * `toMM()` here that re-multiplied those already-mm values by 1000 (a
 * leftover from when this module assumed float-metre input), silently
 * putting every escape distance 1000x off. Deleted -- see bots/CLAUDE.md.
 *
 * Usage: `import { inTelegraph, nearestSafePoint } from "./positioning.js"`
 */
import type { Telegraph } from "../types.js";

export type MM = number;
export interface PointMM {
  x: MM;
  z: MM;
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

/**
 * True if `p` (mm) falls inside the telegraph's danger shape.
 *
 * Two independent pre-existing bugs fixed here alongside the unit fix, both load-bearing for
 * DELIVER(1)'s cone/line escape behaviour:
 *  - `tg.yaw` is an angle1024 unit (0..1023, same convention as ActorState.yaw); pointInCone and
 *    pointInLine's `yawRad` parameter is radians. Passing the raw angle1024 integer straight
 *    through (no angle1024ToRad) read a real yaw like 200 as ~200 radians -- over 31 full turns
 *    off -- rather than ~70 degrees.
 *  - A cone's reach is `tg.radius` (matching sim/encounter/telegraphs.ts's own
 *    resolveTelegraphs: both "circle" and "cone" read `tg.radius`, only "line" reads
 *    `tg.length`/`tg.width`). Reading `tg.length` for cones read `undefined ?? 0` on every real
 *    cone telegraph (telegraph-map.ts never sets `length` on a cone), so a cone's danger zone was
 *    always radius 0 -- never actually dangerous.
 */
export function inTelegraph(p: PointMM, tg: Telegraph): boolean {
  const center = { x: tg.center.x, z: tg.center.z };
  const yawRad = angle1024ToRad(tg.yaw ?? 0);
  if (tg.shape === "circle") {
    return pointInCircle(p, center, tg.radius ?? 0);
  }
  if (tg.shape === "cone") {
    return pointInCone(p, center, yawRad, tg.angle1024 ?? 0, tg.radius ?? 0);
  }
  return pointInLine(p, center, yawRad, tg.length ?? 0, tg.width ?? 0);
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
