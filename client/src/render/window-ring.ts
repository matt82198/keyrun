/**
 * client/src/render/window-ring.ts
 *
 * The 3D Window ring (design/BIBLE.md §4, D10 readability): a flat placeholder ring on the floor
 * around every enemy carrying an open Window. Its arc is the remaining fraction of the Window
 * (full circle when it opens, sweeping down to nothing as it closes) and its colour shifts from
 * WINDOW_RING_OPEN_COLOR to WINDOW_RING_CLOSING_COLOR. It disappears ONLY when the Window closes:
 * snapshot window -> null (a Payoff consumed it -- sim/combat/payoff.ts removes the effect, so
 * toWindow projects null -- or it expired), or tick >= endsTick. A kick does NOT close a Window in
 * the sim, so a kick never dismisses the ring (that would hide a Window a Payoff can still close).
 *
 * A constant-size label sprite rides just above the ring naming whose setup it is
 * (windowRingLabel: "Warden Window: Sundered"), so a player can tell which class can close it;
 * render/payoff-ready.ts separately lights YOUR payoff key on the ability bar.
 *
 * Driven ONLY from snapshot state (ActorSnapshot.window, projected by sim-host/snapshot-codec.ts's
 * toWindow) -- never from sim internals and never from one-shot events.
 *
 * Usage: `const rings = new WindowRingPool(scene); rings.sync(interpolated, rawById, tick, events);`
 * (render/actors.ts's ActorPool.sync makes that call once per frame).
 */
import * as THREE from "three";
import type { InterpolatedActor } from "../sim-host/interpolate.js";
import type { ActorSnapshot, ActorWindow, SnapshotEvent } from "../sim-host/snapshot.js";
import { humanizeId } from "../sim-host/display-info.js";
import { createCanvasSprite, type CanvasSprite } from "./canvasSprite.js";

const MM_PER_M = 1000;
/** Placeholder ring size (metres), sized to sit just outside the 0.4 m actor capsule. */
export const WINDOW_RING_INNER_M = 0.95;
export const WINDOW_RING_OUTER_M = 1.2;
/** Arc is quantised to this many steps so geometry is only rebuilt when it visibly changes. */
export const WINDOW_RING_STEPS = 60;
export const WINDOW_RING_OPEN_COLOR = 0x3ddc97; // fresh Window: green
const WINDOW_RING_MID_COLOR = 0xffd166; // half-way: amber
export const WINDOW_RING_CLOSING_COLOR = 0xef476f; // about to close: red
const RING_LIFT_M = 0.03;
/** Label canvas px and its constant on-screen size (same px-per-world ratio as render/name-tag.ts). */
const LABEL_PX: [number, number] = [512, 64];
const LABEL_LIFT_M = 0.3;

/** The ring's text (D10 window readability): whose setup it is and which Window -- e.g.
 * "Warden Window: Sundered"; the Lock (BIBLE.md sec 9, any class but the last closer) reads as such. */
export function windowRingLabel(win: Pick<ActorWindow, "windowId" | "setupClass" | "lock">): string {
  if (win.lock) return "LOCK: alternate classes";
  return `${humanizeId(win.setupClass)} Window: ${humanizeId(win.windowId)}`;
}

function drawLabel(label: CanvasSprite, text: string, lock: boolean): void {
  const { ctx, canvas } = label;
  ctx.clearRect(0, 0, canvas.width, canvas.height);
  ctx.font = "bold 40px sans-serif";
  ctx.textAlign = "center";
  ctx.textBaseline = "middle";
  ctx.lineWidth = 8;
  ctx.strokeStyle = "rgba(0,0,0,0.9)";
  ctx.fillStyle = lock ? "#ffd166" : "#3ddc97";
  ctx.strokeText(text, LABEL_PX[0] / 2, LABEL_PX[1] / 2);
  ctx.fillText(text, LABEL_PX[0] / 2, LABEL_PX[1] / 2);
  label.sync();
}

export interface WindowRingModel {
  visible: boolean;
  /** Remaining fraction of the Window, 1 at open, toward 0 at close. */
  fraction: number;
  color: number;
}

const HIDDEN: WindowRingModel = { visible: false, fraction: 0, color: WINDOW_RING_CLOSING_COLOR };

function lerpHex(a: number, b: number, t: number): number {
  const ch = (shift: number) => {
    const ca = (a >> shift) & 0xff;
    const cb = (b >> shift) & 0xff;
    return Math.round(ca + (cb - ca) * t) << shift;
  };
  return ch(16) | ch(8) | ch(0);
}

/** Open colour at fraction 1, amber at 0.5, closing colour at 0. */
export function windowRingColor(fraction: number): number {
  const f = Math.min(1, Math.max(0, fraction));
  return f >= 0.5
    ? lerpHex(WINDOW_RING_MID_COLOR, WINDOW_RING_OPEN_COLOR, (f - 0.5) * 2)
    : lerpHex(WINDOW_RING_CLOSING_COLOR, WINDOW_RING_MID_COLOR, f * 2);
}

/** Pure model: visible exactly while the snapshot Window is present and tick < endsTick. */
export function windowRingModel(win: ActorWindow | null | undefined, currentTick: number): WindowRingModel {
  if (!win || currentTick >= win.endsTick) return HIDDEN;
  const span = Math.max(1, win.durationTicks);
  const fraction = Math.min(1, Math.max(0, (win.endsTick - currentTick) / span));
  return { visible: true, fraction, color: windowRingColor(fraction) };
}

interface RingView {
  mesh: THREE.Mesh;
  material: THREE.MeshBasicMaterial;
  step: number;
  label: CanvasSprite;
  labelText: string;
}

function ringGeometry(step: number): THREE.RingGeometry {
  const theta = (2 * Math.PI * step) / WINDOW_RING_STEPS;
  return new THREE.RingGeometry(WINDOW_RING_INNER_M, WINDOW_RING_OUTER_M, 48, 1, Math.PI / 2, theta);
}

export class WindowRingPool {
  private readonly rings = new Map<number, RingView>();
  constructor(private readonly scene: THREE.Scene) {}

  /** `_events` is accepted for ActorPool.sync's call shape but deliberately unused: no current
   * snapshot event closes a Window (the stream carries only interrupts, which do not). */
  sync(interpolated: InterpolatedActor[], rawById: Map<number, ActorSnapshot>, currentTick: number, _events?: readonly SnapshotEvent[]): void {
    const seen = new Set<number>();
    for (const actor of interpolated) {
      const raw = rawById.get(actor.id);
      if (!raw || raw.kind !== "enemy" || !actor.alive) continue;
      const model = windowRingModel(raw.window, currentTick);
      if (!model.visible) continue;
      seen.add(actor.id);
      const view = this.ensure(actor.id, Math.max(1, Math.ceil(model.fraction * WINDOW_RING_STEPS)));
      view.material.color.setHex(model.color);
      view.mesh.position.set(actor.pos.x / MM_PER_M, actor.pos.y / MM_PER_M + RING_LIFT_M, actor.pos.z / MM_PER_M);
      const text = windowRingLabel(raw.window!);
      if (view.labelText !== text) {
        drawLabel(view.label, text, raw.window!.lock);
        view.labelText = text;
      }
      view.label.sprite.position.set(actor.pos.x / MM_PER_M, actor.pos.y / MM_PER_M + LABEL_LIFT_M, actor.pos.z / MM_PER_M);
    }
    for (const [id, view] of this.rings) {
      if (seen.has(id)) continue;
      this.scene.remove(view.mesh);
      this.scene.remove(view.label.sprite);
      view.mesh.geometry.dispose();
      view.material.dispose();
      view.label.sprite.material.map?.dispose();
      view.label.sprite.material.dispose();
      this.rings.delete(id);
    }
  }

  private ensure(id: number, step: number): RingView {
    let view = this.rings.get(id);
    if (!view) {
      const material = new THREE.MeshBasicMaterial({ color: WINDOW_RING_OPEN_COLOR, transparent: true, opacity: 0.85, side: THREE.DoubleSide, depthWrite: false });
      const mesh = new THREE.Mesh(ringGeometry(step), material);
      mesh.rotation.x = -Math.PI / 2;
      mesh.renderOrder = 2;
      this.scene.add(mesh);
      const label = createCanvasSprite(LABEL_PX[0], LABEL_PX[1], 0.29, 0.036, false);
      this.scene.add(label.sprite);
      view = { mesh, material, step, label, labelText: "" };
      this.rings.set(id, view);
    } else if (view.step !== step) {
      view.mesh.geometry.dispose();
      view.mesh.geometry = ringGeometry(step);
      view.step = step;
    }
    return view;
  }
}
