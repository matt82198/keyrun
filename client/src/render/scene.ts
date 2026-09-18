// Scene bootstrap: renderer, 60m x 60m ground grid, lighting. Pure Three.js
// wiring; no game logic lives here.
import * as THREE from "three";

export interface SceneBundle {
  scene: THREE.Scene;
  renderer: THREE.WebGLRenderer;
}

export function createScene(canvas: HTMLCanvasElement): SceneBundle {
  const scene = new THREE.Scene();
  scene.background = new THREE.Color(0x0c0f14);
  scene.fog = new THREE.Fog(0x0c0f14, 25, 70);

  const renderer = new THREE.WebGLRenderer({ canvas, antialias: true });
  renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));

  const hemi = new THREE.HemisphereLight(0x8899aa, 0x11141a, 1.1);
  scene.add(hemi);
  const sun = new THREE.DirectionalLight(0xffffff, 1.4);
  sun.position.set(15, 25, 10);
  scene.add(sun);

  const ground = new THREE.Mesh(
    new THREE.PlaneGeometry(60, 60),
    new THREE.MeshStandardMaterial({ color: 0x1b2027, roughness: 1 }),
  );
  ground.rotation.x = -Math.PI / 2;
  scene.add(ground);

  const grid = new THREE.GridHelper(60, 30, 0x3a4452, 0x262c35);
  scene.add(grid);

  return { scene, renderer };
}
