import { describe, expect, it } from "vitest";
import { computeMoveDeltaMm } from "../../client/src/camera/movement.js";

const NO_KEYS = { forward: false, back: false, left: false, right: false };

describe("computeMoveDeltaMm", () => {
  it("returns zero when no movement keys are held", () => {
    expect(computeMoveDeltaMm(NO_KEYS, 0, 1000)).toEqual({ dx: 0, dz: 0 });
  });

  it("moves forward along -Z at camera yaw 0, in integer millimetres", () => {
    const result = computeMoveDeltaMm({ ...NO_KEYS, forward: true }, 0, 1500);
    expect(result).toEqual({ dx: 0, dz: -1500 });
  });

  it("moves backward along +Z at camera yaw 0", () => {
    const result = computeMoveDeltaMm({ ...NO_KEYS, back: true }, 0, 1500);
    expect(result).toEqual({ dx: 0, dz: 1500 });
  });

  it("strafes right along +X at camera yaw 0", () => {
    const result = computeMoveDeltaMm({ ...NO_KEYS, right: true }, 0, 1500);
    expect(result).toEqual({ dx: 1500, dz: 0 });
  });

  it("strafes left along -X at camera yaw 0", () => {
    const result = computeMoveDeltaMm({ ...NO_KEYS, left: true }, 0, 1500);
    expect(result).toEqual({ dx: -1500, dz: 0 });
  });

  it("rotates forward with camera yaw (90 degrees -> -X)", () => {
    const result = computeMoveDeltaMm({ ...NO_KEYS, forward: true }, Math.PI / 2, 1000);
    expect(result.dx).toBe(-1000);
    expect(Math.abs(result.dz)).toBeLessThanOrEqual(1);
  });

  it("normalizes diagonal input so it is not faster than a single direction", () => {
    const result = computeMoveDeltaMm({ ...NO_KEYS, forward: true, right: true }, 0, 1000);
    const magnitude = Math.hypot(result.dx, result.dz);
    expect(magnitude).toBeGreaterThan(990);
    expect(magnitude).toBeLessThanOrEqual(1000);
  });

  it("cancels out opposite keys held together", () => {
    const result = computeMoveDeltaMm({ forward: true, back: true, left: true, right: true }, 0, 1000);
    expect(result).toEqual({ dx: 0, dz: 0 });
  });

  it("rounds output to whole millimetres", () => {
    const result = computeMoveDeltaMm({ ...NO_KEYS, forward: true, right: true }, 0.3, 777);
    expect(Number.isInteger(result.dx)).toBe(true);
    expect(Number.isInteger(result.dz)).toBe(true);
  });
});
