/**
 * client/src/input/ping.ts
 *
 * Pure G-key -> PingEvent derivation. G is the party's only communication
 * key (design/BIBLE.md §2, §10): a target ping when the player has a
 * current target, otherwise a spot ping at the ground point under the
 * reticle. No DOM/Three.js dependency, mirroring commands.ts, so it is
 * directly unit-testable given a key-state sample and a tick.
 *
 * A ping is NOT a sim command (sim/core/commands.ts has no "ping" kind):
 * it is a client/bot-side channel carried alongside snapshots. See
 * bots/src/shared/pings.ts for the bots-side copy of this contract.
 *
 * Usage: `import { derivePingEvent, PingRateLimiter } from "./ping.js"`
 */

export type PingKind = "target" | "spot";

export interface PingGroundPoint {
  x: number;
  z: number;
}

export interface PingEvent {
  tick: number;
  actorId: number;
  kind: PingKind;
  targetId?: number;
  point?: PingGroundPoint;
}

/** Physical key for the ping action; matches client/keybinds.json's "ping" bind. */
export const DEFAULT_PING_KEY_CODE = "KeyG";

/** One ping per this many ticks, per actor (design backlog: rate-limited to one per 20 ticks). */
export const PING_RATE_LIMIT_TICKS = 20;

/**
 * Tracks the last tick each actor pinged so pings can be rate-limited.
 * Stateful like KeyState, so a caller holds one instance across ticks.
 */
export class PingRateLimiter {
  private lastPingTick = new Map<number, number>();

  /** True if `actorId` is allowed to ping again at `tick`. */
  canPing(actorId: number, tick: number, windowTicks: number = PING_RATE_LIMIT_TICKS): boolean {
    const last = this.lastPingTick.get(actorId);
    return last === undefined || tick - last >= windowTicks;
  }

  /** Records that `actorId` pinged at `tick`. */
  record(actorId: number, tick: number): void {
    const last = this.lastPingTick.get(actorId);
    if (last === undefined || tick > last) this.lastPingTick.set(actorId, tick);
  }

  reset(): void {
    this.lastPingTick.clear();
  }
}

/**
 * Derives the local player's PingEvent for this tick, or null when G was
 * not just pressed or the actor is still rate-limited. A non-null
 * `targetId` produces a "target" ping; otherwise a "spot" ping is emitted
 * at `groundPoint` (the world point under the reticle -- camera/raycast
 * math to compute that point is out of scope for this pure module).
 *
 * Edge-triggered: `codesJustPressed` is the same newly-pressed-this-tick
 * set produced by KeyState.sampleAndClearJustPressed(), so holding G does
 * not repeat-fire.
 */
export function derivePingEvent(
  codesJustPressed: ReadonlySet<string>,
  pingKeyCode: string,
  tick: number,
  actorId: number,
  targetId: number | null,
  groundPoint: PingGroundPoint,
  limiter: PingRateLimiter,
): PingEvent | null {
  if (!pingKeyCode || !codesJustPressed.has(pingKeyCode)) return null;
  if (!limiter.canPing(actorId, tick)) return null;

  const event: PingEvent =
    targetId !== null
      ? { tick, actorId, kind: "target", targetId }
      : { tick, actorId, kind: "spot", point: groundPoint };

  limiter.record(actorId, tick);
  return event;
}
