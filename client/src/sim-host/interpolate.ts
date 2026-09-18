// Pure interpolation between the last two sim snapshots. No Three.js
// dependency, so unit-testable directly.

import type { ActorCast, Snapshot, Vec3Mm } from "./snapshot.js";

export interface InterpolatedActor {
  id: number;
  pos: Vec3Mm;
  yaw: number;
  health: number;
  maxHealth: number;
  resource: number;
  maxResource: number;
  alive: boolean;
  targetId: number | null;
  cast: ActorCast | null;
}

const YAW_RANGE = 1024;

function lerp(a: number, b: number, t: number): number {
  return a + (b - a) * t;
}

/** Shortest-path interpolation across the circular 0..1023 yaw range. */
function lerpYaw1024(a: number, b: number, t: number): number {
  let diff = (((b - a + YAW_RANGE / 2) % YAW_RANGE) + YAW_RANGE) % YAW_RANGE - YAW_RANGE / 2;
  let result = a + diff * t;
  result = ((result % YAW_RANGE) + YAW_RANGE) % YAW_RANGE;
  return result;
}

/**
 * Interpolates actor transforms between two snapshots. `alpha` is clamped to
 * [0, 1]; 0 returns `prev`'s values, 1 returns `next`'s. Discrete fields
 * (health bars filling, cooldowns, etc.) are taken from `next` as-is since
 * they change on tick boundaries, not continuously. Actors with no match in
 * `prev` (newly spawned) snap straight to their `next` transform.
 */
export function interpolateSnapshots(prev: Snapshot, next: Snapshot, alpha: number): InterpolatedActor[] {
  const t = Math.min(1, Math.max(0, alpha));
  const prevById = new Map(prev.actors.map((a) => [a.id, a] as const));

  return next.actors.map((na) => {
    const pa = prevById.get(na.id);
    const pos: Vec3Mm = pa
      ? { x: lerp(pa.pos.x, na.pos.x, t), y: lerp(pa.pos.y, na.pos.y, t), z: lerp(pa.pos.z, na.pos.z, t) }
      : { ...na.pos };
    const yaw = pa ? lerpYaw1024(pa.yaw, na.yaw, t) : na.yaw;

    return {
      id: na.id,
      pos,
      yaw,
      health: na.health,
      maxHealth: na.maxHealth,
      resource: na.resource,
      maxResource: na.maxResource,
      alive: na.alive,
      targetId: na.targetId,
      cast: na.cast,
    };
  });
}
