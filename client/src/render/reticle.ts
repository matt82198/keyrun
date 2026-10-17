// World-space target reticle: a ring on the ground under the player's
// current target, plus a screen-space crosshair is drawn in hud.ts.
import * as THREE from "three";

export class TargetReticle {
  private readonly mesh: THREE.Mesh;

  constructor(scene: THREE.Scene) {
    const geometry = new THREE.RingGeometry(0.85, 1.1, 40); // D10: clears a tank-width or boss body
    const material = new THREE.MeshBasicMaterial({
      color: 0xffe066,
      transparent: true,
      opacity: 0.9,
      side: THREE.DoubleSide,
      depthWrite: false,
    });
    this.mesh = new THREE.Mesh(geometry, material);
    this.mesh.rotation.x = -Math.PI / 2;
    this.mesh.renderOrder = 2;
    this.mesh.visible = false;
    scene.add(this.mesh);
  }

  update(worldPos: THREE.Vector3 | null): void {
    if (!worldPos) {
      this.mesh.visible = false;
      return;
    }
    this.mesh.visible = true;
    this.mesh.position.set(worldPos.x, 0.03, worldPos.z);
  }
}
