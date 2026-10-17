// Actor capsules + billboard health/resource/cast bars + D10 dressing (render/actor-label.ts: name
// tag with "casting X", per-role silhouette scale, facing nose, white ground ring under YOU, a hit
// flash on every actor a cast event hit), kept in sync with interpolated transforms each frame.
// The per-actor scene graph (and its test-load-bearing child order) lives in render/actor-view.ts.
import * as THREE from "three";
import type { InterpolatedActor } from "../sim-host/interpolate.js";
import type { ActorSnapshot, SnapshotEvent } from "../sim-host/snapshot.js";
import type { DisplayInfo } from "../sim-host/display-info.js";
import { castBarModel, InterruptLatch } from "./castBar.js";
import { colorForActor } from "./roleColor.js";
import { WindowRingPool } from "./window-ring.js";
import { actorLabel } from "./actor-label.js";
import { drawNameTag } from "./name-tag.js";
import { buildActorView, drawBar, placeOverhead, CAPSULE_LENGTH_M, CAPSULE_RADIUS_M, type ActorView } from "./actor-view.js";

const MM_PER_M = 1000;
/** Hit flash: sim ticks a struck actor stays lit (0.2 s at 20 Hz). */
export const HIT_FLASH_TICKS = 4;
export const HIT_FLASH_HOSTILE = 0xffffff;
export const HIT_FLASH_FRIENDLY = 0x7dffb0;

export class ActorPool {
  private views = new Map<number, ActorView>();
  private readonly kicks = new InterruptLatch();
  private readonly windowRings: WindowRingPool;
  private readonly flashes = new Map<number, { untilTick: number; color: number }>();
  private humanActorId: number | null = null;
  private display: DisplayInfo | undefined;
  private readonly titles = new Map<number, string>();

  constructor(private readonly scene: THREE.Scene) {
    this.windowRings = new WindowRingPool(scene);
  }

  /** Which actor is YOU (white ring, "YOU:" tag) and the host's names; called once after whenReady(). */
  configure(humanActorId: number, display: DisplayInfo | undefined): void {
    this.humanActorId = humanActorId;
    this.display = display;
  }

  /** The current hit-flash colour of `id` at `tick`, or null (exposed for tests and the e2e). */
  flashOf(id: number, tick: number): number | null {
    const f = this.flashes.get(id);
    return f && tick <= f.untilTick ? f.color : null;
  }

  private ingestHits(events: readonly SnapshotEvent[] | undefined, rawById: Map<number, ActorSnapshot>): void {
    for (const e of events ?? []) {
      if (e.type !== "cast") continue;
      const casterKind = rawById.get(e.actorId)?.kind;
      for (const id of e.hitIds) {
        const friendly = casterKind !== undefined && rawById.get(id)?.kind === casterKind;
        this.flashes.set(id, { untilTick: e.tick + HIT_FLASH_TICKS, color: friendly ? HIT_FLASH_FRIENDLY : HIT_FLASH_HOSTILE });
      }
    }
  }

  /**
   * Syncs one actor pool entry per id present in `interpolated`, pulling
   * static fields (kind/role) from `rawById` since those aren't part of the
   * interpolated transform. Entries no longer present are removed. `events` is the current
   * snapshot's one-shot Snapshot.events; landed kicks are latched so the cast bar flashes for
   * castBar.ts's INTERRUPT_FLASH_TICKS, and cast events' hitIds light the struck actors.
   */
  sync(
    interpolated: InterpolatedActor[],
    rawById: Map<number, ActorSnapshot>,
    currentTick: number,
    events?: readonly SnapshotEvent[],
  ): void {
    this.kicks.ingest(events);
    this.ingestHits(events, rawById);
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
        const isSelf = raw.id === this.humanActorId;
        const flash = this.flashOf(actor.id, currentTick);
        view.material.color.setHex(flash ?? colorForActor(raw.kind, raw.role, raw.classId));
        const emissive = (view.material as { emissive?: THREE.Color }).emissive;
        emissive?.setHex(isSelf ? 0x1a2a44 : 0x000000);
        const label = actorLabel({ ...raw, cast: actor.cast }, isSelf, this.display);
        view.capsule.scale.set(label.scale[0], label.scale[1], label.scale[0]);
        view.capsule.position.y = (CAPSULE_LENGTH_M / 2 + CAPSULE_RADIUS_M) * label.scale[1];
        (view.nose.material as THREE.MeshBasicMaterial).color.setHex(raw.kind === "enemy" ? 0xff3030 : 0xffffff);
        view.selfRing.visible = isSelf;
        placeOverhead(view, label.scale[1]);
        drawNameTag(view.nameTag, label);
        this.titles.set(actor.id, label.title);
        drawBar(view.healthBar, actor.health / Math.max(1, actor.maxHealth), raw.kind === "enemy" ? "#e63946" : "#4cd964");
      }

      drawBar(view.resourceBar, actor.resource / Math.max(1, actor.maxResource), "#4ea8de");

      const bar = castBarModel(actor.cast, currentTick, this.kicks.interruptedAt(actor.id));
      view.castBar.sprite.visible = bar.visible;
      if (bar.visible) drawBar(view.castBar, bar.fraction, bar.fill);
    }

    for (const [id, view] of this.views) {
      if (!seen.has(id)) {
        this.scene.remove(view.group);
        this.views.delete(id);
        this.titles.delete(id);
      }
    }
  }

  /** Name-tag title per live actor id, as last drawn (read-only view for tests and the e2e hook). */
  tagTitles(): Record<number, string> {
    return Object.fromEntries(this.titles);
  }

  worldPositionOf(id: number): THREE.Vector3 | null {
    const view = this.views.get(id);
    return view ? view.group.position.clone() : null;
  }
}
