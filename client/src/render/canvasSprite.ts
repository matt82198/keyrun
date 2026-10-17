// Shared helper for canvas-texture billboard sprites (health/resource/cast
// bars, floating damage numbers). Each sprite owns a small 2D canvas that
// callers redraw into and then call `sync()` to push to the GPU texture.
import * as THREE from "three";

export interface CanvasSprite {
  sprite: THREE.Sprite;
  canvas: HTMLCanvasElement;
  ctx: CanvasRenderingContext2D;
  /** Call after drawing into ctx to upload the new pixels. */
  sync(): void;
}

export function createCanvasSprite(
  pixelWidth: number,
  pixelHeight: number,
  worldWidth: number,
  worldHeight: number,
  /** false = constant on-screen size (name tags, D10): world size is then the size at 1 m depth. */
  sizeAttenuation = true,
): CanvasSprite {
  const canvas = document.createElement("canvas");
  canvas.width = pixelWidth;
  canvas.height = pixelHeight;
  const ctx = canvas.getContext("2d");
  if (!ctx) throw new Error("2d canvas context unavailable");

  const texture = new THREE.CanvasTexture(canvas);
  texture.minFilter = THREE.LinearFilter;
  const material = new THREE.SpriteMaterial({ map: texture, transparent: true, depthTest: false, sizeAttenuation });
  const sprite = new THREE.Sprite(material);
  sprite.scale.set(worldWidth, worldHeight, 1);
  sprite.renderOrder = 10;

  return {
    sprite,
    canvas,
    ctx,
    sync() {
      texture.needsUpdate = true;
    },
  };
}
