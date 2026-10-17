/**
 * tests/client/render/window-ring.test.ts
 *
 * Drives the real render/window-ring.ts (and its one-line wiring in render/actors.ts's ActorPool)
 * against a minimal recording fake of "three" (three ships from cdnjs, not node_modules). Assertions
 * are on what the pool actually built and put in the scene: one ring per enemy carrying an open
 * Window (snapshot ActorSnapshot.window), its arc length = remaining fraction, its colour shifting as
 * the Window closes, and its removal ONLY when the snapshot Window closes (null, or tick >= endsTick).
 * A kick does not close a Window in the sim (only a Payoff does, design/BIBLE.md §4 and
 * sim/combat/payoff.ts), so a kick landing during the Window must leave the ring up.
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
    color: Color; opacity = 1;
    constructor(p: Record<string, unknown> = {}) {
      this.color = new Color((p.color as number) ?? 0);
      if (p.opacity !== undefined) this.opacity = p.opacity as number;
    }
    dispose(): void {}
  }
  class Vec { x = 0; y = 0; z = 0; set(x: number, y: number, z: number): this { this.x = x; this.y = y; this.z = z; return this; } clone(): Vec { return new Vec().set(this.x, this.y, this.z); } }
  class Object3D {
    children: Object3D[] = [];
    position = new Vec();
    rotation = { x: 0, y: 0, z: 0 };
    visible = true;
    renderOrder = 0;
    add(c: Object3D): void { this.children.push(c); }
    remove(c: Object3D): void { this.children = this.children.filter((x) => x !== c); }
  }
  class Geometry { disposed = false; dispose(): void { this.disposed = true; } }
  class RingGeometry extends Geometry {
    constructor(readonly innerRadius: number, readonly outerRadius: number, readonly thetaSegments: number,
      readonly phiSegments: number, readonly thetaStart: number, readonly thetaLength: number) { super(); }
  }
  class Mesh extends Object3D {
    constructor(public geometry: Geometry, public material: Material) { super(); }
  }
  return {
    Color, Object3D, Group: Object3D, Scene: Object3D, Mesh, RingGeometry, CapsuleGeometry: Geometry,
    MeshBasicMaterial: Material, MeshStandardMaterial: Material, DoubleSide: 2,
  };
});

vi.mock("../../../client/src/render/canvasSprite.js", () => ({
  createCanvasSprite: () => ({
    canvas: { width: 128, height: 16 },
    ctx: { clearRect: () => undefined, fillRect: () => undefined, fillStyle: "" },
    sprite: { position: { set: () => undefined }, visible: true },
    sync: () => undefined,
  }),
}));

import * as THREE from "three";
import {
  WINDOW_RING_CLOSING_COLOR, WINDOW_RING_OPEN_COLOR, WindowRingPool, windowRingColor, windowRingModel,
} from "../../../client/src/render/window-ring.js";
import { ActorPool } from "../../../client/src/render/actors.js";
import type { ActorSnapshot, ActorWindow, SnapshotEvent } from "../../../client/src/sim-host/snapshot.js";
import type { InterpolatedActor } from "../../../client/src/sim-host/interpolate.js";

interface FakeMesh { geometry: { thetaLength: number; disposed: boolean }; material: { color: { getHex(): number } }; position: { x: number; z: number } }

const WIN: ActorWindow = { windowId: "sundered", setupClass: "warden", endsTick: 160, durationTicks: 60, lock: false };

function actor(id: number, kind: "enemy" | "player", window: ActorWindow | null): ActorSnapshot {
  return {
    id, kind, role: kind === "enemy" ? "lieutenant" : "tank", classId: "x", pos: { x: 2000, y: 0, z: -3000 }, yaw: 0,
    health: 10, maxHealth: 10, resource: 0, maxResource: 1, alive: true, targetId: null, cast: null, cooldowns: {}, window,
  };
}

function frame(actors: ActorSnapshot[]): [InterpolatedActor[], Map<number, ActorSnapshot>] {
  return [actors as unknown as InterpolatedActor[], new Map(actors.map((a) => [a.id, a]))];
}

const kick = (actorId: number, tick: number): SnapshotEvent => ({ type: "interrupt", tick, actorId, sourceId: 1, school: "physical" });
const rings = (scene: THREE.Scene) => (scene as unknown as { children: FakeMesh[] }).children.filter((c) => c.geometry && "thetaLength" in c.geometry);

describe("windowRingModel", () => {
  it("is hidden with no Window and once it closes (tick >= endsTick)", () => {
    expect(windowRingModel(null, 120).visible).toBe(false);
    expect(windowRingModel(undefined, 120).visible).toBe(false);
    expect(windowRingModel(WIN, 160).visible).toBe(false);
    expect(windowRingModel(WIN, 161).visible).toBe(false);
  });

  it("shows the remaining fraction while open", () => {
    expect(windowRingModel(WIN, 100)).toMatchObject({ visible: true, fraction: 1 });
    expect(windowRingModel(WIN, 130).fraction).toBeCloseTo(0.5);
    expect(windowRingModel(WIN, 159)).toMatchObject({ visible: true });
  });

  it("shifts colour from the open colour toward the closing colour", () => {
    expect(windowRingColor(1)).toBe(WINDOW_RING_OPEN_COLOR);
    expect(windowRingColor(0)).toBe(WINDOW_RING_CLOSING_COLOR);
    expect(windowRingColor(0.5)).not.toBe(WINDOW_RING_OPEN_COLOR);
    expect(windowRingColor(0.5)).not.toBe(WINDOW_RING_CLOSING_COLOR);
  });
});

describe("WindowRingPool", () => {
  it("creates one ring on Window open at the enemy, sized to the remaining fraction", () => {
    const scene = new THREE.Scene();
    const pool = new WindowRingPool(scene);
    pool.sync(...frame([actor(102, "enemy", WIN), actor(1, "player", null)]), 115);
    const r = rings(scene);
    expect(r).toHaveLength(1);
    expect(r[0]!.geometry.thetaLength).toBeCloseTo(2 * Math.PI * 0.75, 1);
    expect(r[0]!.position.x).toBeCloseTo(2);
    expect(r[0]!.position.z).toBeCloseTo(-3);
  });

  it("shrinks and recolours the ring on each later snapshot", () => {
    const scene = new THREE.Scene();
    const pool = new WindowRingPool(scene);
    pool.sync(...frame([actor(102, "enemy", WIN)]), 101);
    const first = rings(scene)[0]!;
    const firstColor = first.material.color.getHex();
    const firstGeometry = first.geometry;
    pool.sync(...frame([actor(102, "enemy", WIN)]), 150);
    const later = rings(scene)[0]!;
    expect(later.geometry.thetaLength).toBeLessThan(firstGeometry.thetaLength);
    expect(later.material.color.getHex()).not.toBe(firstColor);
    expect(firstGeometry.disposed).toBe(true);
  });

  it("removes the ring when the Window closes (snapshot window -> null)", () => {
    const scene = new THREE.Scene();
    const pool = new WindowRingPool(scene);
    pool.sync(...frame([actor(102, "enemy", WIN)]), 110);
    expect(rings(scene)).toHaveLength(1);
    pool.sync(...frame([actor(102, "enemy", null)]), 111);
    expect(rings(scene)).toHaveLength(0);
  });

  it("keeps the ring up when a kick lands during the open Window (a kick does not close it)", () => {
    const scene = new THREE.Scene();
    const pool = new WindowRingPool(scene);
    pool.sync(...frame([actor(102, "enemy", WIN)]), 110);
    pool.sync(...frame([actor(102, "enemy", WIN)]), 112, [kick(102, 111)]);
    expect(rings(scene)).toHaveLength(1);
    pool.sync(...frame([actor(102, "enemy", WIN)]), 113);
    expect(rings(scene)).toHaveLength(1);
  });

  it("removes the ring when a Payoff consumes the Window (snapshot window -> null mid-Window)", () => {
    const scene = new THREE.Scene();
    const pool = new WindowRingPool(scene);
    pool.sync(...frame([actor(102, "enemy", WIN)]), 120);
    expect(rings(scene)).toHaveLength(1);
    pool.sync(...frame([actor(102, "enemy", null)]), 121);
    expect(rings(scene)).toHaveLength(0);
  });

  it("removes the ring when the Window expires even if the snapshot still carries it", () => {
    const scene = new THREE.Scene();
    const pool = new WindowRingPool(scene);
    pool.sync(...frame([actor(102, "enemy", WIN)]), 159);
    expect(rings(scene)).toHaveLength(1);
    pool.sync(...frame([actor(102, "enemy", WIN)]), 160);
    expect(rings(scene)).toHaveLength(0);
  });

  it("draws no ring for an enemy without a Window or for a player carrying one", () => {
    const scene = new THREE.Scene();
    const pool = new WindowRingPool(scene);
    pool.sync(...frame([actor(102, "enemy", null), actor(1, "player", WIN)]), 110);
    expect(rings(scene)).toHaveLength(0);
  });
});

describe("ActorPool wiring", () => {
  it("draws the Window ring from the snapshot, keeps it through a drained kick, drops it on close", () => {
    const scene = new THREE.Scene();
    const pool = new ActorPool(scene);
    pool.sync(...frame([actor(102, "enemy", WIN)]), 110);
    expect(rings(scene)).toHaveLength(1);
    pool.sync(...frame([actor(102, "enemy", WIN)]), 112, [kick(102, 111)]);
    expect(rings(scene)).toHaveLength(1);
    pool.sync(...frame([actor(102, "enemy", null)]), 113);
    expect(rings(scene)).toHaveLength(0);
  });
});
