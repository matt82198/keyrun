/**
 * client/src/render/dash-trail.ts
 *
 * Item 13: the Space dash moves an actor sim/combat/movement.ts's DASH_DISTANCE_MM in ONE tick (a
 * walk tick is ~180 mm), so a dash is readable from snapshot positions alone -- no new event, and the
 * same on the local host and ?net=1. `detectDashes` compares each live actor's snapshot position
 * with the last one it saw: a jump between DASH_MIN_MM and DASH_MAX_MM is a dash (shorter = walking,
 * even across a few skipped snapshots; longer = a teleport/respawn, not drawn). render/cast-fx.ts
 * draws the returned moves as a fading ground strip for DASH_TRAIL_SECONDS. Display only.
 *
 * Usage: `for (const d of detectDashes(lastPos, rawById)) drawTrail(d);` once per render frame.
 */
import * as THREE from "three";
import type { ActorSnapshot, Vec2Mm } from "../sim-host/snapshot.js";
import { DASH_DISTANCE_MM } from "../../../sim/combat/movement.js";

export const DASH_MIN_MM = DASH_DISTANCE_MM / 2;
export const DASH_MAX_MM = DASH_DISTANCE_MM * 2;
export const DASH_TRAIL_SECONDS = 0.45;

export interface DashMove {
  id: number;
  from: Vec2Mm;
  to: Vec2Mm;
}

const MM_PER_M = 1000;
const TRAIL_WIDTH_M = 0.5;

/** Three yaw (ground-frame.ts convention: local +x forward) from take-off toward landing. */
export function dashYaw(d: DashMove): number {
  return Math.atan2(-(d.to.z - d.from.z), d.to.x - d.from.x);
}

/** The strip from take-off (local origin) to landing along local +x, in metres. */
export function dashGeometry(d: DashMove): THREE.BufferGeometry {
  const len = Math.hypot(d.to.x - d.from.x, d.to.z - d.from.z) / MM_PER_M;
  return new THREE.PlaneGeometry(len, TRAIL_WIDTH_M).translate(len / 2, 0, 0);
}

/** Mutates `last` to the current positions (dead actors are forgotten). */
export function detectDashes(last: Map<number, Vec2Mm>, rawById: ReadonlyMap<number, ActorSnapshot>): DashMove[] {
  const out: DashMove[] = [];
  for (const a of rawById.values()) {
    if (!a.alive) {
      last.delete(a.id);
      continue;
    }
    const to = { x: a.pos.x, z: a.pos.z };
    const from = last.get(a.id);
    last.set(a.id, to);
    if (!from) continue;
    const d = Math.hypot(to.x - from.x, to.z - from.z);
    if (d >= DASH_MIN_MM && d <= DASH_MAX_MM) out.push({ id: a.id, from, to });
  }
  return out;
}
