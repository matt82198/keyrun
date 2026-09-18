// Third-person orbit camera: right-mouse drag to look (pointer lock while
// held), scroll to zoom, orbiting a target position. Math for
// camera-relative movement lives in movement.ts (pure, tested); this file
// wires that math to a real Three.js camera and DOM pointer events.
import * as THREE from "three";

export interface OrbitCameraOptions {
  minDistance?: number;
  maxDistance?: number;
  initialDistance?: number;
  initialPitch?: number;
}

const TWO_PI = Math.PI * 2;

export class OrbitCamera {
  readonly camera: THREE.PerspectiveCamera;
  yaw = 0; // radians, 0 = looking down -Z
  pitch = -0.45; // radians, negative looks down at the target
  distance: number;
  private readonly minDistance: number;
  private readonly maxDistance: number;
  private dragging = false;

  constructor(
    private readonly domElement: HTMLElement,
    aspect: number,
    options: OrbitCameraOptions = {},
  ) {
    this.camera = new THREE.PerspectiveCamera(60, aspect, 0.1, 500);
    this.distance = options.initialDistance ?? 11;
    this.minDistance = options.minDistance ?? 2.5;
    this.maxDistance = options.maxDistance ?? 25;
    if (options.initialPitch !== undefined) this.pitch = options.initialPitch;
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
      if (document.pointerLockElement === this.domElement) {
        document.exitPointerLock?.();
      }
    });
    window.addEventListener("mousemove", (ev) => {
      if (!this.dragging) return;
      const sensitivity = 0.0025;
      this.yaw -= ev.movementX * sensitivity;
      this.yaw = ((this.yaw % TWO_PI) + TWO_PI) % TWO_PI;
      this.pitch -= ev.movementY * sensitivity;
      const limit = Math.PI / 2 - 0.05;
      this.pitch = Math.max(-limit, Math.min(limit, this.pitch));
    });
    this.domElement.addEventListener("wheel", (ev) => {
      ev.preventDefault();
      const delta = Math.sign(ev.deltaY) * 0.5;
      this.distance = Math.max(this.minDistance, Math.min(this.maxDistance, this.distance + delta));
    });
    this.domElement.addEventListener("contextmenu", (ev) => ev.preventDefault());
  }

  setAspect(aspect: number): void {
    this.camera.aspect = aspect;
    this.camera.updateProjectionMatrix();
  }

  /** Positions the camera relative to a world-space target (metres). */
  update(targetX: number, targetY: number, targetZ: number): void {
    const horizontal = Math.cos(this.pitch) * this.distance;
    const height = Math.sin(-this.pitch) * this.distance;
    const offsetX = Math.sin(this.yaw) * horizontal;
    const offsetZ = Math.cos(this.yaw) * horizontal;
    this.camera.position.set(targetX + offsetX, targetY + height, targetZ + offsetZ);
    this.camera.lookAt(targetX, targetY + 1, targetZ);
  }
}
