/**
 * bots/src/skill.ts
 *
 * The 0-100 "skill dial" that governs how sharp a bot plays: it maps to a
 * deterministic reaction-delay-in-ticks (pure function of the dial, no
 * randomness) and a mistake probability sampled via a tiny local xorshift128
 * PRNG step (deterministic given a seed/state, independent of Math.random).
 *
 * Usage: `import { reactionDelayTicks, rollMistake } from "./skill.js"`
 */
import type { Tick } from "./types.js";

export type PrngState = [number, number, number, number];

const MIN_DELAY_TICKS = 1;
const MAX_DELAY_TICKS = 10;
const MAX_MISTAKE_CHANCE = 0.35;

function clampDial(dial: number): number {
  return Math.max(0, Math.min(100, dial));
}

/**
 * Reaction delay in ticks for a given skill dial: 100 (perfect) reacts in
 * MIN_DELAY_TICKS, 0 (worst) reacts in MAX_DELAY_TICKS. Pure and
 * deterministic: the same dial always yields the same delay.
 */
export function reactionDelayTicks(dial: number): Tick {
  const d = clampDial(dial);
  const span = MAX_DELAY_TICKS - MIN_DELAY_TICKS;
  return Math.round(MAX_DELAY_TICKS - (span * d) / 100);
}

/** Probability [0,1] of a rotation/positioning mistake for a given skill dial. */
export function mistakeChance(dial: number): number {
  const d = clampDial(dial);
  return MAX_MISTAKE_CHANCE * (1 - d / 100);
}

/**
 * One xorshift128 step. Deterministic, allocation-free, independent of
 * Math.random so bot decisions replay exactly given the same seed.
 * Returns a float in [0,1) and the advanced state.
 */
export function stepPrng(state: PrngState): { value: number; next: PrngState } {
  let [x, y, z, w] = state;
  const t = x ^ (x << 11);
  x = y;
  y = z;
  z = w;
  w = (w ^ (w >>> 19)) ^ (t ^ (t >>> 8));
  w >>>= 0;
  const next: PrngState = [x >>> 0, y >>> 0, z >>> 0, w];
  const value = next[3] / 0xffffffff;
  return { value, next };
}

/** Seed a PrngState from a single integer seed (splitmix-ish spread). */
export function seedPrng(seed: number): PrngState {
  const s = seed >>> 0 || 1;
  return [s, (s * 2654435761) >>> 0 || 1, (s ^ 0x9e3779b9) >>> 0 || 1, (s * 40503) >>> 0 || 1];
}

/** Roll whether a mistake happens for `dial`, advancing and returning the PRNG state. */
export function rollMistake(dial: number, prng: PrngState): { mistake: boolean; next: PrngState } {
  const { value, next } = stepPrng(prng);
  return { mistake: value < mistakeChance(dial), next };
}
