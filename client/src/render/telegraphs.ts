// Ground telegraph decals: circle = ring mesh, cone = sector mesh, line =
// box mesh. Each decal is coloured and outlined by its telegraph class
// (telegraphFill.ts's TELEGRAPH_STYLES: avoid / spread / stack / boss each
// get their own colour AND edge pattern), and fills in (opacity ramps up)
// as resolvesTick approaches, per telegraphFillFraction.
import * as THREE from "three";
import type { TelegraphSnapshot } from "../sim-host/snapshot.js";
import type { TelegraphClass } from "../sim-host/telegraph-map.js";
import { edgePattern, telegraphFillFraction, telegraphStyle, type TelegraphStyle } from "./telegraphFill.js";

const MM_PER_M = 1000;
const YAW_UNITS = 1024;
const EDGE_LIFT_M = 0.01;
const DOUBLE_INNER_SCALE = 0.88;

/** snapshot.ts's TelegraphSnapshot plus telegraph-map.ts's optional class (absent on the server
 * wire path, which then renders as "avoid"). */
export type RenderTelegraph = TelegraphSnapshot & { telegraphClass?: TelegraphClass };

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

/** Closed outline of the decal in the same local frame as buildGeometry, scaled about the origin. */
function outlinePoints(t: TelegraphSnapshot, scale: number): THREE.Vector3[] {
  const pts: THREE.Vector3[] = [];
  if (t.shape === "line") {
    const w = (Math.max(0.05, (t.width ?? 0) / MM_PER_M) / 2) * scale;
    const l = Math.max(0.05, (t.length ?? 0) / MM_PER_M) * scale;
    for (const [x, z] of [[-w, 0], [w, 0], [w, l], [-w, l]] as const) pts.push(new THREE.Vector3(x, EDGE_LIFT_M, z));
    return pts;
  }
  const r = Math.max(0.05, (t.radius ?? 0) / MM_PER_M) * scale;
  const full = t.shape !== "cone";
  const span = full ? Math.PI * 2 : ((t.angle1024 ?? 0) / YAW_UNITS) * Math.PI * 2;
  const segments = full ? 48 : 24;
  if (!full) pts.push(new THREE.Vector3(0, 0, EDGE_LIFT_M));
  for (let i = 0; i < (full ? segments : segments + 1); i++) {
    const a = -span / 2 + (span * i) / segments;
    pts.push(new THREE.Vector3(Math.cos(a) * r, Math.sin(a) * r, EDGE_LIFT_M));
  }
  return pts;
}

function buildEdge(t: TelegraphSnapshot, style: TelegraphStyle): THREE.Group {
  const group = new THREE.Group();
  const pattern = edgePattern(style.edge);
  for (let i = 0; i < pattern.loops; i++) {
    const geometry = new THREE.BufferGeometry().setFromPoints(outlinePoints(t, i === 0 ? 1 : DOUBLE_INNER_SCALE));
    const material = pattern.dash
      ? new THREE.LineDashedMaterial({ color: style.color, dashSize: pattern.dash.dashSize, gapSize: pattern.dash.gapSize })
      : new THREE.LineBasicMaterial({ color: style.color });
    const line = new THREE.LineLoop(geometry, material);
    if (pattern.dash) line.computeLineDistances();
    line.renderOrder = 2;
    group.add(line);
  }
  return group;
}

interface TelegraphView {
  mesh: THREE.Mesh;
  material: THREE.MeshBasicMaterial;
  edge: THREE.Group;
  shape: TelegraphSnapshot["shape"];
  telegraphClass: TelegraphClass | undefined;
}

function disposeView(view: TelegraphView): void {
  view.mesh.geometry.dispose();
  view.material.dispose();
  for (const child of view.edge.children) {
    const line = child as THREE.LineLoop;
    line.geometry.dispose();
    (line.material as THREE.Material).dispose();
  }
}

export class TelegraphPool {
  private views = new Map<string, TelegraphView>();

  constructor(private readonly scene: THREE.Scene) {}

  sync(telegraphs: readonly RenderTelegraph[], currentTick: number): void {
    const seen = new Set<string>();
    for (const t of telegraphs) {
      seen.add(t.id);
      const style = telegraphStyle(t.telegraphClass);
      let view = this.views.get(t.id);
      if (!view || view.shape !== t.shape || view.telegraphClass !== t.telegraphClass) {
        if (view) {
          this.scene.remove(view.mesh);
          disposeView(view);
        }
        const material = new THREE.MeshBasicMaterial({
          color: style.color,
          transparent: true,
          opacity: 0.2,
          side: THREE.DoubleSide,
          depthWrite: false,
        });
        const mesh = new THREE.Mesh(buildGeometry(t), material);
        mesh.rotation.x = -Math.PI / 2;
        mesh.renderOrder = 1;
        const edge = buildEdge(t, style);
        mesh.add(edge);
        this.scene.add(mesh);
        view = { mesh, material, edge, shape: t.shape, telegraphClass: t.telegraphClass };
        this.views.set(t.id, view);
      }

      view.mesh.position.set(t.center.x / MM_PER_M, 0.02, t.center.z / MM_PER_M);
      view.mesh.rotation.y = yawUnitsToRadians(t.yaw ?? 0);

      const fraction = telegraphFillFraction(currentTick, t.resolvesTick);
      view.material.opacity = 0.15 + fraction * 0.55;
      view.material.color.setHex(style.color);
    }

    for (const [id, view] of this.views) {
      if (!seen.has(id)) {
        this.scene.remove(view.mesh);
        disposeView(view);
        this.views.delete(id);
      }
    }
  }
}
