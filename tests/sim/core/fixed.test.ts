// Usage: vitest suite for sim/core/fixed.ts integer vector math.
import { describe, expect, it } from "vitest";
import { add, sub, scale, dot, length, isqrt, normaliseTo, yawBetween, angleDiff } from "@sim/core/fixed";

describe("fixed: isqrt", () => {
  it("is exact for perfect squares", () => {
    expect(isqrt(0)).toBe(0);
    expect(isqrt(1)).toBe(1);
    expect(isqrt(4)).toBe(2);
    expect(isqrt(9)).toBe(3);
    expect(isqrt(144)).toBe(12);
    expect(isqrt(1000000)).toBe(1000);
  });

  it("floors for non-perfect squares", () => {
    expect(isqrt(2)).toBe(1);
    expect(isqrt(8)).toBe(2);
    expect(isqrt(10)).toBe(3);
    expect(isqrt(99)).toBe(9);
  });

  it("rejects negative input", () => {
    expect(() => isqrt(-1)).toThrow();
  });
});

describe("fixed: vector arithmetic", () => {
  const a = { x: 1, y: 2, z: 3 };
  const b = { x: 4, y: -5, z: 6 };

  it("adds and subtracts componentwise", () => {
    expect(add(a, b)).toEqual({ x: 5, y: -3, z: 9 });
    expect(sub(a, b)).toEqual({ x: -3, y: 7, z: -3 });
  });

  it("dots componentwise products", () => {
    expect(dot(a, b)).toBe(1 * 4 + 2 * -5 + 3 * 6);
  });

  it("scales by basis points with rounding, no floats in result", () => {
    const v = { x: 100, y: 0, z: -100 };
    const r = scale(v, 5000); // half
    expect(r).toEqual({ x: 50, y: 0, z: -50 });
    for (const k of ["x", "y", "z"] as const) expect(Number.isInteger(r[k])).toBe(true);
  });

  it("computes integer length via isqrt (3-4-5 triangle)", () => {
    expect(length({ x: 3, y: 0, z: 4 })).toBe(5);
    expect(length({ x: 0, y: 0, z: 0 })).toBe(0);
  });

  it("normalises to a target integer length", () => {
    const v = { x: 3, y: 0, z: 4 };
    const r = normaliseTo(v, 10);
    expect(r).toEqual({ x: 6, y: 0, z: 8 });
    expect(normaliseTo({ x: 0, y: 0, z: 0 }, 10)).toEqual({ x: 0, y: 0, z: 0 });
  });
});

describe("fixed: yaw", () => {
  it("yawBetween returns 0 along +x", () => {
    expect(yawBetween({ x: 0, y: 0, z: 0 }, { x: 10, y: 0, z: 0 })).toBe(0);
  });

  it("yawBetween returns quarter turn along +z", () => {
    expect(yawBetween({ x: 0, y: 0, z: 0 }, { x: 0, y: 0, z: 10 })).toBe(256);
  });

  it("yawBetween returns half turn along -x", () => {
    expect(yawBetween({ x: 0, y: 0, z: 0 }, { x: -10, y: 0, z: 0 })).toBe(512);
  });

  it("yawBetween wraps negative angles into 0..1023", () => {
    const yaw = yawBetween({ x: 0, y: 0, z: 0 }, { x: 0, y: 0, z: -10 });
    expect(yaw).toBeGreaterThanOrEqual(0);
    expect(yaw).toBeLessThan(1024);
    expect(yaw).toBe(768);
  });

  it("yawBetween of coincident points is 0", () => {
    expect(yawBetween({ x: 5, y: 0, z: 5 }, { x: 5, y: 9, z: 5 })).toBe(0);
  });

  it("angleDiff finds the shortest signed path", () => {
    expect(angleDiff(0, 256)).toBe(256);
    expect(angleDiff(1000, 0)).toBe(24);
    expect(angleDiff(0, 1000)).toBe(-24);
    expect(angleDiff(100, 100)).toBe(0);
  });
});
