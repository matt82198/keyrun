// Draws render/actor-label.ts's ActorLabel into an actor's billboard name-tag sprite: the title on
// line 1 in the label colour, and (while casting) the "casting X" line under it in gold when the cast
// is kickable, grey otherwise. Redraws only when the text changes (canvas uploads are not free).
import type { CanvasSprite } from "./canvasSprite.js";
import type { ActorLabel } from "./actor-label.js";
import { CAST_FILL_KICKABLE, CAST_FILL_UNKICKABLE } from "./castBar.js";

export const NAME_TAG_PX: [number, number] = [512, 112];

const drawn = new WeakMap<CanvasSprite, string>();

function line(ctx: CanvasRenderingContext2D, text: string, y: number, color: string, px: number): void {
  ctx.font = `bold ${px}px sans-serif`;
  ctx.lineWidth = 8;
  ctx.strokeStyle = "rgba(0,0,0,0.9)";
  ctx.fillStyle = color;
  ctx.strokeText(text, NAME_TAG_PX[0] / 2, y);
  ctx.fillText(text, NAME_TAG_PX[0] / 2, y);
}

export function drawNameTag(tag: CanvasSprite, label: ActorLabel): void {
  const key = `${label.title}|${label.color}|${label.castLine}`;
  if (drawn.get(tag) === key) return;
  drawn.set(tag, key);
  const { ctx, canvas } = tag;
  ctx.clearRect(0, 0, canvas.width, canvas.height);
  ctx.textAlign = "center";
  ctx.textBaseline = "middle";
  line(ctx, label.title, 30, label.color, 40);
  if (label.castLine) line(ctx, label.castLine, 82, label.kickable ? CAST_FILL_KICKABLE : CAST_FILL_UNKICKABLE, 34);
  tag.sync();
}
