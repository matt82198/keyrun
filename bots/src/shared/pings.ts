/**
 * bots/src/shared/pings.ts
 *
 * Local copy of the client-facing PingEvent contract (see
 * client/src/input/ping.ts) -- bots has zero compile-time dependency on
 * client/, same reasoning as types.ts's local copy of the sim contract.
 * A ping is NOT a sim command (sim/core/commands.ts has no "ping" kind):
 * it is a client/bot-side channel carried alongside snapshots, produced
 * by the party's G key (design/BIBLE.md §2, §10).
 *
 * Contract note: a "Window" carrier (design/BIBLE.md §4) is detected by
 * the local convention that its effectId is prefixed "window:" -- same
 * pattern as roles.ts's DISPELLABLE_PREFIX convention, since ActorState
 * carries no explicit Window flag.
 *
 * Usage: `import { isPingLive, latestLivePing, type PingEvent } from "./pings.js"`
 */
import type { ActorId, Tick } from "../types.js";

export type PingKind = "target" | "spot";

export interface PingPoint {
  x: number;
  z: number;
}

export interface PingEvent {
  tick: Tick;
  actorId: ActorId;
  kind: PingKind;
  targetId?: ActorId;
  point?: PingPoint;
}

/** One ping per this many ticks, per actor -- matches client/src/input/ping.ts. */
export const PING_RATE_LIMIT_TICKS = 20;

/** How long a ping stays "live" for bot reactions: 3s at 20Hz, matching the render label's lifetime. */
export const PING_REACTION_WINDOW_TICKS = 60;

/** Local convention for a Window-carrying effect's id; see file header. */
export const WINDOW_EFFECT_PREFIX = "window:";

/** True if `event` is still within its reaction window at `tick` (never true for a future-tick ping). */
export function isPingLive(event: PingEvent, tick: Tick, windowTicks: Tick = PING_REACTION_WINDOW_TICKS): boolean {
  return tick >= event.tick && tick - event.tick <= windowTicks;
}

/**
 * The most recent live ping (optionally filtered to `kind`), latest tick
 * first, ties broken by lowest actorId. Null if none qualify.
 */
export function latestLivePing(
  pings: readonly PingEvent[],
  tick: Tick,
  windowTicks: Tick = PING_REACTION_WINDOW_TICKS,
  kind?: PingKind,
): PingEvent | null {
  let best: PingEvent | null = null;
  for (const p of pings) {
    if (kind !== undefined && p.kind !== kind) continue;
    if (!isPingLive(p, tick, windowTicks)) continue;
    if (
      best === null ||
      p.tick > best.tick ||
      (p.tick === best.tick && p.actorId < best.actorId)
    ) {
      best = p;
    }
  }
  return best;
}
