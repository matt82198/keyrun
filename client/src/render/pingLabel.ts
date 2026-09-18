// Pure ping label/expiry math -- how long a ping marker stays on screen and
// what auto-label it shows (design/BIBLE.md §10: "kick", "stack here",
// "Window!"). No Three.js dependency, mirroring telegraphFill.ts, since
// "three" is CDN-loaded (esbuild external, per client/CLAUDE.md) and not an
// installed package -- any module that imports it cannot be loaded by Vitest.
import type { PingEvent } from "../input/ping.js";
import { SIM_HZ } from "../sim-host/snapshot.js";

/** How long a ping's floor marker + label stay visible: 3 seconds at 20Hz. */
export const PING_LABEL_LIFETIME_TICKS = 3 * SIM_HZ;

export interface PingLabelContext {
  /** The pinged target is mid-cast on an ability the party can interrupt. */
  targetIsInterruptibleCast: boolean;
  /** The pinged target carries a Window effect (design/BIBLE.md §4). */
  targetHasWindow: boolean;
}

const NO_CONTEXT: PingLabelContext = { targetIsInterruptibleCast: false, targetHasWindow: false };

/**
 * Chooses the auto-label text for a ping, per design/BIBLE.md §10:
 * "Kick!" for an interruptible cast, "Stack here" for a spot, "Window!"
 * for a Window carrier, else "Focus". Interruptible-cast beats Window
 * when a target somehow has both -- the interrupt is the more urgent call.
 */
export function pingLabelText(event: PingEvent, ctx: PingLabelContext = NO_CONTEXT): string {
  if (event.kind === "spot") return "Stack here";
  if (ctx.targetIsInterruptibleCast) return "Kick!";
  if (ctx.targetHasWindow) return "Window!";
  return "Focus";
}

/** True once `currentTick` is `lifetimeTicks` or more past the ping's own tick. */
export function isPingExpired(
  event: PingEvent,
  currentTick: number,
  lifetimeTicks: number = PING_LABEL_LIFETIME_TICKS,
): boolean {
  return currentTick - event.tick >= lifetimeTicks;
}

/** Fraction in [0, 1] of a ping's 3-second lifetime that remains, for fade-out. */
export function pingLifeRemainingFraction(
  event: PingEvent,
  currentTick: number,
  lifetimeTicks: number = PING_LABEL_LIFETIME_TICKS,
): number {
  if (lifetimeTicks <= 0) return 0;
  const age = currentTick - event.tick;
  return Math.min(1, Math.max(0, 1 - age / lifetimeTicks));
}
