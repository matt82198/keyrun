/**
 * Tests for integer movement, collision push-out, and bounds clamping (sim/combat/movement.ts).
 */
import { describe, expect, it } from "vitest";
import { createActor } from "../../../sim/combat/actors";
import { applyMovement, clampToBounds, isqrt, resolveCollision, type Bounds } from "../../../sim/combat/movement";

describe("isqrt", () => {
  it("is exact for perfect squares", () => {
    expect(isqrt(0)).toBe(0);
    expect(isqrt(1)).toBe(1);
    expect(isqrt(144)).toBe(12);
    expect(isqrt(1000000)).toBe(1000);
  });

  it("floors for non-perfect squares", () => {
    expect(isqrt(2)).toBe(1);
    expect(isqrt(99)).toBe(9);
    expect(isqrt(1000001)).toBe(1000);
  });
});

describe("applyMovement", () => {
  it("moves at exactly baseSpeed along a unit axis direction", () => {
    const a = createActor({ id: 1, kind: "player", classId: "x", role: "damage", pos: { x: 0, y: 0, z: 0 }, maxHealth: 1, maxResource: 0 });
    applyMovement(a, { x: 1, z: 0 }, 300, 0, 512);
    expect(a.pos).toEqual({ x: 300, y: 0, z: 0 });
    expect(a.yaw).toBe(512);
  });

  it("normalizes a diagonal direction to the same integer speed magnitude", () => {
    const a = createActor({ id: 1, kind: "player", classId: "x", role: "damage", pos: { x: 0, y: 0, z: 0 }, maxHealth: 1, maxResource: 0 });
    applyMovement(a, { x: 3, z: 4 }, 500, 0, 0);
    expect(a.pos).toEqual({ x: 300, y: 0, z: 400 });
  });

  it("applies a haste-style movement modifier in basis points", () => {
    const a = createActor({ id: 1, kind: "player", classId: "x", role: "damage", pos: { x: 0, y: 0, z: 0 }, maxHealth: 1, maxResource: 0 });
    applyMovement(a, { x: 1, z: 0 }, 300, 5000, 0); // +50%
    expect(a.pos.x).toBe(450);
  });

  it("updates yaw even when the direction vector is zero", () => {
    const a = createActor({ id: 1, kind: "player", classId: "x", role: "damage", pos: { x: 10, y: 0, z: 10 }, maxHealth: 1, maxResource: 0 });
    applyMovement(a, { x: 0, z: 0 }, 300, 0, 256);
    expect(a.pos).toEqual({ x: 10, y: 0, z: 10 });
    expect(a.yaw).toBe(256);
  });
});

describe("resolveCollision", () => {
  it("pushes two overlapping actors apart symmetrically along X", () => {
    const a = createActor({ id: 1, kind: "player", classId: "x", role: "damage", pos: { x: 0, y: 0, z: 0 }, maxHealth: 1, maxResource: 0 });
    const b = createActor({ id: 2, kind: "player", classId: "x", role: "damage", pos: { x: 100, y: 0, z: 0 }, maxHealth: 1, maxResource: 0 });
    resolveCollision(a, b, 100); // minDist = 200, current dist = 100, overlap = 100
    expect(a.pos.x).toBe(-50);
    expect(b.pos.x).toBe(150);
  });

  it("does nothing when actors are already far enough apart", () => {
    const a = createActor({ id: 1, kind: "player", classId: "x", role: "damage", pos: { x: 0, y: 0, z: 0 }, maxHealth: 1, maxResource: 0 });
    const b = createActor({ id: 2, kind: "player", classId: "x", role: "damage", pos: { x: 500, y: 0, z: 0 }, maxHealth: 1, maxResource: 0 });
    resolveCollision(a, b, 100);
    expect(a.pos.x).toBe(0);
    expect(b.pos.x).toBe(500);
  });

  it("handles exactly coincident positions without dividing by zero", () => {
    const a = createActor({ id: 1, kind: "player", classId: "x", role: "damage", pos: { x: 5, y: 0, z: 5 }, maxHealth: 1, maxResource: 0 });
    const b = createActor({ id: 2, kind: "player", classId: "x", role: "damage", pos: { x: 5, y: 0, z: 5 }, maxHealth: 1, maxResource: 0 });
    resolveCollision(a, b, 100);
    expect(a.pos.x).toBe(-95);
    expect(b.pos.x).toBe(105);
  });
});

describe("clampToBounds", () => {
  it("clamps position into the AABB", () => {
    const a = createActor({ id: 1, kind: "player", classId: "x", role: "damage", pos: { x: -500, y: 0, z: 5000 }, maxHealth: 1, maxResource: 0 });
    const bounds: Bounds = { minX: 0, maxX: 1000, minZ: 0, maxZ: 1000 };
    clampToBounds(a, bounds);
    expect(a.pos).toEqual({ x: 0, y: 0, z: 1000 });
  });
});
