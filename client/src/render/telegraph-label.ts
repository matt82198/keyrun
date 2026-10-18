/**
 * client/src/render/telegraph-label.ts
 *
 * D10 readability: a SPREAD or STACK telegraph says what the player should DO, not just show a
 * coloured circle. A constant-screen-size billboard sprite (same approach as render/name-tag.ts and
 * render/window-ring.ts's label) sits at the telegraph's centre reading "SPREAD: move apart" /
 * "STACK: group up" in the class colour, with a seconds countdown and a bar that drains as the
 * telegraph's remaining ticks run out (the inverse of telegraphFill.ts's telegraphFillFraction, so
 * the bar empties exactly as the decal fills). The label fades over the last LABEL_FADE_TICKS and
 * is removed with its telegraph. Only classes listed in TELEGRAPH_CUES get a label: avoid (incl.
 * seed.slam, sim-host/telegraph-map.ts), boss and an unclassed (server wire) telegraph get none.
 *
 * Usage: `new TelegraphPool(scene, new TelegraphLabelPool(scene))` (render/telegraphs.ts calls
 * `labels.sync(telegraphs, tick)` at the end of its own sync).
 */
import type * as THREE from "three";
import type { TelegraphClass } from "../sim-host/telegraph-map.js";
import { TICK_HZ } from "../sim-host/snapshot.js";
import { createCanvasSprite, type CanvasSprite } from "./canvasSprite.js";
import { telegraphFillFraction, telegraphStyle } from "./telegraphFill.js";

const MM_PER_M = 1000;

export interface TelegraphCue {
  title: string;
  cue: string;
}

/** The instruction per class. A class absent here gets no label. */
export const TELEGRAPH_CUES: Readonly<Partial<Record<TelegraphClass, TelegraphCue>>> = {
  spread: { title: "SPREAD", cue: "move apart" },
  stack: { title: "STACK", cue: "group up" },
};

export function telegraphCue(c: TelegraphClass | undefined): TelegraphCue | null {
  return (c && TELEGRAPH_CUES[c]) || null;
}

/** The label is fully opaque until this many ticks before resolve, then fades to 0 at resolve. */
export const LABEL_FADE_TICKS = 10;

/** Same-class telegraphs whose centres are closer than this (metres) share ONE label (the first
 * in snapshot order): a clumped pull of five casters reads as one instruction, not five stacked. */
export const LABEL_MERGE_M = 2.5;

export interface TelegraphLabelModel extends TelegraphCue {
  /** Seconds left to resolve, one decimal: "2.5s". */
  countdown: string;
  /** Bar fill in [0, 1]: 1 - telegraphFillFraction (drains as the decal fills). */
  remaining: number;
  opacity: number;
}

/** Pure: the label for one telegraph at currentTick, or null when its class gets no label. */
export function telegraphLabelModel(
  c: TelegraphClass | undefined,
  currentTick: number,
  resolvesTick: number,
): TelegraphLabelModel | null {
  const cue = telegraphCue(c);
  if (!cue) return null;
  const ticksLeft = Math.max(0, resolvesTick - currentTick);
  return {
    ...cue,
    countdown: `${(ticksLeft / TICK_HZ).toFixed(1)}s`,
    remaining: 1 - telegraphFillFraction(currentTick, resolvesTick),
    opacity: Math.min(1, ticksLeft / LABEL_FADE_TICKS),
  };
}

/** Canvas px and constant on-screen size: ~1.4x the window-ring label's px-per-world ratio, so
 * the instruction reads clearly from the default camera distance. */
const LABEL_PX: [number, number] = [512, 144];
const LABEL_W = 0.4;
const LABEL_H = (LABEL_W * LABEL_PX[1]) / LABEL_PX[0];
const LABEL_LIFT_M = 0.15;
const BAR = { x: 96, y: 118, w: 320, h: 14 };
/** Bar width is quantised so the canvas is only redrawn when the bar visibly changes. */
const BAR_STEPS = 64;
/** Above every other sprite: name tags and bars (createCanvasSprite's 10) and floating damage
 * numbers (damageNumbers.ts's 20) -- the instruction must never be crossed out by a busy pull. */
export const TELEGRAPH_LABEL_RENDER_ORDER = 21;

function hexCss(color: number): string {
  return `#${color.toString(16).padStart(6, "0")}`;
}

function text(ctx: CanvasRenderingContext2D, s: string, y: number, color: string, px: number): void {
  ctx.font = `bold ${px}px sans-serif`;
  ctx.lineWidth = 8;
  ctx.strokeStyle = "rgba(0,0,0,0.9)";
  ctx.fillStyle = color;
  ctx.strokeText(s, LABEL_PX[0] / 2, y);
  ctx.fillText(s, LABEL_PX[0] / 2, y);
}

function draw(label: CanvasSprite, model: TelegraphLabelModel, color: string, barW: number): void {
  const { ctx, canvas } = label;
  ctx.clearRect(0, 0, canvas.width, canvas.height);
  ctx.fillStyle = "rgba(12,16,22,0.6)"; // backing panel: legible over a busy pull
  ctx.fillRect(0, 0, canvas.width, canvas.height);
  ctx.textAlign = "center";
  ctx.textBaseline = "middle";
  text(ctx, `${model.title}: ${model.cue}`, 34, color, 46);
  text(ctx, model.countdown, 84, "#eef2f6", 38);
  ctx.fillStyle = "rgba(0,0,0,0.75)";
  ctx.fillRect(BAR.x - 3, BAR.y - 3, BAR.w + 6, BAR.h + 6);
  ctx.fillStyle = color;
  ctx.fillRect(BAR.x, BAR.y, barW, BAR.h);
  label.sync();
}

/** The fields of render/telegraphs.ts's RenderTelegraph a label needs. */
export interface LabelledTelegraph {
  id: string;
  center: { x: number; z: number };
  resolvesTick: number;
  telegraphClass?: TelegraphClass;
}

interface LabelView {
  label: CanvasSprite;
  key: string;
}

export class TelegraphLabelPool {
  private readonly views = new Map<string, LabelView>();

  constructor(private readonly scene: THREE.Scene) {}

  sync(telegraphs: readonly LabelledTelegraph[], currentTick: number): void {
    const seen = new Set<string>();
    const placed: LabelledTelegraph[] = [];
    const mergeMm = LABEL_MERGE_M * MM_PER_M;
    for (const t of telegraphs) {
      const model = telegraphLabelModel(t.telegraphClass, currentTick, t.resolvesTick);
      if (!model) continue;
      const near = (p: LabelledTelegraph): boolean =>
        p.telegraphClass === t.telegraphClass && Math.hypot(p.center.x - t.center.x, p.center.z - t.center.z) < mergeMm;
      if (placed.some(near)) continue;
      placed.push(t);
      seen.add(t.id);
      let view = this.views.get(t.id);
      if (!view) {
        const label = createCanvasSprite(LABEL_PX[0], LABEL_PX[1], LABEL_W, LABEL_H, false);
        label.sprite.renderOrder = TELEGRAPH_LABEL_RENDER_ORDER;
        this.scene.add(label.sprite);
        view = { label, key: "" };
        this.views.set(t.id, view);
      }
      const barW = Math.round(model.remaining * BAR_STEPS) * (BAR.w / BAR_STEPS);
      const color = hexCss(telegraphStyle(t.telegraphClass).color);
      const key = `${model.title}|${model.countdown}|${barW}`;
      if (view.key !== key) {
        draw(view.label, model, color, barW);
        view.key = key;
      }
      view.label.sprite.material.opacity = model.opacity;
      view.label.sprite.position.set(t.center.x / MM_PER_M, LABEL_LIFT_M, t.center.z / MM_PER_M);
    }
    for (const [id, view] of this.views) {
      if (seen.has(id)) continue;
      this.scene.remove(view.label.sprite);
      view.label.sprite.material.map?.dispose();
      view.label.sprite.material.dispose();
      this.views.delete(id);
    }
  }
}
