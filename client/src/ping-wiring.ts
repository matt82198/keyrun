/**
 * client/src/ping-wiring.ts
 *
 * The G-ping's world plumbing for hud-wiring.ts (split out to keep that file under the 190-LOC gate):
 *
 * - `aimedGroundPoint`: where a spot ping lands -- the floor point (y = 0) the camera's centre ray
 *   hits. The HUD crosshair is drawn at screen centre (render/hud.ts), so the centre ray IS the
 *   crosshair ray. Null when the camera looks level/up or the hit is beyond MAX_SPOT_PING_RANGE_M;
 *   the caller then falls back to the pinger's own position.
 * - `pingContextFor`: the render/pingLabel.ts context for a target ping, from the latest raw
 *   snapshot: an enemy's still-running cast with `kickable === true` -> "Kick!", an open Window
 *   (tick < endsTick) -> "Window!".
 * - `pingPlacement`: a target ping sits on its target; a spot ping on its own ground point (mm).
 * - `exposePings`: a READ-ONLY `window.__keyrunPings` (label + floor point of each ping as drawn)
 *   for the headless e2e, the same pattern as readability.ts's `window.__keyrunReadability`.
 *
 * Usage: see hud-wiring.ts's runGameLoop.
 */
import * as THREE from "three";
import type { PingEvent, PingGroundPoint } from "./input/ping.js";
import type { PingLabelContext } from "./render/pingLabel.js";
import type { PingWorldPlacement } from "./render/pings.js";
import type { ActorSnapshot } from "./sim-host/snapshot.js";

const MM_PER_M = 1000;
/** A centre ray that only meets the floor this far from the camera (metres) is treated as a miss. */
export const MAX_SPOT_PING_RANGE_M = 80;

interface XYZ {
  x: number;
  y: number;
  z: number;
}

/** Floor (y = 0) hit of the ray `origin + t * dir` (metres in, mm out), or null for no hit in range. */
export function groundPointOnRay(origin: XYZ, dir: XYZ, maxRangeM = MAX_SPOT_PING_RANGE_M): PingGroundPoint | null {
  if (!(dir.y < 0) || origin.y <= 0) return null;
  const t = -origin.y / dir.y;
  if (t * Math.hypot(dir.x, dir.y, dir.z) > maxRangeM) return null;
  return { x: Math.round((origin.x + t * dir.x) * MM_PER_M), z: Math.round((origin.z + t * dir.z) * MM_PER_M) };
}

export interface AimCamera {
  position: XYZ;
  getWorldDirection(target: THREE.Vector3): THREE.Vector3;
}

export function aimedGroundPoint(camera: AimCamera): PingGroundPoint | null {
  return groundPointOnRay(camera.position, camera.getWorldDirection(new THREE.Vector3()));
}

export function pingContextFor(event: PingEvent, rawById: ReadonlyMap<number, ActorSnapshot>, tick: number): PingLabelContext {
  const target = event.targetId === undefined ? undefined : rawById.get(event.targetId);
  if (!target) return { targetIsInterruptibleCast: false, targetHasWindow: false };
  const cast = target.cast;
  return {
    targetIsInterruptibleCast: target.kind === "enemy" && cast?.kickable === true && tick < cast.endTick,
    targetHasWindow: !!target.window && tick < target.window.endsTick,
  };
}

/** One ping as drawn: its label text and floor point (metres). */
export interface ShownPing {
  kind: PingEvent["kind"];
  targetId: number | null;
  label: string;
  x: number;
  z: number;
}

declare global {
  interface Window {
    /** Read-only e2e view (tests/client/e2e/window-ping.e2e.ts) of the last pings drawn, newest last. */
    __keyrunPings?: { labels(): ShownPing[] };
  }
}

const SHOWN_PING_CAP = 32;

/** Installs `window.__keyrunPings` and returns the recorder hud-wiring.ts feeds each label through. */
export function exposePings(): { note(event: PingEvent, label: string, at: PingWorldPlacement | null): string } {
  const shown: ShownPing[] = [];
  if (typeof window !== "undefined") window.__keyrunPings = Object.freeze({ labels: () => shown.map((p) => ({ ...p })) });
  return {
    note(event, label, at) {
      shown.push({ kind: event.kind, targetId: event.targetId ?? null, label, x: at?.x ?? NaN, z: at?.z ?? NaN });
      if (shown.length > SHOWN_PING_CAP) shown.shift();
      return label;
    },
  };
}

export function pingPlacement(event: PingEvent, worldPositionOf: (id: number) => XYZ | null): PingWorldPlacement | null {
  if (event.targetId !== undefined) {
    const pos = worldPositionOf(event.targetId);
    return pos ? { x: pos.x, y: pos.y, z: pos.z } : null;
  }
  if (event.point) return { x: event.point.x / MM_PER_M, y: 0, z: event.point.z / MM_PER_M };
  const pos = worldPositionOf(event.actorId);
  return pos ? { x: pos.x, y: 0, z: pos.z } : null;
}
