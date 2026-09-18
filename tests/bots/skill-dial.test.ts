/**
 * tests/bots/skill-dial.test.ts
 *
 * The 0-100 skill dial deterministically maps to reaction-delay ticks (pure
 * function of the dial, no randomness) and its mistake-roll PRNG step is
 * reproducible given the same seed.
 *
 * Usage: `npx vitest run tests/bots/skill-dial.test.ts`
 */
import { describe, expect, it } from "vitest";
import { reactionDelayTicks, mistakeChance, seedPrng, stepPrng, rollMistake } from "@bots/src/skill.js";

describe("skill dial", () => {
  it("maps a higher dial to a strictly shorter (or equal) reaction delay", () => {
    const worst = reactionDelayTicks(0);
    const mid = reactionDelayTicks(50);
    const best = reactionDelayTicks(100);
    expect(worst).toBeGreaterThan(mid);
    expect(mid).toBeGreaterThan(best);
    expect(best).toBeGreaterThanOrEqual(1);
  });

  it("is a pure function of the dial: same dial always yields the same delay", () => {
    expect(reactionDelayTicks(37)).toBe(reactionDelayTicks(37));
    expect(reactionDelayTicks(37)).toBe(reactionDelayTicks(37));
  });

  it("clamps out-of-range dials", () => {
    expect(reactionDelayTicks(-50)).toBe(reactionDelayTicks(0));
    expect(reactionDelayTicks(500)).toBe(reactionDelayTicks(100));
  });

  it("mistake chance is 0 at dial 100 and positive at dial 0", () => {
    expect(mistakeChance(100)).toBe(0);
    expect(mistakeChance(0)).toBeGreaterThan(0);
  });

  it("stepPrng is deterministic: same seed and state always produce the same next value", () => {
    const seed = seedPrng(42);
    const a = stepPrng(seed);
    const b = stepPrng(seed);
    expect(a.value).toBe(b.value);
    expect(a.next).toEqual(b.next);
  });

  it("rollMistake is reproducible: replaying the same seed reproduces the same mistake sequence", () => {
    const runA = replay(seedPrng(7), 20, 15);
    const runB = replay(seedPrng(7), 20, 15);
    expect(runA).toEqual(runB);
  });
});

function replay(seed: readonly [number, number, number, number], dial: number, steps: number): boolean[] {
  let prng: [number, number, number, number] = [...seed];
  const out: boolean[] = [];
  for (let i = 0; i < steps; i++) {
    const { mistake, next } = rollMistake(dial, prng);
    out.push(mistake);
    prng = next;
  }
  return out;
}
