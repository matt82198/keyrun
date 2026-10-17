/**
 * client/src/render/actor-label.ts
 *
 * Pure (Three-free) model for every actor's D10 readability dressing in render/actors.ts: who it is
 * (name tag text + colour), what silhouette it gets (per-role scale), and what it is doing (the
 * "casting X" line, flagged KICK when the cast is interruptible). Snapshot state + display info only.
 *
 * - self: "YOU: <class>" in white, tallest and lit (render/actors.ts adds the white ground ring).
 * - party bot: "[T]/[H]/[D] <class>" in its role colour (roleColor.ts).
 * - enemy: its content name in red; boss: "BOSS: <name>" in boss purple and 1.7x scale.
 *
 * Usage: `const l = actorLabel(actor, isSelf, display); drawNameTag(sprite, l);`
 */
import type { ActorSnapshot } from "../sim-host/snapshot.js";
import { abilityName, actorName, type DisplayInfo } from "../sim-host/display-info.js";
import { colorForActor, COLOR_BOSS } from "./roleColor.js";

export interface ActorLabel {
  title: string;
  color: string;
  /** "" when not casting; else "casting <ability>" (+ " - KICK!" when interruptible). */
  castLine: string;
  kickable: boolean;
  /** Overrides the second line's colour (render/actors.ts's status tag / Downed line). */
  lineColor?: string;
  /** Capsule scale [x/z, y]: tank wide, healer slim, boss huge, self tallest. */
  scale: [number, number];
}

const ROLE_LETTER: Record<string, string> = { tank: "T", healer: "H", damage: "D" };

function hex(c: number): string {
  return `#${c.toString(16).padStart(6, "0")}`;
}

export function isBoss(a: Pick<ActorSnapshot, "kind" | "classId">): boolean {
  return a.kind === "enemy" && a.classId.endsWith(".boss");
}

function scaleFor(a: ActorSnapshot, isSelf: boolean): [number, number] {
  if (isBoss(a)) return [1.7, 1.7];
  if (a.kind === "enemy") return [1.15, 1.0];
  const base: [number, number] = a.role === "tank" ? [1.3, 1.0] : a.role === "healer" ? [0.85, 1.0] : [1.0, 1.0];
  return isSelf ? [base[0], base[1] * 1.2] : base;
}

export function actorLabel(a: ActorSnapshot, isSelf: boolean, info?: DisplayInfo): ActorLabel {
  const name = actorName(info, a.classId);
  let title: string;
  let color: string;
  if (a.kind === "enemy") {
    title = isBoss(a) ? `BOSS: ${name}` : name;
    color = isBoss(a) ? hex(COLOR_BOSS) : "#ff6b6b";
  } else if (isSelf) {
    title = `YOU: ${name}`;
    color = "#ffffff";
  } else {
    title = `[${ROLE_LETTER[a.role] ?? "?"}] ${name}`;
    color = hex(colorForActor("player", a.role));
  }
  // Only an ENEMY's interruptible cast is a kick prompt; bots' own casts get no line (clutter).
  const kickable = a.kind === "enemy" && a.cast?.kickable === true;
  const showCast = a.cast && (a.kind === "enemy" || isSelf);
  const castLine = showCast ? `casting ${abilityName(info, a.cast!.abilityId)}${kickable ? " - KICK!" : ""}` : "";
  return { title, color, castLine, kickable, scale: scaleFor(a, isSelf) };
}
