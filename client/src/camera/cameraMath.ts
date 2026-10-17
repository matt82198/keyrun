// Pure third-person camera math (D10 camera-feel pass): follow smoothing, mouse-look, pitch clamp,
// scroll zoom, and the orbit offset. No Three.js/DOM dependency so it is unit-testable
// (tests/client/camera-feel.test.ts); orbitCamera.ts wires it to a real camera and pointer events.
//
// Usage: pos = smoothFollow(pos, playerPos, dtS); view = applyMouseLook(view, ev.movementX, ev.movementY);
//        target = applyZoomStep(target, ev.deltaY); off = cameraOffset(yaw, pitch, distance);
//
// Conventions match movement.ts: yaw 0 = camera looks down -Z, so the camera sits on +Z of its
// target; pitch < 0 looks down at the target. Every feel constant lives in CAMERA_TUNING.

/** Every tunable camera-feel constant, in one place. */
export const CAMERA_TUNING = {
  /** Seconds for the camera's follow point to close half the gap to the player (position lag). */
  followHalfLifeS: 0.07,
  /** Seconds for the actual zoom distance to close half the gap to the scrolled target distance. */
  zoomHalfLifeS: 0.06,
  /** A follow gap larger than this (metres) is a teleport/respawn: snap instead of gliding. */
  snapDistanceM: 8,
  /** Largest frame dt (seconds) fed to smoothing, so a backgrounded tab does not lurch. */
  maxFrameDtS: 0.1,
  /** Radians of yaw/pitch per pixel of mouse movement while right-click mouse-look is held. */
  mouseSensitivityRadPerPx: 0.0025,
  /** Steepest look-down pitch (radians, ~80 degrees). */
  pitchMinRad: -1.4,
  /** Shallowest pitch: stays slightly negative so the camera never dips below the ground plane. */
  pitchMaxRad: -0.05,
  /** Starting pitch (radians): ~43 deg down (D10 first-run readability), so the pack behind the party is visible. */
  initialPitchRad: -0.75,
  /** Closest / farthest / starting orbit distance (metres). */
  minDistanceM: 2.5,
  maxDistanceM: 25,
  initialDistanceM: 13,
  /** Metres of zoom per scroll-wheel notch. */
  zoomStepM: 0.75,
  /** Height above the player's feet the camera looks at (metres). */
  lookAtHeightM: 1,
} as const;

export type CameraTuning = { readonly [K in keyof typeof CAMERA_TUNING]: number };

export interface Vec3M {
  x: number;
  y: number;
  z: number;
}

export interface ViewAngles {
  yaw: number;
  pitch: number;
}

const TWO_PI = Math.PI * 2;

/** Fraction of the remaining gap to close this frame for exponential smoothing with the given
 * half-life. Frame-rate independent: two dt/2 steps equal one dt step. halfLife <= 0 snaps. */
export function expSmoothingAlpha(dtS: number, halfLifeS: number): number {
  if (halfLifeS <= 0) return 1;
  if (dtS <= 0) return 0;
  return 1 - Math.pow(2, -dtS / halfLifeS);
}

/** Eases a scalar toward a target (critically damped first-order; never overshoots). */
export function smoothScalar(current: number, target: number, dtS: number, halfLifeS: number): number {
  return current + (target - current) * expSmoothingAlpha(dtS, halfLifeS);
}

/** Eases the camera's follow point toward the player's position; snaps on a teleport-sized gap. */
export function smoothFollow(current: Vec3M, target: Vec3M, dtS: number, tuning: CameraTuning = CAMERA_TUNING): Vec3M {
  const gap = Math.hypot(target.x - current.x, target.y - current.y, target.z - current.z);
  if (gap > tuning.snapDistanceM) return { ...target };
  const a = expSmoothingAlpha(Math.min(dtS, tuning.maxFrameDtS), tuning.followHalfLifeS);
  return {
    x: current.x + (target.x - current.x) * a,
    y: current.y + (target.y - current.y) * a,
    z: current.z + (target.z - current.z) * a,
  };
}

export function clampPitch(pitch: number, tuning: CameraTuning = CAMERA_TUNING): number {
  return Math.max(tuning.pitchMinRad, Math.min(tuning.pitchMaxRad, pitch));
}

/** Applies one mouse-move delta (pixels) to the view: yaw wraps into [0, 2pi), pitch clamps. */
export function applyMouseLook(
  view: ViewAngles,
  movementX: number,
  movementY: number,
  tuning: CameraTuning = CAMERA_TUNING,
): ViewAngles {
  const yaw = view.yaw - movementX * tuning.mouseSensitivityRadPerPx;
  return {
    yaw: ((yaw % TWO_PI) + TWO_PI) % TWO_PI,
    pitch: clampPitch(view.pitch - movementY * tuning.mouseSensitivityRadPerPx, tuning),
  };
}

/** One scroll-wheel event: +deltaY zooms out one notch, -deltaY in; clamped to min/max. */
export function applyZoomStep(distance: number, deltaY: number, tuning: CameraTuning = CAMERA_TUNING): number {
  const next = distance + Math.sign(deltaY) * tuning.zoomStepM;
  return Math.max(tuning.minDistanceM, Math.min(tuning.maxDistanceM, next));
}

/** Camera position relative to its follow point for a given orbit (metres). */
export function cameraOffset(yaw: number, pitch: number, distance: number): Vec3M {
  const horizontal = Math.cos(pitch) * distance;
  return { x: Math.sin(yaw) * horizontal, y: Math.sin(-pitch) * distance, z: Math.cos(yaw) * horizontal };
}
