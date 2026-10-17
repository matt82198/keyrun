/**
 * tests/client/support/fake-three.ts
 *
 * Minimal recording fake of the "three" module surface render/actors.ts + render/canvasSprite.ts
 * construct (three ships from cdnjs at runtime, not node_modules, so vitest cannot load the real
 * one). Records only what the code under test builds: the scene graph, each capsule material's
 * colour, and each sprite's canvas (via its CanvasTexture), so assertions read what the pool
 * actually drew. Also installs a fake `document.createElement("canvas")` whose 2D context logs
 * every fillStyle used by a fillRect since the last clearRect. Not a *.test.ts file -- a helper.
 *
 * Usage: `vi.mock("three", () => import("../support/fake-three.js"));`
 */
export class FakeCanvasContext {
  fillStyle = "";
  strokeStyle = "";
  font = "";
  lineWidth = 1;
  textAlign = "start";
  textBaseline = "alphabetic";
  readonly fills: string[] = [];
  /** Every fillText string since the last clearRect (name tags, cast labels). */
  readonly texts: string[] = [];
  clearRect(): void {
    this.fills.length = 0;
    this.texts.length = 0;
  }
  fillRect(): void {
    this.fills.push(this.fillStyle);
  }
  fillText(text: string): void {
    this.texts.push(text);
  }
  strokeText(): void {}
}

export class FakeCanvas {
  width = 0;
  height = 0;
  readonly ctx = new FakeCanvasContext();
  getContext(): FakeCanvasContext {
    return this.ctx;
  }
}

(globalThis as { document?: unknown }).document ??= { createElement: () => new FakeCanvas() };

export class Color {
  constructor(public hex = 0) {}
  setHex(h: number): this {
    this.hex = h;
    return this;
  }
  getHex(): number {
    return this.hex;
  }
}

export class Vector3 {
  constructor(public x = 0, public y = 0, public z = 0) {}
  set(x: number, y: number, z: number): this {
    this.x = x;
    this.y = y;
    this.z = z;
    return this;
  }
  clone(): Vector3 {
    return new Vector3(this.x, this.y, this.z);
  }
}

export class Object3D {
  children: Object3D[] = [];
  position = new Vector3();
  rotation = { x: 0, y: 0, z: 0 };
  scale = new Vector3(1, 1, 1);
  visible = true;
  renderOrder = 0;
  add(c: Object3D): void {
    this.children.push(c);
  }
  remove(c: Object3D): void {
    this.children = this.children.filter((x) => x !== c);
  }
}

export class Scene extends Object3D {}
export class Group extends Object3D {}

export class BufferGeometry {
  setFromPoints(): this {
    return this;
  }
  translate(): this {
    return this;
  }
  dispose(): void {}
}
export class CapsuleGeometry extends BufferGeometry {}
export class ConeGeometry extends BufferGeometry {}
export class ShapeGeometry extends BufferGeometry {}
export class BoxGeometry extends BufferGeometry {}
export class Shape {
  moveTo(): this {
    return this;
  }
  lineTo(): this {
    return this;
  }
  absarc(): this {
    return this;
  }
}
export class PlaneGeometry extends BufferGeometry {
  constructor(readonly width = 1, readonly height = 1) {
    super();
  }
}
/** render/cast-fx.ts's discs and cone sectors; records the sector so tests can read the shape. */
export class CircleGeometry extends BufferGeometry {
  constructor(readonly radius = 1, readonly segments = 32, readonly thetaStart = 0, readonly thetaLength = Math.PI * 2) {
    super();
  }
}
/** render/window-ring.ts's Window ring arc; records thetaLength so tests can read the fraction. */
export class RingGeometry extends BufferGeometry {
  constructor(readonly innerRadius = 0.5, readonly outerRadius = 1, readonly thetaSegments = 32,
    readonly phiSegments = 1, readonly thetaStart = 0, readonly thetaLength = Math.PI * 2) {
    super();
  }
}
export const DoubleSide = 2;

export class CanvasTexture {
  needsUpdate = false;
  minFilter = 0;
  constructor(readonly image: FakeCanvas) {}
}

export class Material {
  color: Color;
  map: CanvasTexture | undefined;
  opacity: number;
  constructor(p: Record<string, unknown> = {}) {
    this.color = new Color((p.color as number) ?? 0);
    this.map = p.map as CanvasTexture | undefined;
    this.opacity = (p.opacity as number | undefined) ?? 1;
  }
  dispose(): void {}
}
export class MeshStandardMaterial extends Material {}
export class MeshBasicMaterial extends Material {}
export class SpriteMaterial extends Material {}
export class LineBasicMaterial extends Material {}
export class LineDashedMaterial extends Material {}

export class Mesh extends Object3D {
  constructor(readonly geometry: BufferGeometry, readonly material: Material) {
    super();
  }
}

export class Sprite extends Object3D {
  constructor(readonly material: SpriteMaterial) {
    super();
  }
}

export class LineLoop extends Object3D {
  constructor(readonly geometry: BufferGeometry, readonly material: Material) {
    super();
  }
  computeLineDistances(): this {
    return this;
  }
}

export const LinearFilter = 1006;
