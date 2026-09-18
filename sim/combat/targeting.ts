/**
 * Tab-target cycling and integer shape queries (circle/cone/line) over actor positions in the
 * XZ plane. Facing direction comes from a fixed 128-entry Q12 (x4096) cos/sin lookup table,
 * generated once offline and embedded as literal constants below, indexed by yaw in angle1024
 * units (0..1023 = one full turn, matching AbilityDef.shape.angle1024). This keeps facing math
 * integer-only at runtime and platform-deterministic (no Math.sin/cos call during simulation).
 * Usage: inCircle(origin, radiusMm, actors); inCone(caster, angle1024, radiusMm, actors);
 *        cycleTarget(actors, currentTargetId);
 */
import type { ActorId, ActorState, Vec3 } from "./types";
import { isqrt } from "./movement";

const TABLE_N = 128;
const STEP = 1024 / TABLE_N;
export const FACING_Q = 4096;

const COS = [
  4096, 4091, 4076, 4052, 4017, 3973, 3920, 3857, 3784, 3703, 3612, 3513, 3406, 3290, 3166, 3035,
  2896, 2751, 2598, 2440, 2276, 2106, 1931, 1751, 1567, 1380, 1189, 995, 799, 601, 401, 201,
  0, -201, -401, -601, -799, -995, -1189, -1380, -1567, -1751, -1931, -2106, -2276, -2440, -2598, -2751,
  -2896, -3035, -3166, -3290, -3406, -3513, -3612, -3703, -3784, -3857, -3920, -3973, -4017, -4052, -4076, -4091,
  -4096, -4091, -4076, -4052, -4017, -3973, -3920, -3857, -3784, -3703, -3612, -3513, -3406, -3290, -3166, -3035,
  -2896, -2751, -2598, -2440, -2276, -2106, -1931, -1751, -1567, -1380, -1189, -995, -799, -601, -401, -201,
  0, 201, 401, 601, 799, 995, 1189, 1380, 1567, 1751, 1931, 2106, 2276, 2440, 2598, 2751,
  2896, 3035, 3166, 3290, 3406, 3513, 3612, 3703, 3784, 3857, 3920, 3973, 4017, 4052, 4076, 4091,
];

const SIN = [
  0, 201, 401, 601, 799, 995, 1189, 1380, 1567, 1751, 1931, 2106, 2276, 2440, 2598, 2751,
  2896, 3035, 3166, 3290, 3406, 3513, 3612, 3703, 3784, 3857, 3920, 3973, 4017, 4052, 4076, 4091,
  4096, 4091, 4076, 4052, 4017, 3973, 3920, 3857, 3784, 3703, 3612, 3513, 3406, 3290, 3166, 3035,
  2896, 2751, 2598, 2440, 2276, 2106, 1931, 1751, 1567, 1380, 1189, 995, 799, 601, 401, 201,
  0, -201, -401, -601, -799, -995, -1189, -1380, -1567, -1751, -1931, -2106, -2276, -2440, -2598, -2751,
  -2896, -3035, -3166, -3290, -3406, -3513, -3612, -3703, -3784, -3857, -3920, -3973, -4017, -4052, -4076, -4091,
  -4096, -4091, -4076, -4052, -4017, -3973, -3920, -3857, -3784, -3703, -3612, -3513, -3406, -3290, -3166, -3035,
  -2896, -2751, -2598, -2440, -2276, -2106, -1931, -1751, -1567, -1380, -1189, -995, -799, -601, -401, -201,
];

function tableIndex(angle1024: number): number {
  return (((Math.round(angle1024 / STEP) % TABLE_N) + TABLE_N) % TABLE_N);
}

/** Returns the caster's Q12-scaled facing direction vector in the XZ plane. */
export function faceDir(yaw1024: number): { x: number; z: number } {
  const idx = tableIndex(yaw1024);
  return { x: COS[idx]!, z: SIN[idx]! };
}

export function inCircle(origin: Vec3, radiusMm: number, actors: ActorState[]): ActorId[] {
  const r2 = radiusMm * radiusMm;
  return actors
    .filter((a) => {
      const dx = a.pos.x - origin.x;
      const dz = a.pos.z - origin.z;
      return dx * dx + dz * dz <= r2;
    })
    .map((a) => a.id);
}

export function inCone(caster: ActorState, angle1024: number, radiusMm: number, actors: ActorState[]): ActorId[] {
  const face = faceDir(caster.yaw);
  const cosHalf = COS[tableIndex(angle1024 / 2)]!;
  const r2 = radiusMm * radiusMm;
  return actors
    .filter((a) => {
      if (a.id === caster.id) return false;
      const dx = a.pos.x - caster.pos.x;
      const dz = a.pos.z - caster.pos.z;
      const dist2 = dx * dx + dz * dz;
      if (dist2 === 0 || dist2 > r2) return false;
      const dist = isqrt(dist2);
      const dot = face.x * dx + face.z * dz;
      return dot >= cosHalf * dist;
    })
    .map((a) => a.id);
}

/** `radiusMm` here is the line's half-width; targets within [0, length] ahead and within the width. */
export function inLine(caster: ActorState, length: number, radiusMm: number, actors: ActorState[]): ActorId[] {
  const face = faceDir(caster.yaw);
  return actors
    .filter((a) => {
      if (a.id === caster.id) return false;
      const dx = a.pos.x - caster.pos.x;
      const dz = a.pos.z - caster.pos.z;
      const forward = Math.trunc((face.x * dx + face.z * dz) / FACING_Q);
      const perp = Math.trunc((face.x * dz - face.z * dx) / FACING_Q);
      return forward >= 0 && forward <= length && Math.abs(perp) <= radiusMm;
    })
    .map((a) => a.id);
}

/** Cycles to the next living actor id after `fromId` in ascending id order (wraps around). */
export function cycleTarget(actors: ActorState[], fromId: ActorId | null): ActorId | null {
  const candidates = actors.filter((a) => a.alive).map((a) => a.id).sort((x, y) => x - y);
  if (candidates.length === 0) return null;
  if (fromId === null) return candidates[0]!;
  const idx = candidates.indexOf(fromId);
  const nextIdx = idx === -1 ? 0 : (idx + 1) % candidates.length;
  return candidates[nextIdx]!;
}
