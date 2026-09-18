/**
 * Tests for tab-target cycling and integer shape queries (sim/combat/targeting.ts).
 * Cone/line geometry is verified against hand-computed integer expectations (see file history);
 * the caster faces yaw 0, which the fixed-point table resolves to +X.
 */
import { describe, expect, it } from "vitest";
import { createActor } from "../../../sim/combat/actors";
import { cycleTarget, inCircle, inCone, inLine } from "../../../sim/combat/targeting";

function actorAt(id: number, x: number, z: number, yaw = 0) {
  return createActor({ id, kind: "enemy", classId: "x", role: "none", pos: { x, y: 0, z }, yaw, maxHealth: 100, maxResource: 0 });
}

describe("inCircle", () => {
  it("includes a target exactly on the radius and excludes one just outside", () => {
    const onEdge = actorAt(1, 60, 80); // dist 100
    const outside = actorAt(2, 100, 1); // dist2 10001 > 10000
    const ids = inCircle({ x: 0, y: 0, z: 0 }, 100, [onEdge, outside]);
    expect(ids).toEqual([1]);
  });
});

describe("inCone", () => {
  it("includes a target directly ahead within a 90-degree cone", () => {
    const caster = actorAt(9, 0, 0, 0);
    const ahead = actorAt(1, 100, 0);
    expect(inCone(caster, 256, 200, [caster, ahead])).toEqual([1]);
  });

  it("excludes a target 90 degrees to the side of a 90-degree cone", () => {
    const caster = actorAt(9, 0, 0, 0);
    const side = actorAt(1, 0, 100);
    expect(inCone(caster, 256, 200, [caster, side])).toEqual([]);
  });

  it("includes a target just inside the half-angle and excludes one just outside", () => {
    const caster = actorAt(9, 0, 0, 0);
    const inside = actorAt(1, 100, 90); // ~41.9 degrees < 45
    const outside = actorAt(2, 100, 110); // ~47.7 degrees > 45
    const ids = inCone(caster, 256, 200, [caster, inside, outside]);
    expect(ids).toEqual([1]);
  });

  it("excludes targets beyond the radius", () => {
    const caster = actorAt(9, 0, 0, 0);
    const far = actorAt(1, 300, 0);
    expect(inCone(caster, 256, 200, [caster, far])).toEqual([]);
  });
});

describe("inLine", () => {
  it("includes a target ahead and within the half-width, excludes one wider than it", () => {
    const caster = actorAt(9, 0, 0, 0);
    const inWidth = actorAt(1, 150, 20);
    const tooWide = actorAt(2, 150, 60);
    const ids = inLine(caster, 200, 50, [caster, inWidth, tooWide]);
    expect(ids).toEqual([1]);
  });

  it("excludes a target beyond the line length and one behind the caster", () => {
    const caster = actorAt(9, 0, 0, 0);
    const tooFar = actorAt(1, 250, 0);
    const behind = actorAt(2, -10, 0);
    expect(inLine(caster, 200, 50, [caster, tooFar, behind])).toEqual([]);
  });
});

describe("cycleTarget", () => {
  it("selects the lowest id when starting from null", () => {
    const actors = [actorAt(5, 0, 0), actorAt(2, 0, 0), actorAt(8, 0, 0)];
    expect(cycleTarget(actors, null)).toBe(2);
  });

  it("advances to the next id in ascending order and wraps around", () => {
    const actors = [actorAt(5, 0, 0), actorAt(2, 0, 0), actorAt(8, 0, 0)];
    expect(cycleTarget(actors, 2)).toBe(5);
    expect(cycleTarget(actors, 5)).toBe(8);
    expect(cycleTarget(actors, 8)).toBe(2);
  });

  it("skips dead actors", () => {
    const actors = [actorAt(5, 0, 0), actorAt(2, 0, 0), actorAt(8, 0, 0)];
    actors[0]!.alive = false; // id 5
    expect(cycleTarget(actors, 2)).toBe(8);
  });

  it("returns null when no actors are alive", () => {
    const actors = [actorAt(5, 0, 0)];
    actors[0]!.alive = false;
    expect(cycleTarget(actors, null)).toBeNull();
  });
});
