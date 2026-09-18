/**
 * Tests for AbilityDef validation (sim/combat/abilities.ts).
 */
import { describe, expect, it } from "vitest";
import { abilitySchool, validateAbilityDef, type AbilityDef } from "../../../sim/combat/abilities";

function baseDef(overrides: Partial<AbilityDef> = {}): AbilityDef {
  return {
    id: "fireball",
    castTicks: 30,
    gcdTicks: 15,
    cooldownTicks: 0,
    charges: 1,
    cost: { resource: "mana", amount: 20 },
    range: 30000,
    shape: { kind: "target" },
    interruptible: true,
    effects: [{ op: "damage", amount: 100, school: "fire" }],
    ...overrides,
  };
}

describe("validateAbilityDef", () => {
  it("accepts a well-formed target ability", () => {
    expect(validateAbilityDef(baseDef())).toEqual([]);
  });

  it("rejects a missing id", () => {
    expect(validateAbilityDef(baseDef({ id: "" }))).toContain("id is required");
  });

  it("rejects negative castTicks", () => {
    expect(validateAbilityDef(baseDef({ castTicks: -1 }))).toContain("castTicks must be an integer >= 0");
  });

  it("rejects zero charges", () => {
    expect(validateAbilityDef(baseDef({ charges: 0 }))).toContain("charges must be an integer >= 1");
  });

  it("requires radius and angle1024 for a cone shape", () => {
    const errs = validateAbilityDef(baseDef({ shape: { kind: "cone" } }));
    expect(errs).toContain("cone shape requires radius and angle1024");
  });

  it("requires radius for a circle shape", () => {
    const errs = validateAbilityDef(baseDef({ shape: { kind: "circle" } }));
    expect(errs).toContain("circle shape requires radius");
  });

  it("requires length and radius for a line shape", () => {
    const errs = validateAbilityDef(baseDef({ shape: { kind: "line" } }));
    expect(errs).toContain("line shape requires length and radius (half-width)");
  });

  it("rejects an ability with no effects", () => {
    expect(validateAbilityDef(baseDef({ effects: [] }))).toContain("effects must not be empty");
  });

  it("rejects applyEffect with zero stacks", () => {
    const errs = validateAbilityDef(
      baseDef({ effects: [{ op: "applyEffect", effectId: "burn", durationTicks: 40, stacks: 0 }] }),
    );
    expect(errs).toContain("applyEffect burn stacks must be an integer >= 1");
  });
});

describe("abilitySchool", () => {
  it("returns the school of the first damage effect", () => {
    expect(abilitySchool(baseDef())).toBe("fire");
  });

  it("defaults to physical when there is no damage effect", () => {
    const def = baseDef({ effects: [{ op: "heal", amount: 50 }] });
    expect(abilitySchool(def)).toBe("physical");
  });
});
