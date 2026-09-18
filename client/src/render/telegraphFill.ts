// Pure telegraph fill-fraction math (how "full"/imminent a ground decal
// should render as it approaches its resolvesTick). No Three.js dependency.

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
