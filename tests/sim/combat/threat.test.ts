/**
 * Tests for the per-actor threat table (sim/combat/threat.ts).
 */
import { describe, expect, it } from "vitest";
import { createActor } from "../../../sim/combat/actors";
import { addThreat, clearThreat, highestThreatSource } from "../../../sim/combat/threat";

function actor() {
  return createActor({ id: 1, kind: "enemy", classId: "x", role: "none", pos: { x: 0, y: 0, z: 0 }, maxHealth: 100, maxResource: 0 });
}

describe("addThreat", () => {
  it("adds plain amounts with no multiplier", () => {
    const a = actor();
    addThreat(a, 5, 100);
    addThreat(a, 5, 50);
    expect(a.threat[5]).toBe(150);
  });

  it("applies a basis-point multiplier, truncating toward zero", () => {
    const a = actor();
    addThreat(a, 5, 100, 15000); // 150%
    expect(a.threat[5]).toBe(150);
    addThreat(a, 6, 100, 3333); // 33.33% -> 33
    expect(a.threat[6]).toBe(33);
  });
});

describe("highestThreatSource", () => {
  it("returns null with no threat entries", () => {
    expect(highestThreatSource(actor())).toBeNull();
  });

  it("returns the source with the highest value", () => {
    const a = actor();
    addThreat(a, 5, 100);
    addThreat(a, 6, 300);
    addThreat(a, 7, 200);
    expect(highestThreatSource(a)).toBe(6);
  });
});

describe("clearThreat", () => {
  it("removes only the given source's entry", () => {
    const a = actor();
    addThreat(a, 5, 100);
    addThreat(a, 6, 200);
    clearThreat(a, 5);
    expect(a.threat[5]).toBeUndefined();
    expect(a.threat[6]).toBe(200);
  });
});
