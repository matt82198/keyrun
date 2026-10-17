/**
 * tests/client/render/telegraphs.test.ts
 *
 * Drives the real render/telegraphs.ts TelegraphPool against a minimal recording fake of the
 * "three" module (three ships from cdnjs at runtime, not node_modules, so vitest cannot load the
 * real one). The fake only records what the pool constructs; the assertions are on what the pool
 * actually built: each telegraph class gets its own fill colour and its own edge outline.
 */
import { describe, expect, it, vi } from "vitest";

vi.mock("three", () => {
  class Color {
    hex: number;
    constructor(hex = 0) { this.hex = hex; }
    setHex(h: number): this { this.hex = h; return this; }
    getHex(): number { return this.hex; }
  }
  class Material {
    color: Color; opacity = 1; dashSize?: number; gapSize?: number;
    constructor(readonly kind: string, p: Record<string, unknown> = {}) {
      this.color = new Color((p.color as number) ?? 0);
      if (p.opacity !== undefined) this.opacity = p.opacity as number;
      if (p.dashSize !== undefined) this.dashSize = p.dashSize as number;
      if (p.gapSize !== undefined) this.gapSize = p.gapSize as number;
    }
    dispose(): void {}
  }
  class Object3D {
    children: Object3D[] = [];
    position = { set: (..._a: number[]) => undefined };
    rotation = { x: 0, y: 0 };
    renderOrder = 0;
    add(c: Object3D): void { this.children.push(c); }
    remove(c: Object3D): void { this.children = this.children.filter((x) => x !== c); }
  }
  class BufferGeometry {
    points: unknown[] = [];
    setFromPoints(p: unknown[]): this { this.points = p; return this; }
    translate(): this { return this; }
    dispose(): void {}
  }
  class Line extends Object3D {
    lineDistancesComputed = false;
    constructor(readonly geometry: BufferGeometry, readonly material: Material) { super(); }
    computeLineDistances(): this { this.lineDistancesComputed = true; return this; }
  }
  return {
    Color,
    DoubleSide: 2,
    Scene: class extends Object3D {},
    Group: class extends Object3D {},
    Mesh: class extends Object3D {
      constructor(readonly geometry: BufferGeometry, readonly material: Material) { super(); }
    },
    LineLoop: Line,
    MeshBasicMaterial: class extends Material { constructor(p?: Record<string, unknown>) { super("MeshBasicMaterial", p); } },
    LineBasicMaterial: class extends Material { constructor(p?: Record<string, unknown>) { super("LineBasicMaterial", p); } },
    LineDashedMaterial: class extends Material { constructor(p?: Record<string, unknown>) { super("LineDashedMaterial", p); } },
    BufferGeometry,
    RingGeometry: class extends BufferGeometry {},
    ShapeGeometry: class extends BufferGeometry {},
    BoxGeometry: class extends BufferGeometry {},
    Shape: class { moveTo(): void {} absarc(): void {} lineTo(): void {} },
    Vector3: class { constructor(readonly x = 0, readonly y = 0, readonly z = 0) {} },
  };
});

import * as THREE from "three";
import { TelegraphPool } from "../../../client/src/render/telegraphs.js";
import { TELEGRAPH_STYLES } from "../../../client/src/render/telegraphFill.js";
import type { ClassedTelegraphSnapshot, TelegraphClass } from "../../../client/src/sim-host/telegraph-map.js";

interface FakeMat { kind: string; color: { getHex(): number }; dashSize?: number; gapSize?: number }
interface FakeObj { children: FakeObj[]; material?: FakeMat; lineDistancesComputed?: boolean }

function snap(id: string, telegraphClass: TelegraphClass, shape: "circle" | "cone" = "circle"): ClassedTelegraphSnapshot {
  return { id, shape, center: { x: 0, z: 0 }, radius: 3000, angle1024: 200, yaw: 0, resolvesTick: 100, danger: telegraphClass !== "stack", telegraphClass };
}

function render(classes: TelegraphClass[]): FakeObj[] {
  const scene = new THREE.Scene();
  const pool = new TelegraphPool(scene);
  pool.sync(classes.map((c, i) => snap(String(i), c, c === "boss" ? "cone" : "circle")), 50);
  return (scene as unknown as FakeObj).children;
}

function edgeLines(mesh: FakeObj): FakeObj[] {
  return mesh.children.flatMap((c) => (c.material ? [c] : c.children));
}

describe("TelegraphPool: each class renders a distinct colour and edge", () => {
  const classes: TelegraphClass[] = ["avoid", "spread", "stack", "boss"];
  const meshes = render(classes);

  it("builds one decal per telegraph", () => {
    expect(meshes).toHaveLength(4);
  });

  it("fills each decal with its class colour (not a danger-only red/yellow)", () => {
    expect(meshes.map((m) => m.material!.color.getHex())).toEqual(classes.map((c) => TELEGRAPH_STYLES[c].color));
  });

  it("outlines each decal with its class edge pattern", () => {
    const [avoid, spread, stack, boss] = meshes.map(edgeLines);
    expect(avoid!.map((l) => l.material!.kind)).toEqual(["LineBasicMaterial"]);
    expect(boss!.map((l) => l.material!.kind)).toEqual(["LineBasicMaterial", "LineBasicMaterial"]);
    expect(spread!.map((l) => l.material!.kind)).toEqual(["LineDashedMaterial"]);
    expect(stack!.map((l) => l.material!.kind)).toEqual(["LineDashedMaterial"]);
    expect(spread![0]!.material!.dashSize).toBeGreaterThan(stack![0]!.material!.dashSize!);
    for (const l of [...spread!, ...stack!]) expect(l.lineDistancesComputed).toBe(true);
  });

  it("edge lines carry the class colour too", () => {
    meshes.forEach((m, i) => {
      expect(edgeLines(m).length).toBeGreaterThan(0);
      for (const l of edgeLines(m)) expect(l.material!.color.getHex()).toBe(TELEGRAPH_STYLES[classes[i]!].color);
    });
  });

  it("a snapshot without a class (server wire path) renders as avoid", () => {
    const scene = new THREE.Scene();
    const { telegraphClass: _drop, ...plain } = snap("x", "spread");
    new TelegraphPool(scene).sync([plain], 50);
    const mesh = (scene as unknown as FakeObj).children[0]!;
    expect(mesh.material!.color.getHex()).toBe(TELEGRAPH_STYLES.avoid.color);
  });
});
