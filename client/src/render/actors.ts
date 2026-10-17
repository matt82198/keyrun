// Actor capsules + billboard health/resource/cast bars, kept in sync with
// interpolated transforms each render frame.
import * as THREE from "three";
import type { InterpolatedActor } from "../sim-host/interpolate.js";
import type { ActorSnapshot, SnapshotEvent } from "../sim-host/snapshot.js";
import { castBarModel, InterruptLatch } from "./castBar.js";
import { colorForActor } from "./roleColor.js";
import { createCanvasSprite, type CanvasSprite } from "./canvasSprite.js";
import { WindowRingPool } from "./window-ring.js";

const MM_PER_M = 1000;
const CAPSULE_RADIUS_M = 0.4;
const CAPSULE_LENGTH_M = 1.0; // cylindrical section; total height ~= length + 2*radius

function drawBar(bar: CanvasSprite, fraction: number, fillColor: string): void {
  const { ctx, canvas } = bar;
  ctx.clearRect(0, 0, canvas.width, canvas.height);
  ctx.fillStyle = "rgba(10,10,14,0.75)";
  ctx.fillRect(0, 0, canvas.width, canvas.height);
  const clamped = Math.min(1, Math.max(0, fraction));
  ctx.fillStyle = fillColor;
  ctx.fillRect(1, 1, Math.max(0, clamped * (canvas.width - 2)), canvas.height - 2);
  bar.sync();
}

interface ActorView {
  group: THREE.Group;
  capsule: THREE.Mesh;
  material: THREE.MeshStandardMaterial;
  healthBar: CanvasSprite;
  resourceBar: CanvasSprite;
  castBar: CanvasSprite;
}

function buildActorView(): ActorView {
  const group = new THREE.Group();

  const geometry = new THREE.CapsuleGeometry(CAPSULE_RADIUS_M, CAPSULE_LENGTH_M, 4, 8);
  const material = new THREE.MeshStandardMaterial({ color: 0xffffff, roughness: 0.6 });
  const capsule = new THREE.Mesh(geometry, material);
  capsule.position.y = CAPSULE_LENGTH_M / 2 + CAPSULE_RADIUS_M;
  group.add(capsule);

  const barHeadY = CAPSULE_LENGTH_M + CAPSULE_RADIUS_M * 2 + 0.5;
  const healthBar = createCanvasSprite(128, 16, 0.85, 0.1);
  healthBar.sprite.position.set(0, barHeadY, 0);
  group.add(healthBar.sprite);

  const resourceBar = createCanvasSprite(128, 16, 0.85, 0.08);
  resourceBar.sprite.position.set(0, barHeadY - 0.14, 0);
  group.add(resourceBar.sprite);

  const castBar = createCanvasSprite(128, 16, 0.85, 0.08);
  castBar.sprite.position.set(0, barHeadY - 0.28, 0);
  castBar.sprite.visible = false;
  group.add(castBar.sprite);

  return { group, capsule, material, healthBar, resourceBar, castBar };
}

export class ActorPool {
  private views = new Map<number, ActorView>();
  private readonly kicks = new InterruptLatch();
  private readonly windowRings: WindowRingPool;

  constructor(private readonly scene: THREE.Scene) {
    this.windowRings = new WindowRingPool(scene);
  }

  /**
   * Syncs one actor pool entry per id present in `interpolated`, pulling
   * static fields (kind/role) from `rawById` since those aren't part of the
   * interpolated transform. Entries no longer present are removed. `events` is the current
   * snapshot's one-shot Snapshot.events; landed kicks are latched so the cast bar flashes for
   * castBar.ts's INTERRUPT_FLASH_TICKS (passing the same snapshot's events every frame is safe).
   */
  sync(
    interpolated: InterpolatedActor[],
    rawById: Map<number, ActorSnapshot>,
    currentTick: number,
    events?: readonly SnapshotEvent[],
  ): void {
    this.kicks.ingest(events);
    this.windowRings.sync(interpolated, rawById, currentTick, events);
    const seen = new Set<number>();
    for (const actor of interpolated) {
      seen.add(actor.id);
      const raw = rawById.get(actor.id);
      let view = this.views.get(actor.id);
      if (!view) {
        view = buildActorView();
        this.scene.add(view.group);
        this.views.set(actor.id, view);
      }

      view.group.position.set(actor.pos.x / MM_PER_M, actor.pos.y / MM_PER_M, actor.pos.z / MM_PER_M);
      view.group.rotation.y = -(actor.yaw / 1024) * Math.PI * 2;
      view.group.visible = actor.alive;

      if (raw) {
        view.material.color.setHex(colorForActor(raw.kind, raw.role, raw.classId));
      }

      drawBar(view.healthBar, actor.health / Math.max(1, actor.maxHealth), "#e63946");
      drawBar(view.resourceBar, actor.resource / Math.max(1, actor.maxResource), "#4ea8de");

      const bar = castBarModel(actor.cast, currentTick, this.kicks.interruptedAt(actor.id));
      view.castBar.sprite.visible = bar.visible;
      if (bar.visible) drawBar(view.castBar, bar.fraction, bar.fill);
    }

    for (const [id, view] of this.views) {
      if (!seen.has(id)) {
        this.scene.remove(view.group);
        this.views.delete(id);
      }
    }
  }

  worldPositionOf(id: number): THREE.Vector3 | null {
    const view = this.views.get(id);
    return view ? view.group.position.clone() : null;
  }
}
