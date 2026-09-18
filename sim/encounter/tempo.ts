/**
 * sim/encounter/tempo.ts — key level -> Tempo. All basis-point (bp) integer math, 10000 = 100%.
 *
 * Key level scales the fight's pace, never its health pool much (design bible section 8): enemy cast
 * frequency rises (`every` shrinks), telegraph `leadTicks` shrinks toward a floor (never to 0 — a
 * mechanic must stay reactable), extra mechanics unlock at fixed key thresholds, and health scales
 * gently. Every scale function does one integer division (`Math.trunc`, never a stored float) and
 * clamps to its documented floor.
 *
 * Usage: `const every = scaleCastEvery(baseEvery, keyLevel); const lead = scaleLeadTicks(baseLead, keyLevel);`
 */

export const BP_DENOM = 10000;

// TUNE: cast frequency +2% per key level above 1 (mult grows -> `every` shrinks).
export const TEMPO_BP_PER_KEY = 200;
// TUNE: telegraph lead time shrinks 1.5% per key level above 1, capped at 100% shrink.
export const LEAD_SHRINK_BP_PER_KEY = 150;
// TUNE: never react to a telegraph in under 10 ticks (0.5s at 20Hz).
export const LEAD_FLOOR_TICKS = 10;
// TUNE: never let an enemy cast more than once every 20 ticks (1s at 20Hz), however high the key.
export const EVERY_FLOOR_TICKS = 20;
// TUNE: health +3% per key level above 1.
export const HEALTH_BP_PER_KEY = 300;

export const MECHANIC_UNLOCK_KEYS = [5, 10, 15];

export function tempoMultiplierBp(keyLevel: number): number {
  return BP_DENOM + TEMPO_BP_PER_KEY * Math.max(0, keyLevel - 1);
}

export function scaleCastEvery(baseEveryTicks: number, keyLevel: number): number {
  const mult = tempoMultiplierBp(keyLevel);
  const scaled = Math.trunc((baseEveryTicks * BP_DENOM) / mult);
  return Math.max(EVERY_FLOOR_TICKS, scaled);
}

export function scaleLeadTicks(baseLeadTicks: number, keyLevel: number): number {
  const shrinkBp = Math.min(BP_DENOM, LEAD_SHRINK_BP_PER_KEY * Math.max(0, keyLevel - 1));
  const scaled = Math.trunc((baseLeadTicks * (BP_DENOM - shrinkBp)) / BP_DENOM);
  return Math.max(LEAD_FLOOR_TICKS, scaled);
}

export function scaleHealth(baseHealth: number, keyLevel: number): number {
  const bp = BP_DENOM + HEALTH_BP_PER_KEY * Math.max(0, keyLevel - 1);
  return Math.trunc((baseHealth * bp) / BP_DENOM);
}

export function unlockedMechanicCount(keyLevel: number): number {
  return MECHANIC_UNLOCK_KEYS.filter((k) => keyLevel >= k).length;
}
