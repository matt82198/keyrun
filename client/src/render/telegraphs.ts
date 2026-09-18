// Ground telegraph decals: circle = ring mesh, cone = sector mesh, line =
// box mesh. Each fills in (opacity ramps up, danger ones redden) as
// resolvesTick approaches, per telegraphFillFraction.
import * as THREE from "three";
import type { TelegraphSnapshot } from "../sim-host/snapshot.js";
import { telegraphFillFraction } from "./telegraphFill.js";

const MM_PER_M = 1000;
const YAW_UNITS = 1024;

function yawUnitsToRadians(units: number): number {
  return -(units / YAW_UNITS) * Math.PI * 2;
}

function buildGeometry(t: TelegraphSnapshot): THREE.BufferGeometry {
  switch (t.shape) {
    case "circle": {
      const radiusM = (t.radius ?? 0) / MM_PER_M;
      return new THREE.RingGeometry(Math.max(0, radiusM * 0.8), Math.max(0.05, radiusM), 48);
    }
    case "cone": {
      const radiusM = (t.radius ?? 0) / MM_PER_M;
      const angleRad = ((t.angle1024 ?? 0) / YAW_UNITS) * Math.PI * 2;
      const shape = new THREE.Shape();
      shape.moveTo(0, 0);
      shape.absarc(0, 0, Math.max(0.05, radiusM), -angleRad / 2, angleRad / 2, false);
      shape.lineTo(0, 0);
      return new THREE.ShapeGeometry(shape, 24);
    }
    case "line": {
      const widthM = Math.max(0.05, (t.width ?? 0) / MM_PER_M);
      const lengthM = Math.max(0.05, (t.length ?? 0) / MM_PER_M);
      const geometry = new THREE.BoxGeometry(widthM, 0.02, lengthM);
      geometry.translate(0, 0, lengthM / 2);
      return geometry;
    }
    default:
      return new THREE.RingGeometry(0.4, 0.5, 16);
  }
}

interface TelegraphView {
  mesh: THREE.Mesh;
  material: THREE.MeshBasicMaterial;
  shape: TelegraphSnapshot["shape"];
}

export class TelegraphPool {
  private views = new Map<string, TelegraphView>();

  constructor(private readonly scene: THREE.Scene) {}

  sync(telegraphs: TelegraphSnapshot[], currentTick: number): void {
    const seen = new Set<string>();
    for (const t of telegraphs) {
      seen.add(t.id);
      let view = this.views.get(t.id);
      if (!view || view.shape !== t.shape) {
        if (view) this.scene.remove(view.mesh);
        const material = new THREE.MeshBasicMaterial({
          color: 0xffcc55,
          transparent: true,
          opacity: 0.2,
          side: THREE.DoubleSide,
          depthWrite: false,
        });
        const mesh = new THREE.Mesh(buildGeometry(t), material);
        mesh.rotation.x = -Math.PI / 2;
        mesh.renderOrder = 1;
        this.scene.add(mesh);
        view = { mesh, material, shape: t.shape };
        this.views.set(t.id, view);
      }

      view.mesh.position.set(t.center.x / MM_PER_M, 0.02, t.center.z / MM_PER_M);
      view.mesh.rotation.y = yawUnitsToRadians(t.yaw ?? 0);

      const fraction = telegraphFillFraction(currentTick, t.resolvesTick);
      view.material.opacity = 0.15 + fraction * 0.55;
      view.material.color.setHex(t.danger ? 0xe74c3c : 0xf4d35e);
    }

    for (const [id, view] of this.views) {
      if (!seen.has(id)) {
        this.scene.remove(view.mesh);
        view.mesh.geometry.dispose();
        view.material.dispose();
        this.views.delete(id);
      }
    }
  }
}
