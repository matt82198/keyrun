// Third-person orbit camera: right-mouse drag to look (pointer lock while held), scroll to zoom,
// orbiting a smoothed follow point that trails the player. All feel math and constants live in
// cameraMath.ts (pure, tested -- CAMERA_TUNING); camera-relative WASD lives in movement.ts. This
// file only wires that math to a real Three.js camera and DOM pointer events. It never writes sim
// state: `yaw` is read by hud-wiring.ts as the movement basis, so W always moves camera-forward.
import * as THREE from "three";
import {
  CAMERA_TUNING,
  applyMouseLook,
  applyZoomStep,
  cameraOffset,
  smoothFollow,
  smoothScalar,
  type CameraTuning,
  type Vec3M,
} from "./cameraMath.js";

export interface OrbitCameraOptions {
  tuning?: CameraTuning;
  /** Clock for frame dt (ms); defaults to performance.now. Injected for tests/tools. */
  now?: () => number;
}

export class OrbitCamera {
  readonly camera: THREE.PerspectiveCamera;
  /** Radians, 0 = looking down -Z. Unsmoothed so the movement basis tracks the mouse exactly. */
  yaw = 0;
  /** Radians, negative looks down at the target. */
  pitch: number;
  /** Current (smoothed) orbit distance in metres. */
  distance: number;
  private targetDistance: number;
  private follow: Vec3M | null = null;
  private lastFrameMs: number | null = null;
  private dragging = false;
  private readonly tuning: CameraTuning;
  private readonly now: () => number;

  constructor(
    private readonly domElement: HTMLElement,
    aspect: number,
    options: OrbitCameraOptions = {},
  ) {
    this.tuning = options.tuning ?? CAMERA_TUNING;
    this.now = options.now ?? (() => performance.now());
    this.camera = new THREE.PerspectiveCamera(60, aspect, 0.1, 500);
    this.pitch = this.tuning.initialPitchRad;
    this.distance = this.tuning.initialDistanceM;
    this.targetDistance = this.distance;
    this.bind();
  }

  private bind(): void {
    this.domElement.addEventListener("mousedown", (ev) => {
      if (ev.button !== 2) return;
      this.dragging = true;
      this.domElement.requestPointerLock?.();
    });
    window.addEventListener("mouseup", (ev) => {
      if (ev.button !== 2) return;
      this.dragging = false;
      if (document.pointerLockElement === this.domElement) document.exitPointerLock?.();
    });
    window.addEventListener("mousemove", (ev) => {
      if (!this.dragging) return;
      const view = applyMouseLook({ yaw: this.yaw, pitch: this.pitch }, ev.movementX, ev.movementY, this.tuning);
      this.yaw = view.yaw;
      this.pitch = view.pitch;
    });
    this.domElement.addEventListener("wheel", (ev) => {
      ev.preventDefault();
      this.targetDistance = applyZoomStep(this.targetDistance, ev.deltaY, this.tuning);
    });
    this.domElement.addEventListener("contextmenu", (ev) => ev.preventDefault());
  }

  setAspect(aspect: number): void {
    this.camera.aspect = aspect;
    this.camera.updateProjectionMatrix();
  }

  /** Called once per render frame with the player's world-space position (metres). */
  update(targetX: number, targetY: number, targetZ: number): void {
    const nowMs = this.now();
    const dtS = this.lastFrameMs === null ? 0 : Math.min((nowMs - this.lastFrameMs) / 1000, this.tuning.maxFrameDtS);
    this.lastFrameMs = nowMs;

    const target = { x: targetX, y: targetY, z: targetZ };
    this.follow = this.follow === null ? target : smoothFollow(this.follow, target, dtS, this.tuning);
    this.distance = smoothScalar(this.distance, this.targetDistance, dtS, this.tuning.zoomHalfLifeS);

    const off = cameraOffset(this.yaw, this.pitch, this.distance);
    const f = this.follow;
    this.camera.position.set(f.x + off.x, f.y + off.y, f.z + off.z);
    this.camera.lookAt(f.x, f.y + this.tuning.lookAtHeightM, f.z);
  }
}
