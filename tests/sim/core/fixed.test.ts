// Usage: vitest suite for sim/core/fixed.ts integer vector math.
import { describe, expect, it } from "vitest";
import { readdirSync, readFileSync } from "node:fs";
import { join } from "node:path";
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

  it("yawBetween of coincident points is 0 (documented zero-vector value)", () => {
    expect(yawBetween({ x: 5, y: 0, z: 5 }, { x: 5, y: 9, z: 5 })).toBe(0);
    expect(yawBetween({ x: 0, y: 0, z: 0 }, { x: 0, y: 0, z: 0 })).toBe(0);
  });

  it("yawBetween returns exact buckets for the 8 axis/diagonal directions", () => {
    const origin = { x: 0, y: 0, z: 0 };
    // [x, y, z, expectedYaw]
    const cases: Array<[number, number, number, number]> = [
      [10, 0, 0, 0], // +x
      [10, 0, 10, 128], // +x/+z diagonal
      [0, 0, 10, 256], // +z
      [-10, 0, 10, 384], // -x/+z diagonal
      [-10, 0, 0, 512], // -x
      [-10, 0, -10, 640], // -x/-z diagonal
      [0, 0, -10, 768], // -z
      [10, 0, -10, 896], // +x/-z diagonal
    ];
    for (const [x, y, z, expectedYaw] of cases) {
      expect(yawBetween(origin, { x, y, z })).toBe(expectedYaw);
    }
  });

  it("yawBetween is monotonic non-decreasing (mod wrap) around a full circle sampled every ~2 degrees", () => {
    const origin = { x: 0, y: 0, z: 0 };
    const radius = 100000;
    const stepsPerDegree = 1; // 360 samples, ~1 degree apart (finer than the ~2 degree requirement)
    const samples: number[] = [];
    for (let deg = 0; deg < 360; deg += 1 / stepsPerDegree) {
      const rad = (deg * Math.PI) / 180;
      const x = Math.round(radius * Math.cos(rad));
      const z = Math.round(radius * Math.sin(rad));
      samples.push(yawBetween(origin, { x, y: 0, z }));
    }
    let wraps = 0;
    for (let i = 1; i < samples.length; i++) {
      const prev = samples[i - 1] as number;
      const cur = samples[i] as number;
      if (cur < prev) {
        wraps += 1;
        // a decrease is only legal as the single wrap from near 1023 back to near 0
        expect(prev).toBeGreaterThan(1024 - 20);
        expect(cur).toBeLessThan(20);
      } else {
        expect(cur).toBeGreaterThanOrEqual(prev);
      }
    }
    expect(wraps).toBeLessThanOrEqual(1);
  });

  it("yawBetween matches the float atan2 reference within 1 bucket, sampled every ~2 degrees", () => {
    const origin = { x: 0, y: 0, z: 0 };
    const radius = 100000;
    let maxError = 0;
    for (let deg = 0; deg < 360; deg += 2) {
      const rad = (deg * Math.PI) / 180;
      const x = Math.round(radius * Math.cos(rad));
      const z = Math.round(radius * Math.sin(rad));
      const actual = yawBetween(origin, { x, y: 0, z });

      // Float reference computed here in the test only -- never in sim/core.
      const refRad = Math.atan2(z, x);
      let refYaw = Math.round((refRad / (2 * Math.PI)) * 1024) % 1024;
      if (refYaw < 0) refYaw += 1024;

      let diff = Math.abs(actual - refYaw);
      if (diff > 512) diff = 1024 - diff; // circular distance
      maxError = Math.max(maxError, diff);
    }
    expect(maxError).toBeLessThanOrEqual(1);
  });

  it("angleDiff finds the shortest signed path", () => {
    expect(angleDiff(0, 256)).toBe(256);
    expect(angleDiff(1000, 0)).toBe(24);
    expect(angleDiff(0, 1000)).toBe(-24);
    expect(angleDiff(100, 100)).toBe(0);
  });
});

describe("fixed: no floating-point trig/sqrt on the sim path", () => {
  it("sim/core/*.ts never calls Math.atan2, Math.sin, Math.cos, or Math.sqrt", () => {
    // Matched as calls (token immediately followed by "(") so this doesn't flag doc comments that
    // merely name the banned functions while explaining the invariant -- only real invocations count.
    const bannedCalls = ["Math.atan2(", "Math.sin(", "Math.cos(", "Math.sqrt("];
    const simCoreDir = join(process.cwd(), "sim", "core");
    const files = readdirSync(simCoreDir).filter((f) => f.endsWith(".ts"));
    expect(files.length).toBeGreaterThan(0);

    const offenders: string[] = [];
    for (const file of files) {
      const lines = readFileSync(join(simCoreDir, file), "utf8").split("\n");
      lines.forEach((line, lineNo) => {
        const code = line.split("//")[0] ?? ""; // ignore trailing line comments
        for (const call of bannedCalls) {
          if (code.includes(call)) offenders.push(`${file}:${lineNo + 1}: ${call}`);
        }
      });
    }
    expect(offenders).toEqual([]);
  });
});
