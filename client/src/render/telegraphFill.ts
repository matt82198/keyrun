// Pure telegraph fill-fraction math (how "full"/imminent a ground decal
// should render as it approaches its resolvesTick), plus the pure
// telegraph class -> colour + edge style table. No Three.js dependency.
import type { TelegraphClass } from "../sim-host/telegraph-map.js";
import { COLOR_ENEMY } from "./roleColor.js";

export type TelegraphEdge = "solid" | "dashed" | "dotted" | "double";

export interface TelegraphStyle {
  color: number;
  edge: TelegraphEdge;
}

/**
 * D10 readability: shape plus colour plus edge, never colour alone (BIBLE sec 3).
 * Colours are Okabe-Ito (colour-blind-safe) picks bright against the 0x1b2027
 * floor and clear of roleColor's tank/healer/damage hues; the boss reuses
 * roleColor's enemy red so "red = the enemy/boss" stays one vocabulary.
 */
export const TELEGRAPH_STYLES: Readonly<Record<TelegraphClass, TelegraphStyle>> = {
  avoid: { color: 0xf0e442, edge: "solid" }, // yellow: generic hazard, get out
  spread: { color: 0xcc79a7, edge: "dashed" }, // reddish purple: broken ring, stand apart
  stack: { color: 0x56b4e9, edge: "dotted" }, // sky blue: stand together inside
  boss: { color: COLOR_ENEMY, edge: "double" }, // enemy red, heavy double rim
};

export function telegraphStyle(c: TelegraphClass | undefined): TelegraphStyle {
  return TELEGRAPH_STYLES[c ?? "avoid"];
}

export interface EdgePattern {
  loops: number;
  dash: { dashSize: number; gapSize: number } | null;
}

/** Outline recipe per edge style (metres): loop count and dash lengths. */
export function edgePattern(edge: TelegraphEdge): EdgePattern {
  switch (edge) {
    case "double":
      return { loops: 2, dash: null };
    case "dashed":
      return { loops: 1, dash: { dashSize: 0.6, gapSize: 0.35 } };
    case "dotted":
      return { loops: 1, dash: { dashSize: 0.12, gapSize: 0.22 } };
    default:
      return { loops: 1, dash: null };
  }
}

/** Ticks of warning a telegraph is assumed visible for before it resolves. */
export const DEFAULT_TELEGRAPH_WARN_TICKS = 60; // 3s at 20Hz

/**
 * Fraction in [0, 1] of how filled a telegraph decal should render, given
 * the current tick and the tick it resolves on. 0 = just appeared (fully
 * unfilled), 1 = resolves this tick or has already passed (fully filled).
 */
export function telegraphFillFraction(
  currentTick: number,
  resolvesTick: number,
  warnTicks: number = DEFAULT_TELEGRAPH_WARN_TICKS,
): number {
  if (warnTicks <= 0) return 1;
  const ticksRemaining = resolvesTick - currentTick;
  const fraction = 1 - ticksRemaining / warnTicks;
  return Math.min(1, Math.max(0, fraction));
}
