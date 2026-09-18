// Pure camera-relative WASD movement math. No Three.js dependency: the
// camera yaw is passed in as radians so this is directly unit-testable.

export interface MoveKeyState {
  forward: boolean;
  back: boolean;
  left: boolean;
  right: boolean;
}

export interface MoveDeltaMm {
  dx: number;
  dz: number;
}

/**
 * Computes one tick's camera-relative movement delta in integer millimetres.
 *
 * Convention: cameraYawRad = 0 means the camera looks down -Z (Three.js
 * default), and yaw increases toward +X. "Forward" (W) moves along the
 * camera's look direction projected onto the ground plane; "right" (D)
 * strafes perpendicular to it. Diagonal input is normalized so strafing
 * while moving forward is not faster than moving in a single direction.
 */
export function computeMoveDeltaMm(
  keys: MoveKeyState,
  cameraYawRad: number,
  speedMmPerTick: number,
): MoveDeltaMm {
  const forwardInput = (keys.forward ? 1 : 0) - (keys.back ? 1 : 0);
  const strafeInput = (keys.right ? 1 : 0) - (keys.left ? 1 : 0);

  if (forwardInput === 0 && strafeInput === 0) {
    return { dx: 0, dz: 0 };
  }

  const forwardX = -Math.sin(cameraYawRad);
  const forwardZ = -Math.cos(cameraYawRad);
  const rightX = Math.cos(cameraYawRad);
  const rightZ = -Math.sin(cameraYawRad);

  let x = forwardInput * forwardX + strafeInput * rightX;
  let z = forwardInput * forwardZ + strafeInput * rightZ;

  const len = Math.hypot(x, z);
  if (len > 0) {
    x /= len;
    z /= len;
  }

  return {
    // `|| 0` normalizes -0 (e.g. from -sin(0)) to 0 for clean integer output.
    dx: Math.round(x * speedMmPerTick) || 0,
    dz: Math.round(z * speedMmPerTick) || 0,
  };
}
