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
  readonly fills: string[] = [];
  clearRect(): void {
    this.fills.length = 0;
  }
  fillRect(): void {
    this.fills.push(this.fillStyle);
  }
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

export class CanvasTexture {
  needsUpdate = false;
  minFilter = 0;
  constructor(readonly image: FakeCanvas) {}
}

export class Material {
  color: Color;
  map: CanvasTexture | undefined;
  constructor(p: Record<string, unknown> = {}) {
    this.color = new Color((p.color as number) ?? 0);
    this.map = p.map as CanvasTexture | undefined;
  }
  dispose(): void {}
}
export class MeshStandardMaterial extends Material {}
export class SpriteMaterial extends Material {}

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

export const LinearFilter = 1006;
