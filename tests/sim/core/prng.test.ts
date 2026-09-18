// Usage: vitest suite for sim/core/prng.ts xoshiro128** PRNG.
import { describe, expect, it } from "vitest";
import { seedState, nextInt } from "@sim/core/prng";

describe("prng: seedState", () => {
  it("derives a 4-int uint32 state deterministically from a seed", () => {
    const s1 = seedState(42);
    const s2 = seedState(42);
    expect(s1).toEqual(s2);
    expect(s1).toHaveLength(4);
    for (const v of s1) {
      expect(Number.isInteger(v)).toBe(true);
      expect(v).toBeGreaterThanOrEqual(0);
      expect(v).toBeLessThan(2 ** 32);
    }
  });

  it("different seeds produce different state", () => {
    expect(seedState(1)).not.toEqual(seedState(2));
  });
});

describe("prng: nextInt", () => {
  it("is deterministic for a fixed seed and known-answer for the first draws", () => {
    let s = seedState(1);
    const draws: number[] = [];
    for (let i = 0; i < 5; i++) {
      const [v, s2] = nextInt(s, 1000);
      draws.push(v);
      s = s2;
    }
    // Known-answer: locked from this implementation's own output (regression guard).
    expect(draws).toEqual([148, 13, 91, 82, 206]);
  });

  it("replays identically from the same seed across independent runs", () => {
    const run = () => {
      let s = seedState(7);
      const out: number[] = [];
      for (let i = 0; i < 20; i++) {
        const [v, s2] = nextInt(s, 100);
        out.push(v);
        s = s2;
      }
      return out;
    };
    expect(run()).toEqual(run());
  });

  it("stays within [0, n) and does not mutate the input state array", () => {
    const s = seedState(99);
    const before = [...s];
    const [v] = nextInt(s, 6);
    expect(v).toBeGreaterThanOrEqual(0);
    expect(v).toBeLessThan(6);
    expect(s).toEqual(before);
  });

  it("rejects a non-positive or non-integer bound", () => {
    const s = seedState(1);
    expect(() => nextInt(s, 0)).toThrow();
    expect(() => nextInt(s, -3)).toThrow();
    expect(() => nextInt(s, 1.5)).toThrow();
  });
});
