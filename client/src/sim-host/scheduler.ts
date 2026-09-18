/**
 * client/src/sim-host/scheduler.ts
 *
 * Pure fixed-timestep-with-capped-catch-up math for the sim tick loop. worker.ts's real Worker
 * would normally free-run its own event loop; per worker.ts's file docstring this POC instead
 * runs on the main thread via a timer, so a naive `setInterval(tick, TICK_MS)` can drift or -- if
 * the tab is throttled/backgrounded -- fire once after a multi-second gap, which would otherwise
 * either desync the sim clock from real time or attempt to replay a huge backlog of ticks in one
 * go. `computeTicksToRun` is the accumulator step that decides how many whole ticks to run this
 * callback, capped at `maxCatchUpTicks`, carrying only the leftover sub-tick remainder forward
 * (any backlog beyond the cap is dropped rather than queued, so the sim never "explodes" trying to
 * catch up after a long stall -- it just resumes at the current tick).
 *
 * Usage: `const { ticks, nextAccumulatedMs } = computeTicksToRun(accumulatedMs, elapsedMs, TICK_MS);`
 */

export const DEFAULT_MAX_CATCH_UP_TICKS = 5;

export interface TickPlan {
  ticks: number;
  nextAccumulatedMs: number;
}

export function computeTicksToRun(
  accumulatedMs: number,
  elapsedMs: number,
  tickMs: number,
  maxCatchUpTicks: number = DEFAULT_MAX_CATCH_UP_TICKS,
): TickPlan {
  const total = Math.max(0, accumulatedMs) + Math.max(0, elapsedMs);
  const rawTicks = Math.floor(total / tickMs);
  const ticks = Math.min(maxCatchUpTicks, rawTicks);
  if (rawTicks > maxCatchUpTicks) {
    // Backlog exceeds the cap: drop the excess rather than let it balloon into an even bigger
    // catch-up next callback -- resume real-time pacing from a clean accumulator.
    return { ticks, nextAccumulatedMs: 0 };
  }
  return { ticks, nextAccumulatedMs: total - ticks * tickMs };
}
