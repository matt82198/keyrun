/**
 * client/src/net/reconnect.ts
 *
 * Pure, deterministic reconnect backoff (no jitter -- deliberately, so tests and behaviour are
 * reproducible) plus the "backfilled" detector: after a reconnect, the server's room kept ticking
 * while this client was offline (server/CLAUDE.md's room.ts never pauses for a disconnected human,
 * it just hands the slot to a bot), so the first snapshot after reconnecting can land many ticks
 * ahead of the last one this client saw. net-client.ts surfaces that gap as a `backfilled` notice
 * rather than silently interpolating across it.
 *
 * Usage: `const policy = new ReconnectPolicy(); const delayMs = policy.nextDelayMs();`
 *        `if (detectBackfill(lastTick, snapshot.tick)) notify(...);`
 */

export interface BackoffConfig {
  baseMs: number;
  maxMs: number;
  factor: number;
}

export const DEFAULT_BACKOFF: BackoffConfig = { baseMs: 250, maxMs: 5000, factor: 2 };

/** Capped exponential backoff, attempt-counted (0, 1, 2, ...) -- `nextDelayMs()` returns this
 * attempt's delay and advances the counter; `reset()` on a successful reconnect. */
export class ReconnectPolicy {
  private attempt = 0;
  private readonly cfg: BackoffConfig;

  constructor(cfg: BackoffConfig = DEFAULT_BACKOFF) {
    this.cfg = cfg;
  }

  nextDelayMs(): number {
    const delay = Math.min(this.cfg.maxMs, this.cfg.baseMs * this.cfg.factor ** this.attempt);
    this.attempt += 1;
    return delay;
  }

  reset(): void {
    this.attempt = 0;
  }

  attemptsSoFar(): number {
    return this.attempt;
  }
}

/** True when `newTick` lands more than one tick past `lastSeenTick` -- a gap only a stall (a
 * reconnect, a throttled tab) can produce, since the server broadcasts every tick in order.
 * `lastSeenTick === null` (no snapshot seen yet) is never a backfill. */
export function detectBackfill(lastSeenTick: number | null, newTick: number): boolean {
  if (lastSeenTick === null) return false;
  return newTick > lastSeenTick + 1;
}
