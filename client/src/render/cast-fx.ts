/**
 * client/src/render/cast-fx.ts
 *
 * D10 projections (Matt: "no ability projection"): every completed cast (snapshot.ts's one-shot
 * CastSnapshotEvent, drained through render/event-latch.ts) draws its real shape on the ground for
 * FX_SECONDS, fading out: circle = disc around the caster, cone = sector along the caster's facing,
 * line = strip along facing (half-width = radius), target = a beam from caster to target with a ring
 * on the target, self = a ring around the caster. Colour = who cast it (YOU cyan, bots their role
 * colour, enemies red, boss purple). The human's and every enemy's cast also floats its ability
 * NAME over the caster, so "what is that enemy doing" is readable. Hit flashes on struck actors are
 * render/actors.ts's job (same events). Charge (warden.charge) is a "line" cast, so its strip IS the
 * Charge line. Item 13: a Space dash (render/dash-trail.ts, from snapshot positions) leaves a short
 * "dash" strip from take-off to landing in the dasher's colour.
 *
 * Geometry uses render/ground-frame.ts's frame: meshes flattened inside a Group turned by the sim's
 * combat facing (sim/combat/targeting.ts faceDir, yaw 0 = +x).
 *
 * Usage: `fx.spawn(events, rawById, posOf); fx.update(dtSeconds);` once per render frame.
 */
import * as THREE from "three";
import type { ActorSnapshot, CastSnapshotEvent, SnapshotEvent } from "../sim-host/snapshot.js";
import { abilityName, type DisplayInfo } from "../sim-host/display-info.js";
import { colorForActor } from "./roleColor.js";
import { createCanvasSprite } from "./canvasSprite.js";
import { combatYawToThree } from "./ground-frame.js";
import { dashGeometry, dashYaw, detectDashes, DASH_TRAIL_SECONDS } from "./dash-trail.js";

export const FX_SECONDS = 0.7;
const LABEL_SECONDS = 1.2;
const MM_PER_M = 1000;
export const SELF_FX_COLOR = 0x6bc6ff;

interface Fx {
  root: THREE.Object3D;
  materials: THREE.Material[];
  age: number;
  life: number;
  peak: number;
  shape: string;
  rise: boolean;
}

export type PosOf = (id: number) => { x: number; z: number } | null;

function flat(geometry: THREE.BufferGeometry, material: THREE.MeshBasicMaterial): THREE.Mesh {
  const mesh = new THREE.Mesh(geometry, material);
  mesh.rotation.x = -Math.PI / 2;
  mesh.position.y = 0.04;
  mesh.renderOrder = 3;
  return mesh;
}

function shapeGeometries(e: CastSnapshotEvent, posOf: PosOf): { geos: THREE.BufferGeometry[]; yaw: number } {
  const r = Math.max(0.3, (e.radius ?? 0) / MM_PER_M);
  switch (e.shape) {
    case "circle":
      return { geos: [new THREE.CircleGeometry(r, 48)], yaw: 0 };
    case "cone": {
      const a = ((e.angle1024 ?? 0) / 1024) * Math.PI * 2;
      return { geos: [new THREE.CircleGeometry(r, 32, -a / 2, a)], yaw: combatYawToThree(e.yaw) };
    }
    case "line": {
      const len = Math.max(0.5, (e.length ?? 0) / MM_PER_M);
      return { geos: [new THREE.PlaneGeometry(len, r * 2).translate(len / 2, 0, 0)], yaw: combatYawToThree(e.yaw) };
    }
    case "target": {
      const t = e.targetId !== null ? posOf(e.targetId) : null;
      if (!t) return { geos: [], yaw: 0 };
      const dx = t.x - e.origin.x / MM_PER_M;
      const dz = t.z - e.origin.z / MM_PER_M;
      const dist = Math.max(0.2, Math.hypot(dx, dz));
      const beam = new THREE.PlaneGeometry(dist, 0.22).translate(dist / 2, 0, 0);
      const ring = new THREE.RingGeometry(0.55, 0.8, 32).translate(dist, 0, 0);
      return { geos: [beam, ring], yaw: Math.atan2(-dz, dx) };
    }
    default:
      return { geos: [new THREE.RingGeometry(0.85, 1.15, 40)], yaw: 0 };
  }
}

export class CastFxPool {
  private active: Fx[] = [];
  private spawned = 0;
  private selfShapes: string[] = [];
  private readonly lastPos = new Map<number, { x: number; z: number }>();
  private humanActorId: number | null = null;
  private display: DisplayInfo | undefined;

  constructor(private readonly scene: THREE.Scene) {}

  configure(humanActorId: number, display: DisplayInfo | undefined): void {
    this.humanActorId = humanActorId;
    this.display = display;
  }

  /** Live projections, by shape (read-only view for tests and the e2e hook). */
  activeShapes(): string[] {
    return this.active.filter((f) => !f.rise).map((f) => f.shape);
  }

  spawnedCount(): number {
    return this.spawned;
  }

  /** Shapes of YOUR last (up to 20) projections, oldest first. */
  selfProjectionHistory(): string[] {
    return [...this.selfShapes];
  }

  activeLabels(): number {
    return this.active.filter((f) => f.rise).length;
  }

  private colorFor(caster: ActorSnapshot | undefined): number {
    if (!caster) return 0xffffff;
    if (caster.id === this.humanActorId) return SELF_FX_COLOR;
    return colorForActor(caster.kind, caster.role, caster.classId);
  }

  /** One fading ground decal group at `origin` (mm), turned by `yaw`. */
  private ground(origin: { x: number; z: number }, yaw: number, geos: THREE.BufferGeometry[], color: number, shape: string, life: number): void {
    const group = new THREE.Group();
    group.position.set(origin.x / MM_PER_M, 0, origin.z / MM_PER_M);
    group.rotation.y = yaw;
    const materials: THREE.Material[] = [];
    for (const g of geos) {
      const m = new THREE.MeshBasicMaterial({ color, transparent: true, opacity: 0.55, side: THREE.DoubleSide, depthWrite: false });
      materials.push(m);
      group.add(flat(g, m));
    }
    this.scene.add(group);
    this.active.push({ root: group, materials, age: 0, life, peak: 0.55, shape, rise: false });
  }

  spawn(events: readonly SnapshotEvent[] | undefined, rawById: Map<number, ActorSnapshot>, posOf: PosOf): void {
    for (const d of detectDashes(this.lastPos, rawById)) this.ground(d.from, dashYaw(d), [dashGeometry(d)], this.colorFor(rawById.get(d.id)), "dash", DASH_TRAIL_SECONDS);
    for (const e of events ?? []) {
      if (e.type !== "cast") continue;
      const caster = rawById.get(e.actorId);
      const color = this.colorFor(caster);
      const { geos, yaw } = shapeGeometries(e, posOf);
      if (geos.length > 0) {
        this.ground(e.origin, yaw, geos, color, e.shape, FX_SECONDS);
        this.spawned += 1;
        if (e.actorId === this.humanActorId) this.selfShapes = [...this.selfShapes.slice(-19), e.shape];
      }
      if (caster && (caster.id === this.humanActorId || caster.kind === "enemy")) this.label(e, caster, color);
    }
  }

  private label(e: CastSnapshotEvent, caster: ActorSnapshot, color: number): void {
    const s = createCanvasSprite(512, 72, 0.3, 0.042, false); // constant screen size, like name tags
    const { ctx, canvas } = s;
    ctx.clearRect(0, 0, canvas.width, canvas.height);
    ctx.font = "bold 46px sans-serif";
    ctx.textAlign = "center";
    ctx.textBaseline = "middle";
    ctx.lineWidth = 8;
    ctx.strokeStyle = "rgba(0,0,0,0.9)";
    ctx.fillStyle = `#${color.toString(16).padStart(6, "0")}`;
    const text = abilityName(this.display, e.abilityId);
    ctx.strokeText(text, canvas.width / 2, canvas.height / 2);
    ctx.fillText(text, canvas.width / 2, canvas.height / 2);
    s.sync();
    s.sprite.position.set(caster.pos.x / MM_PER_M, 3.4, caster.pos.z / MM_PER_M);
    this.scene.add(s.sprite);
    this.active.push({ root: s.sprite, materials: [s.sprite.material], age: 0, life: LABEL_SECONDS, peak: 1, shape: "label", rise: true });
  }

  update(dtSeconds: number): void {
    for (const f of this.active) {
      f.age += dtSeconds;
      const t = Math.min(1, f.age / f.life);
      for (const m of f.materials) m.opacity = f.peak * (1 - t * t);
      if (f.rise) f.root.position.y += 0.8 * dtSeconds;
    }
    const keep: Fx[] = [];
    for (const f of this.active) {
      if (f.age < f.life) {
        keep.push(f);
        continue;
      }
      this.scene.remove(f.root);
      for (const m of f.materials) m.dispose();
      for (const c of f.root.children) (c as THREE.Mesh).geometry?.dispose();
    }
    this.active = keep;
  }
}
